using TokenSaver.LogHelper.Filtering;
using TokenSaver.LogHelper.Models;
using Xunit;

namespace TokenSaver.LogHelper.Tests.Filtering;

public sealed class LogFilterTests
{
    private static readonly LogHelperOptions Options = new();

    private static LogEntry Entry(LogSeverity severity, string text, string? category = null) =>
        new(null, severity, category, text, text);

    [Fact]
    public void Match_And_Exclude_Compose()
    {
        var entries = new[]
        {
            Entry(LogSeverity.Error, "db timeout"),
            Entry(LogSeverity.Error, "db timeout ignored"),
            Entry(LogSeverity.Information, "db ok"),
        };

        var result = LogFilter.Apply(entries, new LogFilterSpec { Match = "db", Exclude = "ignored" }, Options);

        Assert.Null(result.Error);
        Assert.Equal(new[] { "db timeout", "db ok" }, result.Kept.Select(e => e.Message));
        Assert.Equal(new[] { "db timeout ignored" }, result.Dropped.Select(e => e.Message));
    }

    [Fact]
    public void MinLevel_Warning_DropsInfo()
    {
        var entries = new[]
        {
            Entry(LogSeverity.Information, "a"),
            Entry(LogSeverity.Warning, "b"),
            Entry(LogSeverity.Error, "c"),
        };

        var result = LogFilter.Apply(entries, new LogFilterSpec { MinLevel = LogSeverity.Warning }, Options);

        Assert.Equal(new[] { "b", "c" }, result.Kept.Select(e => e.Message));
    }

    [Fact]
    public void Category_PrefixMatch()
    {
        var entries = new[]
        {
            Entry(LogSeverity.Information, "1", "App.Services.Foo"),
            Entry(LogSeverity.Information, "2", "App.Other"),
            Entry(LogSeverity.Information, "3", "Lib.X"),
        };

        var result = LogFilter.Apply(entries, new LogFilterSpec { Category = "App.Services" }, Options);

        Assert.Single(result.Kept);
        Assert.Equal("1", result.Kept[0].Message);
    }

    [Fact]
    public void Dropped_AreReturnedForIgnoredView()
    {
        var entries = new[]
        {
            Entry(LogSeverity.Error, "keep me"),
            Entry(LogSeverity.Information, "drop me"),
        };

        var result = LogFilter.Apply(entries, new LogFilterSpec { MinLevel = LogSeverity.Error }, Options);

        Assert.Single(result.Kept);
        Assert.Single(result.Dropped);
        Assert.Equal("drop me", result.Dropped[0].Message);
    }

    [Fact]
    public void InvalidRegex_ReturnsErrorAndNoEntries()
    {
        var entries = new[] { Entry(LogSeverity.Error, "x") };

        var result = LogFilter.Apply(entries, new LogFilterSpec { Match = "(?=x)" }, Options);

        Assert.NotNull(result.Error);
        Assert.Empty(result.Kept);
    }
}
