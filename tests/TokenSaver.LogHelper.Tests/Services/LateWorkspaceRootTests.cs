using System.Text;
using TokenSaver.LogHelper.Contracts;
using TokenSaver.LogHelper.Models;
using TokenSaver.LogHelper.Services;
using TokenSaver.LogHelper.Sources;
using Xunit;

namespace TokenSaver.LogHelper.Tests.Services;

/// <summary>
///     The workspace root is often learned after the service is built (the client's first call).
///     Options built from a root provider must pick it up without being rebuilt.
/// </summary>
public sealed class LateWorkspaceRootTests : IDisposable
{
    private static readonly LogRequestContext Context = new("test");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "tsl-late-" + Guid.NewGuid().ToString("N"));
    private string? _current;

    public LateWorkspaceRootTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Options_FollowTheProviderAsTheRootAppears()
    {
        var options = new LogHelperOptions { WorkspaceRootProvider = () => _current };

        Assert.Empty(options.AllowedRoots);
        Assert.Null(options.StateDirectory);

        _current = _root;

        Assert.Equal(new[] { _root }, options.AllowedRoots);
        Assert.Equal(Path.Combine(_root, ".tokensaver", "logs"), options.StateDirectory);
    }

    [Fact]
    public async Task Service_ReadsAndMarksRuns_OnceTheRootIsKnown()
    {
        string path = Path.Combine(_root, "app.log");
        File.WriteAllText(path, "[INF] up\n[ERR] boom\n", new UTF8Encoding(false));
        var options = new LogHelperOptions { WorkspaceRootProvider = () => _current };
        var service = new LogService(new ILogSourceProvider[] { new FileLogSourceProvider(options) }, options);
        ChannelId channel = ChannelId.Parse("file:" + path);

        var before = await service.MarkRunAsync(channel, Context, CancellationToken.None);
        Assert.False(before.Ok);

        _current = _root;

        var after = await service.MarkRunAsync(channel, Context, CancellationToken.None);
        Assert.True(after.Ok, after.Error);
        var query = await service.QueryAsync(new LogQueryRequest(channel), Context, CancellationToken.None);
        Assert.True(query.Ok, query.Error);
        Assert.Contains("boom", query.Text, StringComparison.Ordinal);
    }
}
