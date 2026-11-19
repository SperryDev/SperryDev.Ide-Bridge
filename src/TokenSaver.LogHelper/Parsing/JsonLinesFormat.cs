using System.Globalization;
using System.Text.Json;
using TokenSaver.LogHelper.Models;

namespace TokenSaver.LogHelper.Parsing;

/// <summary>
/// Single-line JSON log records: Serilog compact (@t/@l/@mt/@x), pino (numeric level, msg, time),
/// and generic objects (level, message, timestamp). Returns false for anything that is not a JSON object.
/// </summary>
public static class JsonLinesFormat
{
    public static bool TryParse(string line, out LogEntry? entry)
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
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            LogSeverity severity = ReadSeverity(root);
            string message = FirstString(root, "@mt", "message", "msg", "@m") ?? string.Empty;
            string? category = FirstString(root, "SourceContext", "name", "logger", "category");
            string raw = line;

            string? exception = FirstString(root, "@x", "exception", "stack");
            if (exception is not null)
            {
                raw = line + "\n" + exception;
            }

            entry = new LogEntry(
                ReadTimestamp(root),
                severity,
                category,
                message,
                raw);
            return true;
        }
    }

    private static LogSeverity ReadSeverity(JsonElement root)
    {
        if (root.TryGetProperty("@l", out var serilog) && serilog.ValueKind == JsonValueKind.String)
        {
            return MapWord(serilog.GetString()) ?? LogSeverity.Information;
        }

        if (root.TryGetProperty("level", out var level))
        {
            if (level.ValueKind == JsonValueKind.Number && level.TryGetInt32(out int numeric))
            {
                return MapPino(numeric);
            }

            if (level.ValueKind == JsonValueKind.String)
            {
                return MapWord(level.GetString()) ?? LogSeverity.Unknown;
            }
        }

        return LogSeverity.Information;
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

    private static LogSeverity? MapWord(string? word) => word?.ToLowerInvariant() switch
    {
        "verbose" or "trace" or "trce" => LogSeverity.Trace,
        "debug" or "dbug" => LogSeverity.Debug,
        "information" or "info" => LogSeverity.Information,
        "warning" or "warn" => LogSeverity.Warning,
        "error" or "err" or "fail" => LogSeverity.Error,
        "fatal" or "critical" or "crit" => LogSeverity.Critical,
        _ => null,
    };

    private static DateTimeOffset? ReadTimestamp(JsonElement root)
    {
        foreach (string name in new[] { "@t", "time", "timestamp" })
        {
            if (!root.TryGetProperty(name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long epochMs))
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(epochMs);
            }

            if (value.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static string? FirstString(JsonElement root, params string[] names)
    {
        foreach (string name in names)
        {
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }
}
