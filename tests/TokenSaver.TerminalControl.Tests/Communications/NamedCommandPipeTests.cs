namespace TokenSaver.TerminalControl.Tests;

using System.IO.Pipes;
using Microsoft.Extensions.Logging;
using Xunit;
using TokenSaver.TerminalControl.Output;

public sealed class NamedCommandPipeTests : IDisposable
{
    private readonly ILoggerFactory _loggerFactory = LoggerFactory.Create(b => b.AddDebug());
    private readonly string _session = $"cmdtest{Guid.NewGuid():N}"[..16];

    public void Dispose() => _loggerFactory.Dispose();

    private NamedPipeManager CreateManager() =>
        new(_loggerFactory.CreateLogger<NamedPipeManager>());

    [Fact]
    public async Task CommandPipe_DeliversLinesToConnectedClient()
    {
        using var pipes = CreateManager();
        var name = pipes.CreateCommandPipe(_session, "t1");
        var command = pipes.GetCommandPipe(name)!;

        using var client = new NamedPipeClientStream(".", name, PipeDirection.In);
        var connect = client.ConnectAsync(5000);
        await command.WaitForClientAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        await connect;

        await command.WriteLineAsync("req1 Write-Output 'hi'");

        using var reader = new StreamReader(client);
        Assert.Equal("req1 Write-Output 'hi'", await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task CommandPipe_RejectsSecondClient()
    {
        using var pipes = CreateManager();
        var name = pipes.CreateCommandPipe(_session, "t2");
        var command = pipes.GetCommandPipe(name)!;

        using var first = new NamedPipeClientStream(".", name, PipeDirection.In);
        var connect = first.ConnectAsync(5000);
        await command.WaitForClientAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        await connect;

        using var second = new NamedPipeClientStream(".", name, PipeDirection.In);
        await Assert.ThrowsAnyAsync<Exception>(() => second.ConnectAsync(500));
    }

    [Fact]
    public async Task CommandPipe_WaitForClientTimesOutWithoutClient()
    {
        using var pipes = CreateManager();
        var name = pipes.CreateCommandPipe(_session, "t3");
        var command = pipes.GetCommandPipe(name)!;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => command.WaitForClientAsync(TimeSpan.FromMilliseconds(200), CancellationToken.None));
    }

    [Fact]
    public async Task CommandPipe_DoesNotAcceptReconnectAfterClientDisconnects()
    {
        using var pipes = CreateManager();
        var name = pipes.CreateCommandPipe(_session, "t6");
        var command = pipes.GetCommandPipe(name)!;

        using (var first = new NamedPipeClientStream(".", name, PipeDirection.In))
        {
            var connect = first.ConnectAsync(5000);
            await command.WaitForClientAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            await connect;
        }

        await Assert.ThrowsAnyAsync<Exception>(
            () => command.WaitForClientAsync(TimeSpan.FromMilliseconds(500), CancellationToken.None));
    }

    [Fact]
    public void ClosePipe_RemovesCommandPipe()
    {
        using var pipes = CreateManager();
        var name = pipes.CreateCommandPipe(_session, "t4");

        pipes.ClosePipe(name);

        Assert.Null(pipes.GetCommandPipe(name));
    }

    [Fact]
    public void CloseSessionPipes_RemovesCommandPipes()
    {
        using var pipes = CreateManager();
        var name = pipes.CreateCommandPipe(_session, "t5");

        pipes.CloseSessionPipes(_session);

        Assert.Null(pipes.GetCommandPipe(name));
    }
}
