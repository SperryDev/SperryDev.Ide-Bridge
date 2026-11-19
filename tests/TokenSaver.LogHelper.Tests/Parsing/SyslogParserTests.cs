using TokenSaver.LogHelper.Models;
using TokenSaver.LogHelper.Parsing;
using Xunit;

namespace TokenSaver.LogHelper.Tests.Parsing;

public sealed class SyslogParserTests
{
    [Fact]
    public void Rfc3164_Yearless_UsesClockYear()
    {
        var now = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

        var entry = SyslogParser.Parse("<34>Nov 10 10:20:30 host app[1]: hi", now);

        Assert.Equal(2026, entry.Timestamp!.Value.Year);
        Assert.Equal("app", entry.Category);
        Assert.Equal("hi", entry.Message);
    }

    [Fact]
    public void Rfc3164_DecemberLineReadInJanuary_UsesPreviousYear()
    {
        var now = new DateTimeOffset(2027, 1, 2, 9, 0, 0, TimeSpan.Zero);

        var entry = SyslogParser.Parse("<34>Dec 30 10:00:00 host app: x", now);

        Assert.Equal(2026, entry.Timestamp!.Value.Year);
        Assert.Equal(12, entry.Timestamp.Value.Month);
    }

    [Fact]
    public void Rfc5424_ParsesPriorityToSeverity()
    {
        var now = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

        var entry = SyslogParser.Parse("<11>1 2026-11-15T10:20:30Z host app 123 ID1 - something bad", now);

        Assert.Equal(LogSeverity.Error, entry.Severity);
        Assert.Equal("app", entry.Category);
        Assert.Equal("something bad", entry.Message);
    }

    [Fact]
    public void Journald_Json_MapsPriorityAndIdentifier()
    {
        var now = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
        const string line = "{\"MESSAGE\":\"disk full\",\"PRIORITY\":\"4\",\"SYSLOG_IDENTIFIER\":\"kernel\",\"__REALTIME_TIMESTAMP\":\"1700000000000000\"}";

        var entry = SyslogParser.Parse(line, now);

        Assert.Equal(LogSeverity.Warning, entry.Severity);
        Assert.Equal("kernel", entry.Category);
        Assert.Equal("disk full", entry.Message);
        Assert.NotNull(entry.Timestamp);
    }

    [Fact]
    public void Garbage_FallsBackToPlain()
    {
        var now = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

        var entry = SyslogParser.Parse("random text line", now);

        Assert.Equal(LogSeverity.Unknown, entry.Severity);
        Assert.Equal("random text line", entry.Message);
        Assert.Null(entry.Timestamp);
    }
}
