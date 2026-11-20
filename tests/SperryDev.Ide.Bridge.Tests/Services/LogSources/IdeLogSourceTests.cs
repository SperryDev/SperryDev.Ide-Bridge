using SperryDev.Ide.Bridge;
using System.Text;
using System.Text.Json;
using TokenSaver.LogHelper.Contracts;
using TokenSaver.LogHelper.Models;
using TokenSaver.LogHelper.Services;
using TokenSaver.MCP.Services;
using TokenSaver.MCP.Services.LogSources;
using VsInstance = SperryDev.Ide.Bridge.VsInstance;
using Xunit;

namespace TokenSaver.MCP.Tests.Services.LogSources;

public sealed class IdeLogSourceTests : IDisposable
{
    private static readonly LogRequestContext Context = new("test");

    private readonly string _root;

    public IdeLogSourceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "tsl-ide-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "output_logging_20261115T1"));
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

    private static VsInstance VsCode(params string[] roots) =>
        new(1, "pipe", null, DateTime.UtcNow, "VsCode", roots);

    [Fact]
    public async Task Vscode_ListChannels_FromManifestRoots()
    {
        string channelFile = Path.Combine(_root, "output_logging_20261115T1", "1-Prettier.log");
        File.WriteAllText(channelFile, "[INFO - 2:28:53 PM] ready\n", new UTF8Encoding(false));
        var provider = new VsCodeLogSourceProvider(() => new[] { VsCode(_root) });

        var channels = await provider.ListChannelsAsync(CancellationToken.None);

        var only = Assert.Single(channels);
        Assert.Contains("1-Prettier.log", only.Id.ToString(), StringComparison.Ordinal);
        Assert.StartsWith("vscode:", only.Id.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Vscode_PathOutsideReportedRoots_Rejected()
    {
        string outside = Path.Combine(Path.GetTempPath(), "tsl-outside-" + Guid.NewGuid().ToString("N") + ".log");
        File.WriteAllText(outside, "x\n", new UTF8Encoding(false));
        try
        {
            var provider = new VsCodeLogSourceProvider(() => new[] { VsCode(_root) });
            var source = provider.Open(ChannelId.Parse("vscode:" + outside));

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadAsync(null, 10, CancellationToken.None));
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public async Task Debug_Read_PagesBySeq_UntilEmpty()
    {
        var bridge = new FakeIdeLogBridge
        {
            Reply = _ => Reply(new { lines = new[] { "[INF] a", "[INF] b" }, nextSeq = 3, dropped = 0 }),
        };
        var source = new DebugSessionLogSourceProvider(bridge).Open(ChannelId.Parse("debug:s1"));

        var first = await source.ReadAsync(null, 100, CancellationToken.None);
        Assert.Equal(new[] { "a", "b" }, first.Entries.Select(e => e.Message));

        bridge.Reply = _ => Reply(new { lines = Array.Empty<string>(), nextSeq = 3, dropped = 0 });
        var second = await source.ReadAsync(first.NextCursor, 100, CancellationToken.None);

        Assert.Empty(second.Entries);
        Assert.Contains("\"fromSeq\":3", bridge.LastArgsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Debug_GarbageCursor_StartsFromBeginning()
    {
        var bridge = new FakeIdeLogBridge
        {
            Reply = _ => Reply(new { lines = new[] { "[INF] a" }, nextSeq = 2, dropped = 0 }),
        };
        var source = new DebugSessionLogSourceProvider(bridge).Open(ChannelId.Parse("debug:s1"));

        var batch = await source.ReadAsync("stale-cursor", 100, CancellationToken.None);

        Assert.Equal(new[] { "a" }, batch.Entries.Select(e => e.Message));
        Assert.Contains("\"fromSeq\":1", bridge.LastArgsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Debug_Dropped_SurfacedInResult()
    {
        var bridge = new FakeIdeLogBridge
        {
            Reply = _ => Reply(new { lines = new[] { "[INF] late" }, nextSeq = 9, dropped = 4 }),
        };
        var source = new DebugSessionLogSourceProvider(bridge).Open(ChannelId.Parse("debug:s1"));

        var batch = await source.ReadAsync(null, 100, CancellationToken.None);

        Assert.Equal(4, batch.Dropped);
    }

    [Fact]
    public async Task NoInstance_ReturnsErrorNotThrow()
    {
        var bridge = new FakeIdeLogBridge
        {
            Reply = _ => "{\"ok\":false,\"pid\":0,\"error\":\"no VS Code instance is running\"}",
        };
        var service = new LogService(new ILogSourceProvider[] { new DebugSessionLogSourceProvider(bridge) }, new LogHelperOptions());

        var result = await service.QueryAsync(new LogQueryRequest(ChannelId.Parse("debug:s1")), Context, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Contains("no VS Code instance", result.Error, StringComparison.Ordinal);
    }

    private static string Reply(object result) =>
        JsonSerializer.Serialize(new { ok = true, pid = 1, result });

    private sealed class FakeIdeLogBridge : IIdeLogBridge
    {
        public Func<string, string> Reply { get; set; } = _ => "{\"ok\":true,\"pid\":1,\"result\":{}}";

        public string LastArgsJson { get; private set; } = string.Empty;

        public Task<string> CallAsync(string action, object args, string? solutionPath, CancellationToken ct)
        {
            LastArgsJson = JsonSerializer.Serialize(args);
            return Task.FromResult(Reply(action));
        }
    }
}
