using TokenSaver.LogHelper.Security;
using Xunit;

namespace TokenSaver.LogHelper.Tests.Security;

public sealed class PathGuardTests : IDisposable
{
    private readonly string _base;
    private readonly string _root;

    public PathGuardTests()
    {
        _base = Path.Combine(Path.GetTempPath(), "tsl-pathguard-" + Guid.NewGuid().ToString("N"));
        _root = Path.Combine(_base, "logs");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_base, "logs2"));
        File.WriteAllText(Path.Combine(_root, "a.log"), "x");
        File.WriteAllText(Path.Combine(_base, "secret.log"), "x");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_base, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void DotDot_EscapingRoot_Rejected()
    {
        string path = Path.Combine(_root, "..", "secret.log");

        Assert.False(PathGuard.TryResolve(path, new[] { _root }, out _, out _));
    }

    [Fact]
    public void Unc_Rejected()
    {
        Assert.False(PathGuard.TryResolve(@"\\server\share\a.log", new[] { _root }, out _, out _));
    }

    [Fact]
    public void ExtendedPrefix_Rejected()
    {
        Assert.False(PathGuard.TryResolve(@"\\?\C:\x\a.log", new[] { _root }, out _, out _));
    }

    [Fact]
    public void AlternateDataStream_Rejected()
    {
        string plain = Path.Combine(_root, "a.log");

        Assert.False(PathGuard.TryResolve(plain + "::$DATA", new[] { _root }, out _, out _));
        Assert.False(PathGuard.TryResolve(plain + ":x", new[] { _root }, out _, out _));
    }

    [Fact]
    public void SiblingPrefix_Rejected()
    {
        string sibling = Path.Combine(_base, "logs2", "a.log");

        Assert.False(PathGuard.TryResolve(sibling, new[] { _root }, out _, out _));
    }

    [Fact]
    public void Junction_PointingOutsideRoot_Rejected()
    {
        string link = Path.Combine(_root, "link");
        try
        {
            Directory.CreateSymbolicLink(link, Path.Combine(_base, "logs2"));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return; // Link creation not permitted on this machine; nothing to assert.
        }

        string path = Path.Combine(link, "a.log");

        Assert.False(PathGuard.TryResolve(path, new[] { _root }, out _, out _));
    }

    [Fact]
    public void InsideRoot_Accepted_ReturnsCanonical()
    {
        string path = Path.Combine(_root, "sub", "..", "a.log");

        bool ok = PathGuard.TryResolve(path, new[] { _root }, out var canonical, out var reason);

        Assert.True(ok, reason);
        Assert.Equal(Path.GetFullPath(Path.Combine(_root, "a.log")), canonical);
    }

    [Fact]
    public void NoRootsConfigured_RejectsEverything()
    {
        Assert.False(PathGuard.TryResolve(Path.Combine(_root, "a.log"), Array.Empty<string>(), out _, out _));
    }
}
