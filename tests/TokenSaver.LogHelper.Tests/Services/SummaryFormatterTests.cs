using TokenSaver.LogHelper.Models;
using TokenSaver.LogHelper.Services;
using Xunit;

namespace TokenSaver.LogHelper.Tests.Services;

public sealed class SummaryFormatterTests
{
    private static readonly LogHelperOptions Options = new();

    private static LogEntry Entry(LogSeverity severity, string message, DateTimeOffset? ts = null, string? raw = null) =>
        new(ts, severity, null, message, raw ?? message);

    [Fact]
    public void Aggregates_SameTemplate_CountsAndFirstLast()
    {
        var t1 = new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);
        var t3 = t1.AddMinutes(2);
        var entries = new[]
        {
            Entry(LogSeverity.Information, "user 1 failed", t1),
            Entry(LogSeverity.Information, "user 2 failed", t1.AddMinutes(1)),
            Entry(LogSeverity.Information, "user 3 failed", t3),
        };

        var rows = TemplateAggregator.Aggregate(entries, Options);

        var row = Assert.Single(rows);
        Assert.Equal(3, row.Count);
        Assert.Equal(t1, row.First);
        Assert.Equal(t3, row.Last);
    }

    [Fact]
    public void Orders_ErrorsBeforeInfo_ThenByCount()
    {
        var entries = new[]
        {
            Entry(LogSeverity.Information, "noisy"),
            Entry(LogSeverity.Information, "noisy"),
            Entry(LogSeverity.Information, "noisy"),
            Entry(LogSeverity.Error, "boom"),
            Entry(LogSeverity.Warning, "careful"),
        };

        var rows = TemplateAggregator.Aggregate(entries, Options);
        var text = SummaryFormatter.Format(rows, maxTokens: 1500).Text;

        int boom = text.IndexOf("boom", StringComparison.Ordinal);
        int careful = text.IndexOf("careful", StringComparison.Ordinal);
        int noisy = text.IndexOf("noisy", StringComparison.Ordinal);
        Assert.True(boom >= 0 && boom < careful && careful < noisy);
    }

    [Fact]
    public void MaxTokens_Cap_NeverExceeded_AndErrorsSurvive()
    {
        // Digits are masked by the normalizer, so use letters to get 1000 distinct templates.
        var entries = Enumerable.Range(0, 1000)
            .Select(i => Entry(LogSeverity.Information,
                $"routine event {(char)('a' + i % 26)}{(char)('a' + i / 26 % 26)}{(char)('a' + i / 676 % 26)} started"))
            .Append(Entry(LogSeverity.Error, "database connection lost"))
            .ToList();

        var rows = TemplateAggregator.Aggregate(entries, Options);
        var summary = SummaryFormatter.Format(rows, maxTokens: 50);

        Assert.True(summary.Text.Length <= 50 * 4, $"length {summary.Text.Length}");
        Assert.Contains("database connection lost", summary.Text, StringComparison.Ordinal);
        Assert.True(summary.Truncated);
        Assert.True(summary.Omitted > 0);
    }

    [Fact]
    public void Sample_IsRedacted()
    {
        var entries = new[] { Entry(LogSeverity.Error, "login failed", raw: "login failed password=hunter2") };

        var rows = TemplateAggregator.Aggregate(entries, Options);
        var text = SummaryFormatter.Format(rows, maxTokens: 1500).Text;

        Assert.DoesNotContain("hunter2", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Output_IsLinesNotJsonPerEntry()
    {
        var entries = new[]
        {
            Entry(LogSeverity.Error, "a"),
            Entry(LogSeverity.Warning, "b"),
        };

        var text = SummaryFormatter.Format(TemplateAggregator.Aggregate(entries, Options), maxTokens: 1500).Text;

        Assert.DoesNotContain("{", text, StringComparison.Ordinal);
        Assert.Contains('\n', text);
    }
}
