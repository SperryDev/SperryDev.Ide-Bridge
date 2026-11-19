using System.Diagnostics;
using TokenSaver.LogHelper.Filtering;
using TokenSaver.LogHelper.Models;
using Xunit;

namespace TokenSaver.LogHelper.Tests.Filtering;

public sealed class SafeRegexTests
{
    private static readonly LogHelperOptions Options = new();

    [Fact]
    public void Lookahead_Rejected_WithMessageNamingConstruct()
    {
        bool ok = SafeRegex.TryCreate("foo(?=bar)", Options, out var regex, out var error);

        Assert.False(ok);
        Assert.Null(regex);
        Assert.Contains("lookaround", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Backreference_Rejected()
    {
        bool ok = SafeRegex.TryCreate(@"(a)\1", Options, out _, out var error);

        Assert.False(ok);
        Assert.Contains("backreference", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OverLengthPattern_Rejected()
    {
        bool ok = SafeRegex.TryCreate(new string('a', 600), Options, out _, out var error);

        Assert.False(ok);
        Assert.Contains("too long", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidPattern_ReturnsError()
    {
        bool ok = SafeRegex.TryCreate("[", Options, out _, out var error);

        Assert.False(ok);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void CatastrophicPattern_CompletesUnder1s()
    {
        Assert.True(SafeRegex.TryCreate("(a+)+$", Options, out var regex, out _));

        string input = new string('a', 50_000) + "!";
        var watch = Stopwatch.StartNew();
        _ = regex!.IsMatch(input);
        watch.Stop();

        Assert.True(watch.ElapsedMilliseconds < 1000, $"took {watch.ElapsedMilliseconds} ms");
    }
}
