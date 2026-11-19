using TokenSaver.LogHelper.Filtering;
using TokenSaver.LogHelper.Models;
using Xunit;

namespace TokenSaver.LogHelper.Tests.Filtering;

public sealed class LogFilterFieldTests
{
    private static readonly LogHelperOptions Options = new();

    private static LogEntry Entry(string service, string level) =>
        new(null, LogSeverity.Error, null, "m", "m", false, false,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["service"] = service, ["level"] = level });

    [Fact]
    public void FieldTerm_MatchesCaseInsensitively()
    {
        var entries = new[] { Entry("api", "Error"), Entry("billing", "Error") };
        var spec = new LogFilterSpec { FieldTerms = new[] { new FieldTerm("SERVICE", "API") } };

        var result = LogFilter.Apply(entries, spec, Options);

        Assert.Single(result.Kept);
        Assert.Equal("api", result.Kept[0].Fields!["service"]);
    }

    [Fact]
    public void FieldTerm_DropsEntriesWithoutFields()
    {
        var plain = new LogEntry(null, LogSeverity.Error, null, "m", "m");
        var spec = new LogFilterSpec { FieldTerms = new[] { new FieldTerm("service", "api") } };

        Assert.Empty(LogFilter.Apply(new[] { plain }, spec, Options).Kept);
    }

    [Fact]
    public void ParseFieldTerms_ReadsCommaSeparatedPairs()
    {
        Assert.True(LogFilter.TryParseFieldTerms("level=Error, service=api", out var terms, out _));
        Assert.Equal(2, terms.Count);
        Assert.Equal("service", terms[1].Key);
        Assert.Equal("api", terms[1].Value);
    }

    [Fact]
    public void ParseFieldTerms_RejectsTermWithoutValue()
    {
        Assert.False(LogFilter.TryParseFieldTerms("level=", out _, out string? error));
        Assert.NotNull(error);
    }
}
