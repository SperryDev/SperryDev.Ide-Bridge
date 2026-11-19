using System.Text;
using TokenSaver.LogHelper.Models;
using TokenSaver.LogHelper.Parsing;
using Xunit;

namespace TokenSaver.LogHelper.Tests.Parsing;

public sealed class LogParserTests
{
    private static IReadOnlyList<LogEntry> Parse(params string[] lines) =>
        new LogParser(new LogHelperOptions()).Parse(lines);

    [Fact]
    public void Bracketed_ERR_WARN_INF_MapToSeverity()
    {
        var entries = Parse("[ERR] boom", "[WRN] careful", "[INF] started");

        Assert.Equal(LogSeverity.Error, entries[0].Severity);
        Assert.Equal("boom", entries[0].Message);
        Assert.Equal(LogSeverity.Warning, entries[1].Severity);
        Assert.Equal(LogSeverity.Information, entries[2].Severity);
    }

    [Fact]
    public void LeadingTimestamp_IsParsedAndRemovedFromMessage()
    {
        var entries = Parse("2026-11-15 10:20:30 [DBG] tick");

        Assert.NotNull(entries[0].Timestamp);
        Assert.Equal(LogSeverity.Debug, entries[0].Severity);
        Assert.Equal("tick", entries[0].Message);
    }

    [Fact]
    public void VsCodeChannelFormat_LevelInQuotes_MapsSeverity()
    {
        var entries = Parse("[\"ERROR\" - 2:28:53 PM] Extension failed to activate", "[\"INFO\" - 2:28:54 PM] ready");

        Assert.Equal(LogSeverity.Error, entries[0].Severity);
        Assert.Equal("Extension failed to activate", entries[0].Message);
        Assert.Equal(LogSeverity.Information, entries[1].Severity);
        Assert.Equal("ready", entries[1].Message);
    }

    [Fact]
    public void Plain_WordErrorInsideMessage_IsNotError()
    {
        var entries = Parse("0 errors found", "ErrorHandler registered");

        Assert.All(entries, e => Assert.NotEqual(LogSeverity.Error, e.Severity));
    }

    [Fact]
    public void MelConsole_InfoHeaderPlusIndentedMessage_FoldsToOneEntryWithCategory()
    {
        var entries = Parse(
            "info: App.Services.Foo[0]",
            "      Started listener on port 11436");

        Assert.Single(entries);
        Assert.Equal(LogSeverity.Information, entries[0].Severity);
        Assert.Equal("App.Services.Foo", entries[0].Category);
        Assert.Equal("Started listener on port 11436", entries[0].Message);
    }

    [Fact]
    public void StackTraceLines_FoldIntoPrecedingEntry()
    {
        var entries = Parse(
            "[ERR] request failed",
            "   at Foo.Bar() in Foo.cs:line 10",
            "   at Foo.Baz()",
            "[INF] next");

        Assert.Equal(2, entries.Count);
        Assert.Equal("request failed", entries[0].Message);
        Assert.Contains("at Foo.Bar()", entries[0].Raw, StringComparison.Ordinal);
        Assert.Equal("next", entries[1].Message);
    }

    [Fact]
    public void PythonTraceback_FoldsUntilExceptionLine()
    {
        var entries = Parse(
            "Traceback (most recent call last):",
            "  File \"app.py\", line 3, in <module>",
            "    run()",
            "ValueError: bad input",
            "[INF] after");

        Assert.Equal(2, entries.Count);
        Assert.Equal(LogSeverity.Error, entries[0].Severity);
        Assert.Contains("ValueError: bad input", entries[0].Raw, StringComparison.Ordinal);
        Assert.Equal("after", entries[1].Message);
    }

    [Fact]
    public void JavaCausedBy_FoldsIntoSameEntry()
    {
        var entries = Parse(
            "[ERR] load failed",
            "\tat A.b(A.java:1)",
            "Caused by: java.io.IOException: nope",
            "\tat C.d(C.java:2)");

        Assert.Single(entries);
        Assert.Contains("Caused by: java.io.IOException: nope", entries[0].Raw, StringComparison.Ordinal);
    }

    [Fact]
    public void OversizeEntry_TruncatedAtCap_FlagSet()
    {
        var options = new LogHelperOptions { MaxEntryBytes = 100 };
        var longLine = "[ERR] " + new string('x', 500);

        var entries = new LogParser(options).Parse(new[] { longLine, "   at Foo.Bar()" });

        Assert.Single(entries);
        Assert.True(entries[0].Truncated);
        Assert.True(Encoding.UTF8.GetByteCount(entries[0].Raw) <= 100);
    }

    [Fact]
    public void VsFirstChance_ExceptionThrownIn_MarkedFirstChance()
    {
        var entries = Parse("Exception thrown: 'System.InvalidOperationException' in App.dll");

        Assert.Single(entries);
        Assert.True(entries[0].FirstChanceException);
    }
}
