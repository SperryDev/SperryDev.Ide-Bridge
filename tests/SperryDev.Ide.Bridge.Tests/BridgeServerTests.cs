using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using SperryDev.Ide.Bridge;
using Xunit;

namespace SperryDev.Ide.Bridge.Tests;

public sealed class BridgeServerTests
{
    private sealed class SilentLogger : IBridgeLogger
    {
        public void LogDebug(string message) { }

        public void LogInfo(string message) { }

        public void LogError(string message) { }

        public void LogException(Exception exception) { }
    }

    private static string NewPipeName() => $"TokenSaver.VS.Test.{Guid.NewGuid():N}";

    private static BridgeServer NewServer(string pipeName) =>
        new(pipeName, (_, _) => Task.FromResult<object>(new { }), () => null, new SilentLogger());

    private static async Task<string?> SendAsync(string pipeName, string request, int timeoutMs = 3000)
    {
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(timeoutMs);
        using var reader = new StreamReader(client, Encoding.UTF8, false, 1024, leaveOpen: true);
        using var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(request);
        return await reader.ReadLineAsync();
    }

    [Fact]
    public async Task StopClosesThePipe_AndStartReopensIt()
    {
        var pipe = NewPipeName();
        using var server = NewServer(pipe);

        server.Start();
        var first = await SendAsync(pipe, "{\"action\":\"health\"}");
        Assert.NotNull(first);
        Assert.Contains("\"listener_running\":true", first);

        server.Stop();
        await Task.Delay(300);
        Assert.False(server.IsRunning);
        await Assert.ThrowsAnyAsync<TimeoutException>(() => SendAsync(pipe, "{\"action\":\"health\"}", timeoutMs: 500));

        server.Start();
        Assert.True(server.IsRunning);
        var second = await SendAsync(pipe, "{\"action\":\"health\"}");
        Assert.NotNull(second);
        Assert.Contains("\"listener_running\":true", second);
    }

    [Fact]
    public void StartTwice_DoesNotStartASecondListener()
    {
        var pipe = NewPipeName();
        using var server = NewServer(pipe);

        server.Start();
        server.Start();

        Assert.True(server.IsRunning);
        server.Stop();
    }
}
