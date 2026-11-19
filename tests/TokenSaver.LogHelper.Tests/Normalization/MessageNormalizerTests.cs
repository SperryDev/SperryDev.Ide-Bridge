using TokenSaver.LogHelper.Normalization;
using Xunit;

namespace TokenSaver.LogHelper.Tests.Normalization;

public sealed class MessageNormalizerTests
{
    [Theory]
    [InlineData("Started at 2026-11-15T10:20:30.123Z", "Started at <ts>")]
    [InlineData("Started at 2026-11-15 10:20:30+02:00", "Started at <ts>")]
    [InlineData("Jan  5 10:20:30 host app: hi", "<ts> host app: hi")]
    public void Normalize_MasksTimestamps(string input, string expected)
    {
        Assert.Equal(expected, MessageNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_MasksGuid()
    {
        Assert.Equal("id <guid> ok", MessageNormalizer.Normalize("id 3f2504e0-4f89-11d3-9a0c-0305e82c3301 ok"));
    }

    [Fact]
    public void Normalize_MasksHex()
    {
        Assert.Equal("ptr <hex>", MessageNormalizer.Normalize("ptr 0xDEADBEEF"));
    }

    [Theory]
    [InlineData("took 12ms", "took <dur>")]
    [InlineData("took 1.5 s", "took <dur>")]
    [InlineData("elapsed 00:00:01.5", "elapsed <dur>")]
    public void Normalize_MasksDurations(string input, string expected)
    {
        Assert.Equal(expected, MessageNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_MasksWindowsPath()
    {
        Assert.Equal("opened <path>", MessageNormalizer.Normalize(@"opened C:\logs\2026\app.log"));
    }

    [Fact]
    public void Normalize_MasksPosixPath()
    {
        Assert.Equal("read <path> now", MessageNormalizer.Normalize("read /var/log/syslog now"));
    }

    [Fact]
    public void Normalize_StripsAnsiEscapes()
    {
        Assert.Equal("ERR x", MessageNormalizer.Normalize("\u001b[31mERR\u001b[0m x"));
    }

    [Fact]
    public void Normalize_MasksNumbers()
    {
        Assert.Equal("retry <n> of <n>", MessageNormalizer.Normalize("retry 3 of 10"));
    }

    [Fact]
    public void Normalize_LinesDifferingOnlyInIdsAndTimes_SameTemplate()
    {
        var a = MessageNormalizer.Normalize("user 42 failed at 10:00:01 id 3f2504e0-4f89-11d3-9a0c-0305e82c3301");
        var b = MessageNormalizer.Normalize("user 7 failed at 11:59:59 id 0a1b2c3d-4f89-11d3-9a0c-0305e82c3302");

        Assert.Equal(a, b);
    }

    [Fact]
    public void Normalize_DifferentMessages_DifferentTemplate()
    {
        Assert.NotEqual(MessageNormalizer.Normalize("user failed"), MessageNormalizer.Normalize("user denied"));
    }

    [Fact]
    public void NormalizerVersion_IsOne()
    {
        Assert.Equal(1, MessageNormalizer.Version);
    }

    [Fact]
    public void TemplateHash_MatchesKnownSha256Prefix()
    {
        // SHA-256("abc") = ba7816bf8f01cfea414140de5dae2223...; first 16 hex chars kept.
        Assert.Equal("ba7816bf8f01cfea", MessageNormalizer.TemplateHash("abc"));
    }
}
