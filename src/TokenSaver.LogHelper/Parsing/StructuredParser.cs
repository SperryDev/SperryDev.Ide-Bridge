using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using TokenSaver.LogHelper.Filtering;
using TokenSaver.LogHelper.Models;

namespace TokenSaver.LogHelper.Parsing;

/// <summary>
/// Turns one line into a <see cref="LogEntry"/> with every parsed key in <see cref="LogEntry.Fields"/>, for
/// json, xml, logfmt or regex. Standard slots (timestamp, level, message, category, exception) are read from
/// well-known keys unless a schema maps them to other field names. Lines that do not parse fold into the
/// previous entry, so stack traces and wrapped output stay attached.
/// </summary>
public sealed class StructuredParser
{
    private static readonly string[] KnownNames = { "auto", "plain", "json", "xml", "logfmt", "regex" };
    private static readonly string[] SlotNames = { "timestamp", "level", "message", "category", "exception" };

    private static readonly string[] TimestampKeys = { "@t", "time", "timestamp", "ts" };
    private static readonly string[] LevelKeys = { "@l", "level", "severity", "lvl" };
    private static readonly string[] MessageKeys = { "@mt", "message", "msg", "@m" };
    private static readonly string[] CategoryKeys = { "SourceContext", "name", "logger", "category" };
    private static readonly string[] ExceptionKeys = { "@x", "exception", "stack" };

    private const int MaxFields = 256;
    private const int MaxValueChars = 4096;
    private const long MaxUnixMilliseconds = 253402300800000L;

    private readonly string _name;
    private readonly Dictionary<string, string>? _schema;
    private readonly Regex? _regex;
    private readonly int _maxEntryBytes;

    private StructuredParser(string name, Dictionary<string, string>? schema, Regex? regex, int maxEntryBytes)
    {
        _name = name;
        _schema = schema;
        _regex = regex;
        _maxEntryBytes = maxEntryBytes;
    }

    /// <summary>The format name after trimming and defaulting: auto, plain, json, xml, logfmt or regex.</summary>
    public static string NameOf(LogFormatSpec spec) =>
        string.IsNullOrWhiteSpace(spec.Name)
            ? (spec.Pattern is null ? "auto" : "regex")
            : spec.Name.Trim().ToLowerInvariant();

    /// <summary>Returns an error message for an invalid spec, or null when it is usable.</summary>
    public static string? Validate(LogFormatSpec? spec, LogHelperOptions options)
    {
        if (spec is null)
        {
            return null;
        }

        string name = NameOf(spec);
        if (Array.IndexOf(KnownNames, name) < 0)
        {
            return $"Unknown format '{spec.Name}'. Use auto, plain, json, xml, logfmt or regex.";
        }

        if (spec.Pattern is not null && name != "regex")
        {
            return "A pattern applies only to format regex.";
        }

        if (name == "regex")
        {
            if (string.IsNullOrEmpty(spec.Pattern))
            {
                return "Format regex needs a pattern with named groups.";
            }

            if (!SafeRegex.TryCreate(spec.Pattern, options, out _, out string? regexError))
            {
                return regexError;
            }
        }

        return TryParseSchema(spec.Schema, out _, out string? schemaError) ? null : schemaError;
    }

    /// <summary>Builds a parser for a concrete format (json, xml, logfmt or regex). Auto and plain must be resolved first.</summary>
    public static bool TryCreate(LogFormatSpec spec, LogHelperOptions options, out StructuredParser? parser, out string? error)
    {
        parser = null;
        error = Validate(spec, options);
        if (error is not null)
        {
            return false;
        }

        string name = NameOf(spec);
        if (name is "auto" or "plain")
        {
            error = "Format must be resolved before parsing.";
            return false;
        }

        _ = TryParseSchema(spec.Schema, out Dictionary<string, string>? schema, out _);
        Regex? regex = null;
        if (name == "regex")
        {
            _ = SafeRegex.TryCreate(spec.Pattern!, options, out regex, out _);
        }

        parser = new StructuredParser(name, schema, regex, options.MaxEntryBytes);
        return true;
    }

    public string Name => _name;

    public bool TryParse(string line, out LogEntry? entry)
    {
        entry = null;
        return _name switch
        {
            "json" => TryJson(line, out entry),
            "xml" => TryXml(line, out entry),
            "logfmt" => TryLogfmt(line, out entry),
            "regex" => TryRegex(line, out entry),
            _ => false,
        };
    }

