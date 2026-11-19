using TokenSaver.LogHelper.Models;
using TokenSaver.LogHelper.Normalization;
using TokenSaver.LogHelper.State;
using TokenSaver.LogHelper.Services;
using Xunit;

namespace TokenSaver.LogHelper.Tests.State;

public sealed class RunDifferTests
{
    private static readonly DateTimeOffset Started = new(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);

    private static TemplateRow Row(string template, int count, LogSeverity severity = LogSeverity.Error) =>
        new(MessageNormalizer.TemplateHash(template), template, severity, count, Started, Started, template + " sample");

    private static RunSnapshot Snapshot(string runId, params TemplateRow[] rows) =>
        RunSnapshot.FromRows(runId, Started, rows);

    [Fact]
    public void New_Gone_CountChanged_Classified()
    {
        var previous = Snapshot("r1", Row("db timeout", 2), Row("disk full", 1), Row("retry <n>", 5));
        var current = Snapshot("r2", Row("db timeout", 7), Row("cache miss", 3), Row("retry <n>", 5));

        var diff = RunDiffer.Diff(previous, current);

        Assert.False(diff.BaselineReset);
        Assert.Equal(new[] { "cache miss" }, diff.New.Select(c => c.Template));
        Assert.Equal(new[] { "disk full" }, diff.Gone.Select(c => c.Template));
        var changed = Assert.Single(diff.Changed);
        Assert.Equal("db timeout", changed.Template);
        Assert.Equal(2, changed.PreviousCount);
        Assert.Equal(7, changed.CurrentCount);
    }

    [Fact]
    public void IdenticalSnapshots_EmptyDiff()
    {
        var a = Snapshot("r1", Row("x", 1), Row("y", 2));
        var b = Snapshot("r2", Row("x", 1), Row("y", 2));

        var diff = RunDiffer.Diff(a, b);

        Assert.Empty(diff.New);
        Assert.Empty(diff.Gone);
        Assert.Empty(diff.Changed);
        Assert.False(diff.BaselineReset);
    }

    [Fact]
    public void NormalizerVersionMismatch_ReturnsBaselineReset_NotEverythingNew()
    {
        var previous = Snapshot("r1", Row("x", 1)) with { NormalizerVersion = MessageNormalizer.Version - 1 };
        var current = Snapshot("r2", Row("x", 1), Row("y", 2));

        var diff = RunDiffer.Diff(previous, current);

        Assert.True(diff.BaselineReset);
        Assert.Empty(diff.New);
        Assert.Empty(diff.Gone);
        Assert.Empty(diff.Changed);
    }

    [Fact]
    public void Diff_IsIdempotent()
    {
        var previous = Snapshot("r1", Row("x", 1));
        var current = Snapshot("r2", Row("x", 4), Row("y", 1));

        var first = RunDiffer.Diff(previous, current);
        var second = RunDiffer.Diff(previous, current);

        // Lists inside the record compare by reference, so compare the contents of each list.
        Assert.Equal(first.New, second.New);
        Assert.Equal(first.Gone, second.Gone);
        Assert.Equal(first.Changed, second.Changed);
        Assert.Equal(first.BaselineReset, second.BaselineReset);
    }
}
