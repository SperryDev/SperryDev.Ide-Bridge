using TokenSaver.LogHelper.Models;
using TokenSaver.LogHelper.Normalization;
using TokenSaver.LogHelper.Security;

namespace TokenSaver.LogHelper.Services;

/// <summary>One distinct message shape: how often it occurred, its worst severity, when, and one redacted sample.</summary>
public sealed record TemplateRow(
    string Hash,
    string Template,
    LogSeverity MaxSeverity,
    int Count,
    DateTimeOffset? First,
    DateTimeOffset? Last,
    string Sample);

public static class TemplateAggregator
{
    private const int SampleChars = 200;

    public static IReadOnlyList<TemplateRow> Aggregate(IEnumerable<LogEntry> entries, LogHelperOptions options)
    {
        var groups = new Dictionary<string, Group>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (LogEntry entry in entries)
        {
            string template = MessageNormalizer.Normalize(entry.Message);
            if (!groups.TryGetValue(template, out var group))
            {
                group = new Group(entry);
                groups[template] = group;
                order.Add(template);
            }

            group.Add(entry);
        }

        return order.Select(template => groups[template].ToRow(template, options)).ToList();
    }

    private sealed class Group
    {
        private readonly LogEntry _first;
        private LogSeverity _max = LogSeverity.Unknown;
        private int _count;
        private DateTimeOffset? _firstSeen;
        private DateTimeOffset? _lastSeen;

        public Group(LogEntry first)
        {
            _first = first;
        }

        public void Add(LogEntry entry)
        {
            _count++;
            if (entry.Severity > _max)
            {
                _max = entry.Severity;
            }

            if (entry.Timestamp is { } ts)
            {
                _firstSeen = _firstSeen is null || ts < _firstSeen ? ts : _firstSeen;
                _lastSeen = _lastSeen is null || ts > _lastSeen ? ts : _lastSeen;
            }
        }

        public TemplateRow ToRow(string template, LogHelperOptions options)
        {
            string firstLine = _first.Raw.Split('\n')[0];
            if (firstLine.Length > SampleChars)
            {
                firstLine = firstLine[..SampleChars];
            }

            // The hash keys the template as normalized. The label is redacted, so secrets that appear in
            // message text never reach summaries or stored snapshots.
            return new TemplateRow(
                TemplateHashOf(template),
                Redactor.Redact(template, options),
                _max,
                _count,
                _firstSeen,
                _lastSeen,
                Redactor.Redact(firstLine, options));
        }

        private static string TemplateHashOf(string template) => MessageNormalizer.TemplateHash(template);
    }
}
