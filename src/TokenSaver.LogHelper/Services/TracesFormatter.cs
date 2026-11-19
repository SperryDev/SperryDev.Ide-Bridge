using System.Globalization;
using System.Text;
using TokenSaver.LogHelper.Parsing;

namespace TokenSaver.LogHelper.Services;

/// <summary>
/// Renders flagged traces, highest confidence first, within a character budget (about four characters per token).
/// Traces that do not fit are counted in a trailer, as in <see cref="SummaryFormatter"/>.
/// </summary>
public static class TracesFormatter
{
    private const int CharsPerToken = 4;
    private const int TrailerReserve = 64;
    private const int MessageChars = 160;

    public static SummaryText Format(IReadOnlyList<FlaggedTrace> traces, int maxTokens)
    {
        int budget = maxTokens * CharsPerToken;
        var ordered = traces
            .OrderByDescending(t => Rank(t.Confidence))
            .ThenByDescending(t => t.Count)
            .ThenBy(t => t.GroupKey, StringComparer.Ordinal)
            .ToList();

        var full = Pack(ordered, budget);
        if (full.Used == ordered.Count)
        {
            return new SummaryText(full.Text, Truncated: false, Omitted: 0);
        }

        var trimmed = Pack(ordered, budget - TrailerReserve);
        int omitted = ordered.Count - trimmed.Used;
        string trailer = $"+{omitted} more traces (raise maxTokens to see)";
        string text = trimmed.Used == 0 ? trailer : trimmed.Text + "\n" + trailer;
        return new SummaryText(text, Truncated: true, Omitted: omitted);
    }

    private static (string Text, int Used) Pack(IReadOnlyList<FlaggedTrace> traces, int budget)
    {
        var sb = new StringBuilder();
        int used = 0;
        foreach (FlaggedTrace trace in traces)
        {
            string line = Line(trace);
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

    private static string Line(FlaggedTrace trace)
    {
        string message = trace.Message.Length > MessageChars ? trace.Message[..MessageChars] : trace.Message;
        return string.Create(CultureInfo.InvariantCulture,
            $"{trace.Confidence.ToUpperInvariant()} x{trace.Count} {message} :: {trace.Reason} [{trace.GroupKey}]");
    }

    private static int Rank(string confidence) => confidence switch
    {
        "high" => 3,
        "medium" => 2,
        _ => 1,
    };
}
