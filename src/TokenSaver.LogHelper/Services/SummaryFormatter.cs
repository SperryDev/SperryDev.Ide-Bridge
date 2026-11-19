using System.Globalization;
using System.Text;
using TokenSaver.LogHelper.Models;

namespace TokenSaver.LogHelper.Services;

public sealed record SummaryText(string Text, bool Truncated, int Omitted);

/// <summary>
/// Renders template rows as compact text lines, most severe first, within a character budget
/// (about four characters per token). Rows that do not fit are counted in a trailer.
/// </summary>
public static class SummaryFormatter
{
    private const int CharsPerToken = 4;
    private const int TrailerReserve = 64;
    private const int SampleChars = 120;

    public static SummaryText Format(IReadOnlyList<TemplateRow> rows, int maxTokens)
    {
        int budget = maxTokens * CharsPerToken;
        var ordered = rows
            .OrderByDescending(r => r.MaxSeverity)
            .ThenByDescending(r => r.Count)
            .ThenBy(r => r.Template, StringComparer.Ordinal)
            .ToList();

        var full = Pack(ordered, budget);
        if (full.Used == ordered.Count)
        {
            return new SummaryText(full.Text, Truncated: false, Omitted: 0);
        }

        var trimmed = Pack(ordered, budget - TrailerReserve);
        int omitted = ordered.Count - trimmed.Used;
        string trailer = $"+{omitted} more templates (raise maxTokens to see)";
        string text = trimmed.Used == 0 ? trailer : trimmed.Text + "\n" + trailer;
        return new SummaryText(text, Truncated: true, Omitted: omitted);
    }

    private static (string Text, int Used) Pack(IReadOnlyList<TemplateRow> rows, int budget)
    {
        var sb = new StringBuilder();
        int used = 0;
        foreach (TemplateRow row in rows)
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

    private static string Line(TemplateRow row)
    {
        string sample = row.Sample.Length > SampleChars ? row.Sample[..SampleChars] : row.Sample;
        return string.Create(CultureInfo.InvariantCulture,
            $"{Tag(row.MaxSeverity)} x{row.Count} {row.Template} :: {sample}");
    }

    private static string Tag(LogSeverity severity) => severity switch
    {
        LogSeverity.Critical => "CRIT",
        LogSeverity.Error => "ERR",
        LogSeverity.Warning => "WRN",
        LogSeverity.Information => "INF",
        LogSeverity.Debug => "DBG",
        LogSeverity.Trace => "TRC",
        _ => "UNK",
    };
}
