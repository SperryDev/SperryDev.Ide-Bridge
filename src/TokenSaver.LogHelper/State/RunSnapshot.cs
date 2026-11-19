using TokenSaver.LogHelper.Models;
using TokenSaver.LogHelper.Normalization;
using TokenSaver.LogHelper.Services;

namespace TokenSaver.LogHelper.State;

/// <summary>Per-template state stored in a run snapshot. Holds counts and one redacted sample, never raw lines.</summary>
public sealed record TemplateSnapshot(string Template, LogSeverity MaxSeverity, int Count, string Sample);

/// <summary>
/// What one run looked like: template hash to counts. <see cref="NormalizerVersion"/> records which
/// masking rules produced the hashes, so a rules change yields a baseline reset instead of false "new" entries.
/// </summary>
public sealed record RunSnapshot(
    int SchemaVersion,
    int NormalizerVersion,
    string RunId,
    DateTimeOffset Started,
    IReadOnlyDictionary<string, TemplateSnapshot> Templates)
{
    public const int CurrentSchemaVersion = 1;

    public static RunSnapshot FromRows(string runId, DateTimeOffset started, IEnumerable<TemplateRow> rows)
    {
        var templates = new Dictionary<string, TemplateSnapshot>(StringComparer.Ordinal);
        foreach (TemplateRow row in rows)
        {
            templates[row.Hash] = new TemplateSnapshot(row.Template, row.MaxSeverity, row.Count, row.Sample);
        }

        return new RunSnapshot(CurrentSchemaVersion, MessageNormalizer.Version, runId, started, templates);
    }
}
