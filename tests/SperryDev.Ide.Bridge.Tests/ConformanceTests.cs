using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SperryDev.Ide.Bridge;
using Xunit;

namespace SperryDev.Ide.Bridge.Tests;

/// <summary>
/// Runs the shared fixtures in conformance/fixtures.json against <see cref="BridgeServer"/>. The VS Code bridge
/// is held to the same fixtures on the TypeScript side.
/// </summary>
public sealed class ConformanceTests
{
    private sealed class SilentLogger : IBridgeLogger
    {
        public void LogDebug(string message) { }

        public void LogInfo(string message) { }

        public void LogError(string message) { }

        public void LogException(Exception exception) { }
    }

    public static IEnumerable<object[]> Fixtures()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures.json")));
        foreach (var fixture in doc.RootElement.EnumerateObject())
        {
            yield return new object[] { fixture.Name };
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task Reply_MatchesFixture(string name)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures.json")));
        var fixture = doc.RootElement.GetProperty(name);

        var pipe = $"TokenSaver.VS.Conformance.{Guid.NewGuid():N}";
        using var server = new BridgeServer(
            pipe,
            (action, _) => Task.FromException<object>(new KeyNotFoundException($"Unknown action {action}")),
            () => null,
            new SilentLogger());
        server.Start();

        var raw = fixture.TryGetProperty("raw", out var rawProp)
            ? rawProp.GetString()!
            : fixture.GetProperty("request").GetRawText();
        var reply = await SendLineAsync(pipe, raw);

        using var replyDoc = JsonDocument.Parse(reply);
        var root = replyDoc.RootElement;

        Assert.Equal(fixture.GetProperty("ok").GetBoolean(), root.GetProperty("ok").GetBoolean());
        foreach (var key in fixture.GetProperty("reply_keys").EnumerateArray())
        {
            Assert.True(root.TryGetProperty(key.GetString()!, out _), $"reply is missing '{key.GetString()}'");
        }
    }

    private static async Task<string> SendLineAsync(string pipeName, string line)
    {
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(3000);
        using var reader = new StreamReader(client, Encoding.UTF8, false, 1024, leaveOpen: true);
        using var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(line);
        return await reader.ReadLineAsync() ?? string.Empty;
    }
}
