#pragma warning disable CA2000 // Test helpers hand disposables to the session under test; each test is short-lived.
namespace TokenSaver.TerminalControl.Tests;

using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Xunit;
using TokenSaver.TerminalControl.Contracts;
using TokenSaver.TerminalControl.Core;
using TokenSaver.TerminalControl.Models;
using TokenSaver.TerminalControl.Output;

/// <summary>
/// Manual smoke tests against real Windows Terminal. Opt-in:
/// TOKENSAVER_WT_SMOKE=1 for a standard window; TOKENSAVER_WT_ELEVATED_SMOKE=1 for an elevated window (UAC prompt).
/// </summary>
public sealed class WindowsTerminalSmokeTests : IDisposable
{
    private const string IsAdminCommand =
        "([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent())" +
        ".IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)";

    private readonly ILoggerFactory _loggerFactory = LoggerFactory.Create(b => b.AddDebug());

    public void Dispose() => _loggerFactory.Dispose();

    [Fact]
    public async Task RealWindowsTerminal_RunsCommandInTab()
    {
        if (Environment.GetEnvironmentVariable("TOKENSAVER_WT_SMOKE") != "1")
        {
            return;
        }

        using var pipes = new NamedPipeManager(_loggerFactory.CreateLogger<NamedPipeManager>());
        var session = new StandardTerminalSession(
            _loggerFactory.CreateLogger<StandardTerminalSession>(),
            pipes,
            new WrapperPathRewritingLauncher(FindWrapperScript()));

        await session.InitializeAsync(new TerminalSessionConfig());
        var tab = await session.CreateTabAsync("smoke");

        var result = await session.ExecuteInTabAsync(
            tab.TabId,
            "Write-Output 'smoke-ok'; $PSVersionTable.PSVersion.ToString()",
            timeout: TimeSpan.FromSeconds(60));

        Assert.True(result.Success, $"Command failed: {string.Join("; ", result.Errors ?? Array.Empty<string>())}");
        Assert.Contains("smoke-ok", result.Stdout ?? string.Empty, StringComparison.Ordinal);

        await session.CloseTabAsync(tab.TabId);
        await session.TerminateAsync(graceful: true);
    }

    [Fact]
    public async Task RealWindowsTerminal_ElevatedSessionTabRunsElevated()
    {
        if (Environment.GetEnvironmentVariable("TOKENSAVER_WT_ELEVATED_SMOKE") != "1")
        {
            return;
        }

        using var pipes = new NamedPipeManager(_loggerFactory.CreateLogger<NamedPipeManager>());
        var session = new ElevatedTerminalSession(
            _loggerFactory.CreateLogger<ElevatedTerminalSession>(),
            pipes,
            new WrapperPathRewritingLauncher(FindWrapperScript()));

        // No prompt yet; the tab prompts UAC when it is created.
        await session.InitializeAsync(new TerminalSessionConfig(IsElevated: true));
        var tab = await session.CreateTabAsync("elevated-smoke");

        var result = await session.ExecuteInTabAsync(tab.TabId, IsAdminCommand, timeout: TimeSpan.FromSeconds(90));

        Assert.True(result.Success, $"Command failed: {string.Join("; ", result.Errors ?? Array.Empty<string>())}");
        Assert.Equal("True", result.Stdout?.Trim());

        await session.CloseTabAsync(tab.TabId);
        await session.TerminateAsync(graceful: true);
    }

    private static string FindWrapperScript()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Core", "TokenSaver.TerminalControl", "Scripts", "tokensaver-tab.ps1");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("tokensaver-tab.ps1 not found above the test output folder.");
    }

    /// <summary>Starts the real wt.exe (keeping Verb/UseShellExecute), pointing the wrapper path at the source script.</summary>
    private sealed class WrapperPathRewritingLauncher : ITerminalProcessLauncher
    {
        private static readonly Regex WrapperArg = new("-File \"([^\"]+)\"", RegexOptions.Compiled);
        private readonly string _wrapperPath;

        public WrapperPathRewritingLauncher(string wrapperPath) => _wrapperPath = wrapperPath;

        public Process? Start(ProcessStartInfo startInfo)
        {
            var args = WrapperArg.Replace(startInfo.Arguments, $"-File \"{_wrapperPath}\"", 1);
            return Process.Start(new ProcessStartInfo
            {
                FileName = startInfo.FileName,
                Arguments = args,
                Verb = startInfo.Verb,
                UseShellExecute = startInfo.UseShellExecute,
            });
        }
    }
}
