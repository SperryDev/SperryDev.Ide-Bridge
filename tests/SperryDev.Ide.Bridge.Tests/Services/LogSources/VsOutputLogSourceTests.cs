using System.Text.Json;
using TokenSaver.LogHelper.Contracts;
using TokenSaver.LogHelper.Models;
using TokenSaver.MCP.Services.LogSources;
using Xunit;

namespace TokenSaver.MCP.Tests.Services.LogSources;

public sealed class VsOutputLogSourceTests
{
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

    private static string Ok(object result) => JsonSerializer.Serialize(new { ok = true, pid = 1, result });

    [Fact]
    public async Task Vsout_ListChannels_FromListOutputPanes()
    {
        var bridge = new FakeIdeLogBridge { Reply = _ => Ok(new { count = 2, panes = new[] { "Build", "Debug" } }) };
        var provider = new VsOutputLogSourceProvider(bridge);

        var channels = await provider.ListChannelsAsync(CancellationToken.None);

        Assert.Equal(new[] { "vsout:Build", "vsout:Debug" }, channels.Select(c => c.Id.ToString()));
    }

    [Fact]
    public async Task Vsout_Read_UsesFromOffsetAndAnchor()
    {
        var bridge = new FakeIdeLogBridge
        {
            Reply = _ => Ok(new { pane = "Build", text = "[INF] a\n[INF] b\n", nextOffset = 14, anchor = "abc", reset = false }),
        };
        var source = new VsOutputLogSourceProvider(bridge).Open(ChannelId.Parse("vsout:Build"));

        var first = await source.ReadAsync(null, 100, CancellationToken.None);
        Assert.Equal(new[] { "a", "b" }, first.Entries.Select(e => e.Message));
        Assert.Contains("\"fromOffset\":0", bridge.LastArgsJson, StringComparison.Ordinal);

        var second = await source.ReadAsync(first.NextCursor, 100, CancellationToken.None);
        Assert.Contains("\"fromOffset\":14", bridge.LastArgsJson, StringComparison.Ordinal);
        Assert.Contains("\"anchor\":\"abc\"", bridge.LastArgsJson, StringComparison.Ordinal);
        Assert.Equal(new[] { "a", "b" }, second.Entries.Select(e => e.Message));
    }

    [Fact]
    public async Task Reset_WhenBridgeReportsReset()
    {
        var bridge = new FakeIdeLogBridge
        {
            Reply = _ => Ok(new { pane = "Build", text = "[INF] fresh\n", nextOffset = 12, anchor = "z", reset = true }),
        };
        var source = new VsOutputLogSourceProvider(bridge).Open(ChannelId.Parse("vsout:Build"));

        var batch = await source.ReadAsync("stale-cursor", 100, CancellationToken.None);

        Assert.True(batch.Reset);
    }

    [Fact]
    public async Task OldExtension_WithoutFromOffset_FallsBackToMaxLines()
    {
        var bridge = new FakeIdeLogBridge
        {
            Reply = _ => Ok(new { pane = "Build", lines = new[] { "[INF] tail one", "[INF] tail two" } }),
        };
        var source = new VsOutputLogSourceProvider(bridge).Open(ChannelId.Parse("vsout:Build"));

        var first = await source.ReadAsync(null, 100, CancellationToken.None);
        Assert.Equal(new[] { "tail one", "tail two" }, first.Entries.Select(e => e.Message));
        Assert.Contains("\"maxLines\"", bridge.LastArgsJson, StringComparison.Ordinal);

        // The old bridge has no cursor, so later reads must not repeat the same tail.
        var second = await source.ReadAsync(first.NextCursor, 100, CancellationToken.None);
        Assert.Empty(second.Entries);
    }
}
