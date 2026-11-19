using TokenSaver.LogHelper.Models;

namespace TokenSaver.LogHelper.Parsing;

/// <summary>
/// Chooses one format for a whole channel. An explicit format wins. Otherwise the first non-empty lines
/// are tried against json, xml and logfmt, and the first format that parses most of them is used.
/// Anything else is plain text. Regex is never sniffed; it needs an explicit pattern.
/// </summary>
public static class FormatSniffer
{
    public const int SampleLines = 20;
    private const double MinimumHitRate = 0.8;

    private static readonly string[] Candidates = { "json", "xml", "logfmt" };

    /// <summary>Returns a concrete spec: a named format (json, xml, logfmt, regex) or plain.</summary>
    public static LogFormatSpec Resolve(LogFormatSpec? requested, IReadOnlyList<string> lines, LogHelperOptions options)
    {
        string? schema = requested?.Schema;
        string name = requested is null ? "auto" : StructuredParser.NameOf(requested);

        if (name is not ("auto" or "plain"))
        {
            return requested is null ? new LogFormatSpec("plain") : requested with { Name = name };
        }

        if (name == "plain")
        {
            return new LogFormatSpec("plain", null, schema);
        }

        List<string> sample = lines.Where(l => !string.IsNullOrWhiteSpace(l)).Take(SampleLines).ToList();
        if (sample.Count == 0)
        {
            return new LogFormatSpec("plain", null, schema);
        }

        foreach (string candidate in Candidates)
        {
            if (!StructuredParser.TryCreate(new LogFormatSpec(candidate), options, out StructuredParser? parser, out _) || parser is null)
            {
                continue;
            }

            int hits = sample.Count(line => parser.TryParse(line, out _));
            if (hits >= MinimumHitRate * sample.Count)
            {
                return new LogFormatSpec(candidate, null, schema);
            }
        }

        return new LogFormatSpec("plain", null, schema);
    }
}
