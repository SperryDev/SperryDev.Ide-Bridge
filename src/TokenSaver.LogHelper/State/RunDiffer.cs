using TokenSaver.LogHelper.Models;

namespace TokenSaver.LogHelper.State;

public sealed record TemplateChange(string Template, LogSeverity Severity, int PreviousCount, int CurrentCount, string Sample);

public sealed record RunDiff(
    IReadOnlyList<TemplateChange> New,
    IReadOnlyList<TemplateChange> Gone,
    IReadOnlyList<TemplateChange> Changed,
    bool BaselineReset);

/// <summary>
/// Pure comparison of two snapshots. No I/O, so the same inputs always give the same diff.
/// When normalizer or schema versions differ, the hashes are not comparable and the result is a baseline reset.
/// </summary>
public static class RunDiffer
{
    public static RunDiff Diff(RunSnapshot previous, RunSnapshot current)
    {
        if (previous.NormalizerVersion != current.NormalizerVersion || previous.SchemaVersion != current.SchemaVersion)
        {
            return new RunDiff(Array.Empty<TemplateChange>(), Array.Empty<TemplateChange>(), Array.Empty<TemplateChange>(), BaselineReset: true);
        }

        var added = new List<TemplateChange>();
        var removed = new List<TemplateChange>();
        var changed = new List<TemplateChange>();

        foreach ((string hash, TemplateSnapshot now) in current.Templates.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!previous.Templates.TryGetValue(hash, out TemplateSnapshot? before))
            {
                added.Add(new TemplateChange(now.Template, now.MaxSeverity, 0, now.Count, now.Sample));
            }
            else if (before.Count != now.Count)
            {
                changed.Add(new TemplateChange(now.Template, now.MaxSeverity, before.Count, now.Count, now.Sample));
            }
        }

        foreach ((string hash, TemplateSnapshot before) in previous.Templates.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!current.Templates.ContainsKey(hash))
            {
                removed.Add(new TemplateChange(before.Template, before.MaxSeverity, before.Count, 0, before.Sample));
            }
        }

        return new RunDiff(added, removed, changed, BaselineReset: false);
    }
}
