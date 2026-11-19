using TokenSaver.LogHelper.Models;
using Xunit;

namespace TokenSaver.LogHelper.Tests.Models;

public sealed class ChannelIdTests
{
    [Fact]
    public void Parse_FileScheme_KeepsWindowsPathWithDriveColon()
    {
        var id = ChannelId.Parse(@"file:C:\a\b.log");

        Assert.Null(id.Host);
        Assert.Equal("file", id.Scheme);
        Assert.Equal(@"C:\a\b.log", id.Name);
    }

    [Fact]
    public void Parse_HostQualified_SplitsHost()
    {
        var id = ChannelId.Parse("srv1/syslog:auth");

        Assert.Equal("srv1", id.Host);
        Assert.Equal("syslog", id.Scheme);
        Assert.Equal("auth", id.Name);
    }

    [Fact]
    public void Parse_UnknownScheme_Throws()
    {
        Assert.Throws<FormatException>(() => ChannelId.Parse("nope:whatever"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("file:")]
    [InlineData("file")]
    [InlineData(":name")]
    public void Parse_Malformed_Throws(string text)
    {
        Assert.Throws<FormatException>(() => ChannelId.Parse(text));
    }

    [Theory]
    [InlineData(@"file:C:\a\b.log")]
    [InlineData("srv1/syslog:auth")]
    [InlineData("vscode:Log (Extension Host)")]
    public void ToString_RoundTrips(string text)
    {
        Assert.Equal(text, ChannelId.Parse(text).ToString());
    }
}
