#pragma warning disable CA2000 // Test helpers hand disposables to the session under test; each test is short-lived.
namespace TokenSaver.TerminalControl.Tests;

using Microsoft.Extensions.Logging;
using Xunit;
using TokenSaver.TerminalControl.Core;
using TokenSaver.TerminalControl.Models;
using TokenSaver.TerminalControl.Output;

public sealed class ElevatedTerminalSessionTests : IDisposable
{
    private readonly ILoggerFactory _loggerFactory = LoggerFactory.Create(b => b.AddDebug());

    public void Dispose() => _loggerFactory.Dispose();

    [Fact]
    public async Task InitializeAsync_DoesNotPromptUntilFirstTab()
    {
        var launcher = new RecordingTerminalLauncher();
        using var pipes = new NamedPipeManager(_loggerFactory.CreateLogger<NamedPipeManager>());
        var session = new ElevatedTerminalSession(
            _loggerFactory.CreateLogger<ElevatedTerminalSession>(),
            pipes,
            launcher);

        await session.InitializeAsync(new TerminalSessionConfig(IsElevated: true));

        Assert.Empty(launcher.Started);
        Assert.True(session.IsElevated);
    }

    [Fact]
    public async Task CreateTabAsync_EachTabIsLaunchedWithRunAs()
    {
        // One UAC prompt per tab, by design.
        var launcher = new RecordingTerminalLauncher();
        using var pipes = new NamedPipeManager(_loggerFactory.CreateLogger<NamedPipeManager>());
        var session = new ElevatedTerminalSession(
            _loggerFactory.CreateLogger<ElevatedTerminalSession>(),
            pipes,
            launcher);
        await session.InitializeAsync(new TerminalSessionConfig(IsElevated: true));

        await session.CreateTabAsync("elevated-tab");

        var tabLaunch = Assert.Single(launcher.Started);
        Assert.Equal("wt.exe", tabLaunch.FileName);
        Assert.Contains("new-tab", tabLaunch.Arguments, StringComparison.Ordinal);
        Assert.Equal("runas", tabLaunch.Verb);
        Assert.True(tabLaunch.UseShellExecute);
    }
}
