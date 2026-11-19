using TokenSaver.LogHelper.Models;
using TokenSaver.LogHelper.Parsing;
using Xunit;

namespace TokenSaver.LogHelper.Tests.Parsing;

public sealed class TraceFlaggerTests
{
    private static LogEntry Entry(LogSeverity severity, string raw, bool firstChance = false) =>
        new(null, severity, null, raw.Split('\n')[0], raw, firstChance);

    [Fact]
    public void DotNetUnhandled_High()
    {
        var flagged = TraceFlagger.Flag(new[]
        {
            Entry(LogSeverity.Critical, "Unhandled exception. System.Exception: boom\n   at Foo.Bar() in Foo.cs:line 1"),
        });

        var trace = Assert.Single(flagged);
        Assert.Equal("high", trace.Confidence);
        Assert.Contains("Unhandled", trace.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void NodeUnhandledRejection_High()
    {
        var flagged = TraceFlagger.Flag(new[]
        {
            Entry(LogSeverity.Error, "unhandledRejection: boom\n    at main (app.js:1:1)"),
        });

        Assert.Equal("high", Assert.Single(flagged).Confidence);
    }

    [Fact]
    public void JavaExceptionInThread_High()
    {
        var flagged = TraceFlagger.Flag(new[]
        {
            Entry(LogSeverity.Error, "Exception in thread \"main\" java.lang.IllegalStateException: x\n\tat A.b(A.java:1)"),
        });

        Assert.Equal("high", Assert.Single(flagged).Confidence);
    }

    [Fact]
    public void TraceLoggedAtInfo_Medium_ReasonSaysLevel()
    {
        var flagged = TraceFlagger.Flag(new[]
        {
            Entry(LogSeverity.Information, "request failed\n   at Foo.Bar()"),
        });

        var trace = Assert.Single(flagged);
        Assert.Equal("medium", trace.Confidence);
        Assert.Contains("Information", trace.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void FirstChance_NotFlagged()
    {
        var flagged = TraceFlagger.Flag(new[]
        {
            Entry(LogSeverity.Unknown, "Exception thrown: 'System.IO.IOException' in App.dll\n   at Foo.Bar()", firstChance: true),
        });

        Assert.Empty(flagged);
    }

    [Fact]
    public void SameTopFrame_GroupedWithCount()
    {
        var flagged = TraceFlagger.Flag(new[]
        {
            Entry(LogSeverity.Information, "first failure\n   at Foo.Bar() in Foo.cs:line 10"),
            Entry(LogSeverity.Information, "second failure\n   at Foo.Bar() in Foo.cs:line 99"),
        });

        var trace = Assert.Single(flagged);
        Assert.Equal(2, trace.Count);
    }

    [Fact]
    public void PlainEntry_NotFlagged()
    {
        Assert.Empty(TraceFlagger.Flag(new[] { Entry(LogSeverity.Error, "just a line") }));
    }
}
