using System.Globalization;
using System.Text;
using TokenSaver.LogHelper.Models;

namespace TokenSaver.LogHelper.Services;

/// <summary>One message shape seen in an application log and/or a system sink, with counts from each.</summary>
public sealed record SinkRow(string Template, LogSeverity Severity, int AppCount, int SinkCount, string Flag);

/// <summary>
/// Compares an application log with a system sink (Event Log or syslog). It flags candidates only; nothing is written.
/// "prune": a sink holds information-level messages, so they are noise there or can be dropped at the source.
/// "elevate": an application logs an error that the sink never sees, so it may need to reach the system log.
/// </summary>
public static class SinkComparer
{
    private const int CharsPerToken = 4;
    private const int TrailerReserve = 64;
    private const int TemplateChars = 160;

    public static IReadOnlyList<SinkRow> Compare(IReadOnlyList<TemplateRow> app, IReadOnlyList<TemplateRow> sink)
    {
        var appByHash = app.ToDictionary(r => r.Hash, StringComparer.Ordinal);
        var sinkByHash = sink.ToDictionary(r => r.Hash, StringComparer.Ordinal);
        var rows = new List<SinkRow>();

        foreach (string hash in appByHash.Keys.Union(sinkByHash.Keys, StringComparer.Ordinal))
        {
            appByHash.TryGetValue(hash, out TemplateRow? a);
            sinkByHash.TryGetValue(hash, out TemplateRow? s);

            LogSeverity severity = a?.MaxSeverity ?? s!.MaxSeverity;
            if (s is not null && s.Count > 0 && severity <= LogSeverity.Information)
            {
                rows.Add(new SinkRow(a?.Template ?? s.Template, severity, a?.Count ?? 0, s.Count, "prune"));
            }
            else if (a is not null && a.Count > 0 && s is null && severity >= LogSeverity.Error)
            {
                rows.Add(new SinkRow(a.Template, severity, a.Count, 0, "elevate"));
            }
            else
            {
                rows.Add(new SinkRow(a?.Template ?? s!.Template, severity, a?.Count ?? 0, s?.Count ?? 0, string.Empty));
            }
        }

        return rows;
    }

    public static SummaryText Format(IReadOnlyList<SinkRow> rows, int maxTokens)
    {
        int budget = maxTokens * CharsPerToken;
        var ordered = rows
            .OrderBy(r => r.Flag.Length == 0 ? 1 : 0)
            .ThenByDescending(r => r.AppCount + r.SinkCount)
            .ThenBy(r => r.Template, StringComparer.Ordinal)
            .ToList();

        var full = Pack(ordered, budget);
        if (full.Used == ordered.Count)
        {
            return new SummaryText(full.Text, Truncated: false, Omitted: 0);
        }

        var trimmed = Pack(ordered, budget - TrailerReserve);
        int omitted = ordered.Count - trimmed.Used;
        string trailer = $"+{omitted} more rows (raise maxTokens to see)";
        string text = trimmed.Used == 0 ? trailer : trimmed.Text + "\n" + trailer;
        return new SummaryText(text, Truncated: true, Omitted: omitted);
    }

    private static (string Text, int Used) Pack(IReadOnlyList<SinkRow> rows, int budget)
    {
        var sb = new StringBuilder();
        int used = 0;
        foreach (SinkRow row in rows)
        {
            string line = Line(row);
            int needed = sb.Length == 0 ? line.Length : line.Length + 1;
            if (sb.Length + needed > budget)
            {
                break;
            }

            if (sb.Length > 0)
            {
                sb.Append('\n');
            }

            sb.Append(line);
            used++;
        }

        return (sb.ToString(), used);
    }

    private static string Line(SinkRow row)
    {
        string template = row.Template.Length > TemplateChars ? row.Template[..TemplateChars] : row.Template;
        string flag = row.Flag.Length == 0 ? "keep" : row.Flag.ToUpperInvariant();
        return string.Create(CultureInfo.InvariantCulture,
            $"{flag} app={row.AppCount} sink={row.SinkCount} {row.Severity} {template}");
    }
}
