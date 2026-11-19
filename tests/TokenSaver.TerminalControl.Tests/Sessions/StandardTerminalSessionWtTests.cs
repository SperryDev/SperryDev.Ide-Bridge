#pragma warning disable CA2000 // Test helpers hand disposables to the session under test; each test is short-lived.
namespace TokenSaver.TerminalControl.Tests;

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Xunit;
using TokenSaver.TerminalControl.Contracts;
using TokenSaver.TerminalControl.Core;
using TokenSaver.TerminalControl.Models;
using TokenSaver.TerminalControl.Output;

public sealed class StandardTerminalSessionWtTests : IDisposable
{
    private readonly ILoggerFactory _loggerFactory = LoggerFactory.Create(b => b.AddDebug());

    public void Dispose() => _loggerFactory.Dispose();

    private StandardTerminalSession CreateSession(RecordingTerminalLauncher launcher, NamedPipeManager pipes) =>
        new(_loggerFactory.CreateLogger<StandardTerminalSession>(), pipes, launcher);

    [Fact]
    public async Task InitializeAsync_LaunchesWtWithWindowArgument()
    {
        var launcher = new RecordingTerminalLauncher();
        using var pipes = new NamedPipeManager(_loggerFactory.CreateLogger<NamedPipeManager>());
        var session = CreateSession(launcher, pipes);

        await session.InitializeAsync(new TerminalSessionConfig());

        var launch = Assert.Single(launcher.Started);
        Assert.EndsWith("wt.exe", launch.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-w", launch.Arguments, StringComparison.Ordinal);
        Assert.Equal(TerminalSessionState.Ready, session.State);
    }

    [Fact]
    public async Task CreateTabAsync_OpensNewTabRunningWrapperWithPipeNames()
    {
        var launcher = new RecordingTerminalLauncher();
        using var pipes = new NamedPipeManager(_loggerFactory.CreateLogger<NamedPipeManager>());
        var session = CreateSession(launcher, pipes);
        await session.InitializeAsync(new TerminalSessionConfig());

        var tab = await session.CreateTabAsync("build");

        var tabLaunch = Assert.Single(launcher.Started.Skip(1));
        Assert.Contains("new-tab", tabLaunch.Arguments, StringComparison.Ordinal);
        Assert.Contains("tokensaver-tab.ps1", tabLaunch.Arguments, StringComparison.Ordinal);
        Assert.NotNull(tab.StdoutPipe);
        Assert.Contains(tab.StdoutPipe!.PipeName, tabLaunch.Arguments, StringComparison.Ordinal);
        Assert.Contains("-CommandPipe", tabLaunch.Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServiceAccountMode_OpensNoWindowAndRunsWrapperDirectly()
    {
        var launcher = new RecordingTerminalLauncher();
        using var pipes = new NamedPipeManager(_loggerFactory.CreateLogger<NamedPipeManager>());
        var session = CreateSession(launcher, pipes);
        await session.InitializeAsync(new TerminalSessionConfig(LaunchMode: TerminalLaunchMode.ServiceAccount));

        Assert.Empty(launcher.Started);

        await session.CreateTabAsync("headless");

        var tabLaunch = Assert.Single(launcher.Started);
        Assert.Equal("pwsh", tabLaunch.FileName);
        Assert.DoesNotContain("new-tab", tabLaunch.Arguments, StringComparison.Ordinal);
        Assert.Contains("tokensaver-tab.ps1", tabLaunch.Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitializeAsync_WhenWtMissing_TerminatesAndRethrows()
    {
        var launcher = new RecordingTerminalLauncher { ThrowOnStart = new FileNotFoundException("wt.exe") };
        using var pipes = new NamedPipeManager(_loggerFactory.CreateLogger<NamedPipeManager>());
        var session = CreateSession(launcher, pipes);

        await Assert.ThrowsAsync<FileNotFoundException>(() => session.InitializeAsync(new TerminalSessionConfig()));
        Assert.Equal(TerminalSessionState.Terminated, session.State);
    }
}
