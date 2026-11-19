namespace TokenSaver.TerminalControl.Tests;

using Xunit;
using TokenSaver.TerminalControl.Core;

public sealed class TabProtocolTests
{
    [Fact]
    public void FormatCommand_PrefixesRequestId()
    {
        Assert.Equal("req1 dir C:\\", TabProtocol.FormatCommand("req1", "dir C:\\"));
    }

    [Fact]
    public void FormatCommand_RejectsNewlines()
    {
        Assert.Throws<ArgumentException>(() => TabProtocol.FormatCommand("req1", "a\nb"));
        Assert.Throws<ArgumentException>(() => TabProtocol.FormatCommand("req1", "a\rb"));
    }

    [Fact]
    public void TryParseEnd_ParsesIdAndExitCode()
    {
        var parsed = TabProtocol.ParseEnd("__TOKENSAVER_END__ req1 3");

        Assert.NotNull(parsed);
        Assert.Equal("req1", parsed!.Value.RequestId);
        Assert.Equal(3, parsed!.Value.ExitCode);
    }

    [Fact]
    public void TryParseError_ParsesIdAndTextWithSpaces()
    {
        var parsed = TabProtocol.ParseError("__TOKENSAVER_ERR__ req1 boom at line 3");

        Assert.NotNull(parsed);
        Assert.Equal("req1", parsed!.Value.RequestId);
        Assert.Equal("boom at line 3", parsed!.Value.Text);
    }

    [Fact]
    public void TryParseError_IgnoresStdoutAndEndMarkers()
    {
        Assert.Null(TabProtocol.ParseError("plain output"));
        Assert.Null(TabProtocol.ParseEnd("__TOKENSAVER_ERR__ req1 boom"));
    }

    [Fact]
    public void TryParseEnd_IgnoresOrdinaryOutput()
    {
        Assert.Null(TabProtocol.ParseEnd("hello world"));
        Assert.Null(TabProtocol.ParseEnd("__TOKENSAVER_END__ only-id"));
    }
}
