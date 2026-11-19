using TokenSaver.LogHelper.Models;
using TokenSaver.LogHelper.Parsing;
using Xunit;

namespace TokenSaver.LogHelper.Tests.Parsing;

public sealed class JsonLinesFormatTests
{
    [Fact]
    public void SerilogCompact_NoLevel_DefaultsInformation()
    {
        bool ok = JsonLinesFormat.TryParse("{\"@t\":\"2026-11-15T10:00:00Z\",\"@mt\":\"Hello {Name}\",\"Name\":\"x\"}", out var entry);

        Assert.True(ok);
        Assert.Equal(LogSeverity.Information, entry!.Severity);
    }

    [Fact]
    public void SerilogCompact_UsesMessageTemplateAsTemplate()
    {
        JsonLinesFormat.TryParse("{\"@t\":\"2026-11-15T10:00:00Z\",\"@mt\":\"Hello {Name}\",\"Name\":\"x\"}", out var entry);

        Assert.Equal("Hello {Name}", entry!.Message);
        Assert.NotNull(entry.Timestamp);
    }

    [Fact]
    public void SerilogCompact_LevelWord_MapsSeverity()
    {
        JsonLinesFormat.TryParse("{\"@l\":\"Fatal\",\"@mt\":\"down\"}", out var entry);

        Assert.Equal(LogSeverity.Critical, entry!.Severity);
    }

    [Fact]
    public void Pino_50_IsError_30_IsInfo()
    {
        JsonLinesFormat.TryParse("{\"level\":50,\"time\":1700000000000,\"msg\":\"boom\"}", out var error);
        JsonLinesFormat.TryParse("{\"level\":30,\"time\":1700000000000,\"msg\":\"ok\"}", out var info);

        Assert.Equal(LogSeverity.Error, error!.Severity);
        Assert.Equal("boom", error.Message);
        Assert.Equal(LogSeverity.Information, info!.Severity);
    }

    [Fact]
    public void GenericStringLevel_IsMapped()
    {
        JsonLinesFormat.TryParse("{\"level\":\"warning\",\"message\":\"slow\"}", out var entry);

        Assert.Equal(LogSeverity.Warning, entry!.Severity);
        Assert.Equal("slow", entry.Message);
    }

    [Fact]
    public void MalformedJson_FallsBackToPlainLine()
    {
        Assert.False(JsonLinesFormat.TryParse("{\"broken", out _));

        var entries = new LogParser(new LogHelperOptions()).Parse(new[] { "{\"broken" });

        Assert.Single(entries);
        Assert.Equal("{\"broken", entries[0].Message);
        Assert.Equal(LogSeverity.Unknown, entries[0].Severity);
    }

    [Fact]
    public void ExceptionField_BecomesTrace()
    {
        JsonLinesFormat.TryParse("{\"@l\":\"Error\",\"@mt\":\"x\",\"@x\":\"System.Exception: boom\\n   at Foo.Bar()\"}", out var entry);

        Assert.Contains("System.Exception: boom", entry!.Raw, StringComparison.Ordinal);
        Assert.Contains("at Foo.Bar()", entry.Raw, StringComparison.Ordinal);
    }
}
