using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TokenSaver.LogHelper.Models;

namespace TokenSaver.LogHelper.Parsing;

/// <summary>
/// Turns raw lines into entries. Continuation lines (stack frames, indented messages, Python
/// tracebacks, Java "Caused by") fold into the preceding entry. Level detection is deliberately
/// strict: plain-text level words must be upper case and whole words.
/// </summary>
public sealed class LogParser
{
    private static readonly Regex TimestampPrefix = new(
        @"^(?<ts>\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:?\d{2})?)\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BracketHeader = new(
        @"^\[(?<lvl>[A-Za-z]{3,8})\]\s?(?<msg>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // VS Code output channel files: ["INFO" - 2:28:53 PM] message
    private static readonly Regex VsCodeChannelHeader = new(
        @"^\[""(?<lvl>[A-Za-z]{3,8})"" - [^\]]+\]\s?(?<msg>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MelHeader = new(
        @"^(?<lvl>trce|dbug|info|warn|fail|crit): (?<cat>.+?)(\[\d+\])?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PlainLevelWord = new(
        @"\b(FATAL|CRITICAL|ERROR|ERR|WARNING|WARN|INFO|DEBUG|TRACE)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TracebackHeader = new(
        @"^Traceback \(most recent call last\):",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex FirstChanceHeader = new(
        @"^Exception thrown: ",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly int _maxEntryBytes;

    public LogParser(LogHelperOptions options)
    {
        _maxEntryBytes = options.MaxEntryBytes;
    }

    public IReadOnlyList<LogEntry> Parse(IEnumerable<string> lines)
    {
        var entries = new List<LogEntry>();
        Pending? current = null;

        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (current is not null && IsContinuation(line, current))
            {
                current.Lines.Add(line);
                if (current.AwaitingMelMessage && char.IsWhiteSpace(line[0]))
                {
                    current.Message = line.Trim();
                    current.AwaitingMelMessage = false;
                }

                continue;
            }

            if (JsonLinesFormat.TryParse(line, out var jsonEntry) && jsonEntry is not null)
            {
                if (current is not null)
                {
                    entries.Add(Finish(current));
                    current = null;
                }

                entries.Add(CapJson(jsonEntry));
                continue;
            }

            if (current is not null)
            {
                entries.Add(Finish(current));
            }

            current = StartEntry(line);
        }

        if (current is not null)
        {
            entries.Add(Finish(current));
        }

        return entries;
    }

    private static bool IsHeader(string line) =>
        BracketHeader.IsMatch(TrimTimestamp(line).rest)
        || VsCodeChannelHeader.IsMatch(TrimTimestamp(line).rest)
        || MelHeader.IsMatch(line)
        || TracebackHeader.IsMatch(line)
        || FirstChanceHeader.IsMatch(line);

    private static bool IsContinuation(string line, Pending current)
    {
        if (char.IsWhiteSpace(line[0]))
        {
            return true;
        }

        if (line.StartsWith("at ", StringComparison.Ordinal) || line.StartsWith("Caused by:", StringComparison.Ordinal))
        {
            return true;
        }

        return current.Traceback && !IsHeader(line);
    }

    private Pending StartEntry(string line)
    {
        var (timestamp, rest) = TrimTimestamp(line);

        Match bracket = BracketHeader.Match(rest);
        if (bracket.Success && TryMapBracketLevel(bracket.Groups["lvl"].Value, out var bracketSeverity))
        {
            return new Pending(timestamp, bracketSeverity, null, bracket.Groups["msg"].Value, line);
        }

        Match channel = VsCodeChannelHeader.Match(rest);
        if (channel.Success && TryMapBracketLevel(channel.Groups["lvl"].Value, out var channelSeverity))
        {
            return new Pending(timestamp, channelSeverity, null, channel.Groups["msg"].Value, line);
        }

        Match mel = MelHeader.Match(line);
        if (mel.Success)
        {
            var pending = new Pending(timestamp, MapMelLevel(mel.Groups["lvl"].Value), mel.Groups["cat"].Value.Trim(), string.Empty, line);
            pending.AwaitingMelMessage = true;
            return pending;
        }

        if (TracebackHeader.IsMatch(rest))
        {
            return new Pending(timestamp, LogSeverity.Error, null, rest, line) { Traceback = true };
        }

        if (FirstChanceHeader.IsMatch(rest))
        {
            return new Pending(timestamp, LogSeverity.Unknown, null, rest, line) { FirstChance = true };
        }

        LogSeverity severity = LogSeverity.Unknown;
        Match word = PlainLevelWord.Match(rest);
        if (word.Success)
        {
            severity = MapPlainLevel(word.Value);
        }

        return new Pending(timestamp, severity, null, rest, line);
    }

    private LogEntry Finish(Pending pending)
    {
        string raw = string.Join('\n', pending.Lines);
        bool truncated = false;

        if (Encoding.UTF8.GetByteCount(raw) > _maxEntryBytes)
        {
            truncated = true;
            raw = TruncateToBytes(raw, _maxEntryBytes);
        }

        return new LogEntry(
            pending.Timestamp,
            pending.Severity,
            pending.Category,
            pending.Message,
            raw,
            pending.FirstChance,
            truncated);
    }

    private LogEntry CapJson(LogEntry entry)
    {
        if (Encoding.UTF8.GetByteCount(entry.Raw) <= _maxEntryBytes)
        {
            return entry;
        }

        return entry with { Raw = TruncateToBytes(entry.Raw, _maxEntryBytes), Truncated = true };
    }

    internal static string TruncateToBytes(string text, int maxBytes)
    {
        string cut = text.Length > maxBytes ? text[..maxBytes] : text;
        while (Encoding.UTF8.GetByteCount(cut) > maxBytes)
        {
            cut = cut[..^1];
        }

        return cut;
    }

    private static (DateTimeOffset? timestamp, string rest) TrimTimestamp(string line)
    {
        Match m = TimestampPrefix.Match(line);
        if (!m.Success)
        {
            return (null, line);
        }

        DateTimeOffset? timestamp = DateTimeOffset.TryParse(
            m.Groups["ts"].Value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;

        return (timestamp, line[m.Length..]);
    }

    private static bool TryMapBracketLevel(string tag, out LogSeverity severity)
    {
        switch (tag.ToUpperInvariant())
        {
            case "TRC":
            case "TRACE":
            case "TRCE":
                severity = LogSeverity.Trace;
                return true;
            case "DBG":
            case "DEBUG":
            case "DBUG":
                severity = LogSeverity.Debug;
                return true;
            case "INF":
            case "INFO":
            case "INFORMATION":
                severity = LogSeverity.Information;
                return true;
            case "WRN":
            case "WARN":
            case "WARNING":
                severity = LogSeverity.Warning;
                return true;
            case "ERR":
            case "ERROR":
            case "FAIL":
                severity = LogSeverity.Error;
                return true;
            case "FTL":
            case "FATAL":
            case "CRIT":
            case "CRITICAL":
                severity = LogSeverity.Critical;
                return true;
            default:
                severity = LogSeverity.Unknown;
                return false;
        }
    }

    private static LogSeverity MapMelLevel(string tag) => tag switch
    {
        "trce" => LogSeverity.Trace,
        "dbug" => LogSeverity.Debug,
        "info" => LogSeverity.Information,
        "warn" => LogSeverity.Warning,
        "fail" => LogSeverity.Error,
        "crit" => LogSeverity.Critical,
        _ => LogSeverity.Unknown,
    };

    private static LogSeverity MapPlainLevel(string word) => word switch
    {
        "FATAL" or "CRITICAL" => LogSeverity.Critical,
        "ERROR" or "ERR" => LogSeverity.Error,
        "WARNING" or "WARN" => LogSeverity.Warning,
        "INFO" => LogSeverity.Information,
        "DEBUG" => LogSeverity.Debug,
        "TRACE" => LogSeverity.Trace,
        _ => LogSeverity.Unknown,
    };

    private sealed class Pending
    {
        public Pending(DateTimeOffset? timestamp, LogSeverity severity, string? category, string message, string firstLine)
        {
            Timestamp = timestamp;
            Severity = severity;
            Category = category;
            Message = message;
            Lines = new List<string> { firstLine };
        }

        public DateTimeOffset? Timestamp { get; }

        public LogSeverity Severity { get; }

        public string? Category { get; }

        public string Message { get; set; }

        public List<string> Lines { get; }

        public bool AwaitingMelMessage { get; set; }

        public bool Traceback { get; set; }

        public bool FirstChance { get; set; }
    }
}