    /// <summary>Parses many lines. Lines that do not parse fold into the previous entry.</summary>
    public IReadOnlyList<LogEntry> ParseLines(IEnumerable<string> lines)
    {
        var entries = new List<LogEntry>();
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (TryParse(line, out LogEntry? parsed) && parsed is not null)
            {
                entries.Add(parsed);
                continue;
            }

            if (entries.Count == 0)
            {
                entries.Add(new LogEntry(null, LogSeverity.Unknown, null, line, line));
                continue;
            }

            LogEntry last = entries[^1];
            string joined = last.Raw + "\n" + line;
            string capped = Cap(joined, out bool truncated);
            entries[^1] = last with { Raw = capped, Truncated = last.Truncated || truncated };
        }

        return entries;
    }

    /// <summary>Parses a schema JSON object of slot to field name. Empty input means no schema.</summary>
    public static bool TryParseSchema(string? schema, out Dictionary<string, string>? map, out string? error)
    {
        map = null;
        error = null;
        if (string.IsNullOrWhiteSpace(schema))
        {
            return true;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(schema);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "Schema must be a JSON object that maps slots to field names.";
                return false;
            }

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                string slot = property.Name.ToLowerInvariant();
                if (Array.IndexOf(SlotNames, slot) < 0)
                {
                    error = $"Unknown schema slot '{property.Name}'. Use timestamp, level, message, category or exception.";
                    return false;
                }

                if (property.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.Value.GetString()))
                {
                    error = $"Schema slot '{property.Name}' must map to a non-empty field name.";
                    return false;
                }

                result[slot] = property.Value.GetString()!;
            }

            map = result;
            return true;
        }
        catch (JsonException)
        {
            error = "Schema is not valid JSON.";
            return false;
        }
    }

    private bool TryJson(string line, out LogEntry? entry)
    {
        entry = null;
        if (line.Length == 0 || line[0] != '{')
        {
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var fields = NewFields();
            FlattenJson(document.RootElement, string.Empty, fields);
            entry = Build(line, fields, LogSeverity.Information);
            return true;
        }
    }

    private bool TryXml(string line, out LogEntry? entry)
    {
        entry = null;
        if (line.Length == 0 || line[0] != '<')
        {
            return false;
        }

        XDocument document;
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using XmlReader reader = XmlReader.Create(new StringReader(line), settings);
            document = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return false;
        }

        XElement root = document.Root!;
        var fields = NewFields();
        AddField(fields, "element", root.Name.LocalName);
        FlattenXml(root, string.Empty, fields);
        entry = Build(line, fields, LogSeverity.Unknown);
        return true;
    }

    private bool TryLogfmt(string line, out LogEntry? entry)
    {
        entry = null;
        var fields = NewFields();
        int i = 0;
        int n = line.Length;
        while (i < n)
        {
            while (i < n && char.IsWhiteSpace(line[i]))
            {
                i++;
            }

            if (i >= n)
            {
                break;
            }

            int keyStart = i;
            while (i < n && line[i] != '=' && !char.IsWhiteSpace(line[i]))
            {
                i++;
            }

            string key = line[keyStart..i];
            if (i >= n || line[i] != '=' || key.Length == 0)
            {
                if (i == keyStart)
                {
                    i++;
                }

                continue;
            }

            i++;
            string value;
            if (i < n && line[i] == '"')
            {
                i++;
                var sb = new StringBuilder();
                while (i < n && line[i] != '"')
                {
                    if (line[i] == '\\' && i + 1 < n)
                    {
                        i++;
                    }

                    sb.Append(line[i]);
                    i++;
                }

                i++;
                value = sb.ToString();
            }
            else
            {
                int valueStart = i;
                while (i < n && !char.IsWhiteSpace(line[i]))
                {
                    i++;
                }

                value = line[valueStart..i];
            }

            AddField(fields, key, value);
        }

        if (fields.Count == 0)
        {
            return false;
        }

        entry = Build(line, fields, LogSeverity.Unknown);
        return true;
    }

    private bool TryRegex(string line, out LogEntry? entry)
    {
        entry = null;
        if (_regex is null)
        {
            return false;
        }

        Match match = _regex.Match(line);
        if (!match.Success)
        {
            return false;
        }

        var fields = NewFields();
        foreach (Group group in match.Groups)
        {
            if (group.Success && !int.TryParse(group.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                AddField(fields, group.Name, group.Value);
            }
        }

        entry = Build(line, fields, LogSeverity.Unknown);
        return true;
    }

    private LogEntry Build(string line, Dictionary<string, string> fields, LogSeverity defaultSeverity)
    {
        DateTimeOffset? timestamp = ParseTimestamp(Pick(fields, "timestamp", TimestampKeys));
        string? level = Pick(fields, "level", LevelKeys);
        LogSeverity severity = level is null ? defaultSeverity : MapLevel(level) ?? LogSeverity.Unknown;
        string message = Pick(fields, "message", MessageKeys) ?? line;
        string? category = Pick(fields, "category", CategoryKeys);
        string? exception = Pick(fields, "exception", ExceptionKeys);

        string raw = exception is null ? line : line + "\n" + exception;
        raw = Cap(raw, out bool truncated);

        return new LogEntry(timestamp, severity, category, message, raw, false, truncated, fields);
    }

    private string Cap(string text, out bool truncated)
    {
        truncated = Encoding.UTF8.GetByteCount(text) > _maxEntryBytes;
        return truncated ? LogParser.TruncateToBytes(text, _maxEntryBytes) : text;
    }

    private string? Pick(Dictionary<string, string> fields, string slot, string[] defaults)
    {
        if (_schema is not null && _schema.TryGetValue(slot, out string? mapped))
        {
            return fields.TryGetValue(mapped, out string? mappedValue) ? mappedValue : null;
        }

        foreach (string key in defaults)
        {
            if (fields.TryGetValue(key, out string? value))
            {
                return value;
            }
        }

        return null;
    }

    private static Dictionary<string, string> NewFields() => new(StringComparer.OrdinalIgnoreCase);

    private static void AddField(Dictionary<string, string> fields, string key, string value)
    {
        if (key.Length == 0 || (fields.Count >= MaxFields && !fields.ContainsKey(key)))
        {
            return;
        }

        fields[key] = value.Length > MaxValueChars ? value[..MaxValueChars] : value;
    }

    private static string Join(string prefix, string name) =>
        prefix.Length == 0 ? name : prefix + "." + name;

    private static void FlattenJson(JsonElement element, string prefix, Dictionary<string, string> fields)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    FlattenJson(property.Value, Join(prefix, property.Name), fields);
                }

                break;
            case JsonValueKind.Array:
                int index = 0;
                foreach (JsonElement item in element.EnumerateArray())
                {
                    FlattenJson(item, Join(prefix, index.ToString(CultureInfo.InvariantCulture)), fields);
                    index++;
                }

                break;
            case JsonValueKind.String:
                AddField(fields, prefix, element.GetString() ?? string.Empty);
                break;
            default:
                AddField(fields, prefix, element.GetRawText());
                break;
        }
    }

    // Root attributes keep their plain names so <event level="ERROR"> reads like a level field.
    // Nested attributes become "parent.@name".
    private static void FlattenXml(XElement element, string prefix, Dictionary<string, string> fields)
    {
        foreach (XAttribute attribute in element.Attributes())
        {
            string key = prefix.Length == 0 ? attribute.Name.LocalName : Join(prefix, "@" + attribute.Name.LocalName);
            AddField(fields, key, attribute.Value);
        }

        foreach (XElement child in element.Elements())
        {
            string key = Join(prefix, child.Name.LocalName);
            if (child.HasElements || child.HasAttributes)
            {
                FlattenXml(child, key, fields);
            }

            if (!child.HasElements)
            {
                AddField(fields, key, child.Value);
            }
        }
    }

    private static DateTimeOffset? ParseTimestamp(string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long milliseconds)
            && milliseconds > 0 && milliseconds < MaxUnixMilliseconds)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed))
        {
            return parsed;
        }

        return null;
    }

    private static LogSeverity? MapLevel(string value)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long numeric)
            && numeric is >= 0 and <= int.MaxValue)
        {
            return MapPino((int)numeric);
        }

        return value.ToLowerInvariant() switch
        {
            "verbose" or "trace" or "trce" or "trc" => LogSeverity.Trace,
            "debug" or "dbug" or "dbg" => LogSeverity.Debug,
            "information" or "info" or "inf" => LogSeverity.Information,
            "warning" or "warn" or "wrn" => LogSeverity.Warning,
            "error" or "err" or "fail" => LogSeverity.Error,
            "fatal" or "critical" or "crit" or "ftl" => LogSeverity.Critical,
            _ => null,
        };
    }

    private static LogSeverity MapPino(int level) => level switch
    {
        <= 10 => LogSeverity.Trace,
        <= 20 => LogSeverity.Debug,
        <= 30 => LogSeverity.Information,
        <= 40 => LogSeverity.Warning,
        <= 50 => LogSeverity.Error,
        _ => LogSeverity.Critical,
    };
}
