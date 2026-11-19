using TokenSaver.LogHelper.Models;
using TokenSaver.LogHelper.Normalization;

namespace TokenSaver.LogHelper.Parsing;

/// <summary>
/// A stack trace the helper flagged. <see cref="Confidence"/> is "high", "medium" or "low" and <see cref="Reason"/>
/// says why. Traces with the same top frames share a <see cref="GroupKey"/>.
/// </summary>
public sealed record FlaggedTrace(
    string GroupKey,
    string Message,
    string Confidence,
    string Reason,
    LogSeverity Severity,
    int Count,
    DateTimeOffset? First,
    DateTimeOffset? Last,
    string Sample);

/// <summary>
/// Flags stack traces from folded entries. This is a heuristic on log text, not proof of an unhandled exception:
/// a log file cannot show whether a catch block handled an error.
/// <list type="bullet">
/// <item><description>High: a runtime marker that means the exception escaped (.NET unhandled, Node unhandled rejection or uncaught exception, Java exception in thread).</description></item>
/// <item><description>Medium: a trace logged below Error (Information, Debug, Trace or no level).</description></item>
/// <item><description>Low: a trace logged at Error or Critical with no escape marker, so it was probably handled.</description></item>
/// </list>
/// First-chance exceptions are never flagged.
/// </summary>
public static class TraceFlagger
{
    private static readonly string[] EscapeMarkers =
    {
        "Unhandled exception.",
        "AppDomain.UnhandledException",
        "unhandledRejection",
        "uncaughtException",
        "Exception in thread",
    };

    private const int SampleChars = 200;
    private const int GroupFrames = 3;

    public static IReadOnlyList<FlaggedTrace> Flag(IEnumerable<LogEntry> entries)
    {
        var groups = new Dictionary<string, Group>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (LogEntry entry in entries)
        {
            if (entry.FirstChanceException || !HasTrace(entry))
            {
                continue;
            }

            (string confidence, string reason) = Rate(entry);
            string key = GroupKeyOf(entry);
            if (!groups.TryGetValue(key, out Group? group))
            {
                group = new Group(key);
                groups[key] = group;
                order.Add(key);
            }

            group.Add(entry, confidence, reason);
        }

        return order.Select(k => groups[k].ToTrace()).ToList();
    }

    private static bool HasTrace(LogEntry entry) =>
        EscapeMarkers.Any(entry.Raw.Contains)
        || FrameLines(entry.Raw).Count > 0
        || entry.Raw.Contains("Caused by:", StringComparison.Ordinal)
        || entry.Raw.StartsWith("Traceback", StringComparison.Ordinal);

    private static (string Confidence, string Reason) Rate(LogEntry entry)
    {
        string? marker = EscapeMarkers.FirstOrDefault(entry.Raw.Contains);
        if (marker is not null)
        {
            return ("high", $"Escape marker '{marker}' in the trace.");
        }

        if (entry.Severity >= LogSeverity.Error)
        {
            return ("low", $"Logged at {entry.Severity}; probably handled.");
        }

        return ("medium", $"Trace logged at {entry.Severity}, not as an error.");
    }

    private static string GroupKeyOf(LogEntry entry)
    {
        List<string> frames = FrameLines(entry.Raw);
        string basis = frames.Count > 0
            ? string.Join("\n", frames.Take(GroupFrames))
            : entry.Message;
        return MessageNormalizer.TemplateHash(MessageNormalizer.Normalize(basis));
    }

    private static List<string> FrameLines(string raw)
    {
        var frames = new List<string>();
        foreach (string line in raw.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("at ", StringComparison.Ordinal) || trimmed.StartsWith("File \"", StringComparison.Ordinal))
            {
                frames.Add(trimmed);
            }
        }

        return frames;
    }

    private sealed class Group
    {
        private readonly string _key;
        private string _confidence = "low";
        private string _reason = string.Empty;
        private string _message = string.Empty;
        private string _sample = string.Empty;
        private LogSeverity _severity = LogSeverity.Unknown;
        private int _count;
        private DateTimeOffset? _first;
        private DateTimeOffset? _last;

        public Group(string key)
        {
            _key = key;
        }

        public void Add(LogEntry entry, string confidence, string reason)
        {
            if (_count == 0 || Rank(confidence) > Rank(_confidence))
            {
                _confidence = confidence;
                _reason = reason;
                _message = entry.Message;
                _sample = entry.Raw.Length > SampleChars ? entry.Raw[..SampleChars] : entry.Raw;
            }

            if (entry.Severity > _severity)
            {
                _severity = entry.Severity;
            }

            _count++;
            if (entry.Timestamp is { } ts)
            {
                _first = _first is null || ts < _first ? ts : _first;
                _last = _last is null || ts > _last ? ts : _last;
            }
        }

        public FlaggedTrace ToTrace() =>
            new(_key, _message, _confidence, _reason, _severity, _count, _first, _last, _sample);

        private static int Rank(string confidence) => confidence switch
        {
            "high" => 3,
            "medium" => 2,
            _ => 1,
        };
    }
}
