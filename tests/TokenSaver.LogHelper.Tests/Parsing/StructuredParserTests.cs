using TokenSaver.LogHelper.Models;
using TokenSaver.LogHelper.Parsing;
using Xunit;

namespace TokenSaver.LogHelper.Tests.Parsing;

public sealed class StructuredParserTests
{
    private static readonly LogHelperOptions Options = new();

    private static StructuredParser Create(string name, string? pattern = null, string? schema = null)
    {
        Assert.True(StructuredParser.TryCreate(new LogFormatSpec(name, pattern, schema), Options, out var parser, out var error), error);
        return parser!;
    }

    [Fact]
    public void Json_KeepsEveryFieldFlattened()
    {
        var parser = Create("json");
        var ok = parser.TryParse("{\"@t\":\"2026-01-02T03:04:05Z\",\"@l\":\"Error\",\"@mt\":\"db down\",\"SourceContext\":\"Api\",\"req\":{\"id\":7,\"path\":\"/x\"},\"tags\":[\"a\",\"b\"]}", out var entry);

        Assert.True(ok);
        Assert.Equal(LogSeverity.Error, entry!.Severity);
        Assert.Equal("db down", entry.Message);
        Assert.Equal("Api", entry.Category);
        Assert.NotNull(entry.Timestamp);
        Assert.Equal("7", entry.Fields!["req.id"]);
        Assert.Equal("/x", entry.Fields["req.path"]);
        Assert.Equal("b", entry.Fields["tags.1"]);
    }

    [Fact]
    public void Json_PinoNumericLevel_MapsToSeverity()
    {
        var parser = Create("json");
        Assert.True(parser.TryParse("{\"level\":50,\"msg\":\"boom\",\"time\":1700000000000}", out var entry));
        Assert.Equal(LogSeverity.Error, entry!.Severity);
        Assert.Equal("boom", entry.Message);
    }

    [Fact]
    public void Xml_RootAttributesAndChildElements_BecomeFields()
    {
        var parser = Create("xml");
        bool ok = parser.TryParse("<event level=\"Warning\" service=\"billing\"><message>slow query</message><db><ms>412</ms></db></event>", out var entry);

        Assert.True(ok);
        Assert.Equal(LogSeverity.Warning, entry!.Severity);
        Assert.Equal("slow query", entry.Message);
        Assert.Equal("billing", entry.Fields!["service"]);
        Assert.Equal("412", entry.Fields["db.ms"]);
        Assert.Equal("event", entry.Fields["element"]);
    }

    [Fact]
    public void Xml_WithDoctype_IsRejected()
    {
        var parser = Create("xml");
        Assert.False(parser.TryParse("<!DOCTYPE x [<!ENTITY e \"boom\">]><event>&e;</event>", out _));
    }

    [Fact]
    public void Logfmt_QuotedAndBareValues()
    {
        var parser = Create("logfmt");
        Assert.True(parser.TryParse("level=error service=api msg=\"upstream \\\"timeout\\\"\" ms=900", out var entry));

        Assert.Equal("api", entry!.Fields!["service"]);
        Assert.Equal("upstream \"timeout\"", entry.Fields["msg"]);
        Assert.Equal("900", entry.Fields["ms"]);
        Assert.Equal(LogSeverity.Error, entry.Severity);
    }

    [Fact]
    public void Regex_NamedGroups_BecomeFieldsAndSlots()
    {
        var parser = Create("regex", @"^(?<ts>\S+) (?<level>[A-Z]+) \[(?<category>[^\]]+)\] (?<message>.*)$");
        Assert.True(parser.TryParse("2026-01-02T03:04:05 WARN [Worker] queue full", out var entry));

        Assert.Equal(LogSeverity.Warning, entry!.Severity);
        Assert.Equal("Worker", entry.Category);
        Assert.Equal("queue full", entry.Message);
        Assert.Equal("Worker", entry.Fields!["category"]);
    }

    [Fact]
    public void Schema_MapsSlotsToOtherFieldNames()
    {
        var parser = Create("json", schema: "{\"level\":\"severity\",\"message\":\"text\"}");
        Assert.True(parser.TryParse("{\"severity\":\"Critical\",\"text\":\"disk full\",\"msg\":\"ignored\"}", out var entry));

        Assert.Equal(LogSeverity.Critical, entry!.Severity);
        Assert.Equal("disk full", entry.Message);
    }

    [Fact]
    public void Schema_UnknownSlot_IsRejectedByValidate()
    {
        string? error = StructuredParser.Validate(new LogFormatSpec("json", null, "{\"colour\":\"x\"}"), Options);
        Assert.NotNull(error);
        Assert.Contains("colour", error);
    }

    [Fact]
    public void Validate_RegexWithoutPattern_IsRejected()
    {
        Assert.NotNull(StructuredParser.Validate(new LogFormatSpec("regex"), Options));
    }

    [Fact]
    public void Validate_UnknownFormat_IsRejected()
    {
        Assert.NotNull(StructuredParser.Validate(new LogFormatSpec("yaml"), Options));
    }

    [Fact]
    public void ParseLines_FoldsUnparsedLinesIntoPreviousEntry()
    {
        var parser = Create("json");
        var entries = parser.ParseLines(new[]
        {
            "{\"level\":\"Error\",\"msg\":\"boom\"}",
            "   at Program.Main()",
            "{\"level\":\"Information\",\"msg\":\"next\"}",
        });

        Assert.Equal(2, entries.Count);
        Assert.Contains("at Program.Main()", entries[0].Raw);
        Assert.Equal("next", entries[1].Message);
    }

    [Fact]
    public void Sniffer_PicksJson_FromSampleLines()
    {
        var lines = new[] { "{\"level\":\"Info\",\"msg\":\"a\"}", "{\"level\":\"Info\",\"msg\":\"b\"}" };
        var spec = FormatSniffer.Resolve(null, lines, Options);
        Assert.Equal("json", spec.Name);
    }

    [Fact]
    public void Sniffer_PlainText_StaysPlain()
    {
        var lines = new[] { "server started", "listening on 11436", "request done" };
        var spec = FormatSniffer.Resolve(null, lines, Options);
        Assert.Equal("plain", spec.Name);
    }

    [Fact]
    public void Sniffer_ExplicitFormat_IsNotOverridden()
    {
        var spec = FormatSniffer.Resolve(new LogFormatSpec("xml"), new[] { "{\"a\":1}" }, Options);
        Assert.Equal("xml", spec.Name);
    }
}
