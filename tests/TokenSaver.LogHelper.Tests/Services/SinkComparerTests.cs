using TokenSaver.LogHelper.Models;
using TokenSaver.LogHelper.Normalization;
using TokenSaver.LogHelper.Services;
using Xunit;

namespace TokenSaver.LogHelper.Tests.Services;

public sealed class SinkComparerTests
{
    private static TemplateRow Row(string template, LogSeverity severity, int count) =>
        new(MessageNormalizer.TemplateHash(template), template, severity, count, null, null, template);

    [Fact]
    public void Compare_TemplateInBoth_ShowsBothCounts()
    {
        var rows = SinkComparer.Compare(
            new[] { Row("db timeout", LogSeverity.Error, 3) },
            new[] { Row("db timeout", LogSeverity.Error, 5) });

        var row = Assert.Single(rows);
        Assert.Equal(3, row.AppCount);
        Assert.Equal(5, row.SinkCount);
        Assert.Equal(string.Empty, row.Flag);
    }

    [Fact]
    public void Compare_InfoLevelInSink_FlaggedAsPruneCandidate()
    {
        var rows = SinkComparer.Compare(
            Array.Empty<TemplateRow>(),
            new[] { Row("heartbeat ok", LogSeverity.Information, 900) });

        var row = Assert.Single(rows);
        Assert.Equal("prune", row.Flag);
    }

    [Fact]
    public void Compare_ErrorOnlyInApp_FlaggedAsElevateCandidate()
    {
        var rows = SinkComparer.Compare(
            new[] { Row("payment gateway timeout", LogSeverity.Error, 4) },
            Array.Empty<TemplateRow>());

        var row = Assert.Single(rows);
        Assert.Equal("elevate", row.Flag);
    }

    [Fact]
    public void Compare_RespectsMaxTokens()
    {
        var app = Enumerable.Range(0, 300)
            .Select(i => Row("app event kind " + (char)('a' + i % 26) + (char)('a' + i / 26 % 26) + (char)('a' + i / 676 % 26), LogSeverity.Error, 1))
            .ToList();

        var text = SinkComparer.Format(SinkComparer.Compare(app, Array.Empty<TemplateRow>()), maxTokens: 50);

        Assert.True(text.Text.Length <= 50 * 4, $"length {text.Text.Length}");
        Assert.True(text.Truncated);
    }
}
