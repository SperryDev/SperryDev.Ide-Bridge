using TokenSaver.LogHelper.Models;
using Xunit;

namespace TokenSaver.LogHelper.Tests.Models;

public sealed class LogCursorTests
{
    [Fact]
    public void Encode_Decode_RoundTrips()
    {
        const string payload = "{\"offset\":42,\"file\":\"a.log\"}";

        var token = LogCursor.Encode(payload);

        Assert.True(LogCursor.TryDecode(token, out var decoded));
        Assert.Equal(payload, decoded);
    }

    [Fact]
    public void Encode_IsUrlSafe()
    {
        var token = LogCursor.Encode("???>>>~~~");

        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.DoesNotContain('=', token);
    }

    [Theory]
    [InlineData("!!!")]
    [InlineData("a")]
    [InlineData("")]
    [InlineData(null)]
    public void Decode_Garbage_ReturnsFalseNotThrow(string? token)
    {
        Assert.False(LogCursor.TryDecode(token, out _));
    }
}
