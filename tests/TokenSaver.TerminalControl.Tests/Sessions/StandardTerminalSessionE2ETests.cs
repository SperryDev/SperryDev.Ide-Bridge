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
/// Runs the real wrapper script with pwsh directly (no Windows Terminal window).
/// Covers the pipe protocol end to end.
/// </summary>
public sealed class StandardTerminalSessionE2ETests : IDisposable
{
    private readonly ILoggerFactory _loggerFactory = LoggerFactory.Create(b => b.AddDebug());
    private readonly List<Process> _tabProcesses = new();

    public void Dispose()
    {
        foreach (var process in _tabProcesses)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // Already exited.
            }

            process.Dispose();
        }

        _loggerFactory.Dispose();
    }

    [Fact]
    public async Task ExecuteInTab_RunsCommandsThroughWrapper()
    {
        var wrapper = FindWrapperScript();
        using var pipes = new NamedPipeManager(_loggerFactory.CreateLogger<NamedPipeManager>());
        var session = new StandardTerminalSession(
            _loggerFactory.CreateLogger<StandardTerminalSession>(),
            pipes,
            new DirectTabLauncher(wrapper, _tabProcesses));
        await session.InitializeAsync(new TerminalSessionConfig());

        var tab = await session.CreateTabAsync("e2e");

        var ok = await session.ExecuteInTabAsync(tab.TabId, "Write-Output 'hello'", timeout: TimeSpan.FromSeconds(60));
        Assert.True(ok.Success);
        Assert.Equal(0, ok.ExitCode);
        Assert.Equal("hello", ok.Stdout);

        var failed = await session.ExecuteInTabAsync(tab.TabId, "throw 'boom'", timeout: TimeSpan.FromSeconds(60));
        Assert.False(failed.Success);
        Assert.Equal(1, failed.ExitCode);
        Assert.Contains("boom", failed.Stderr ?? string.Empty, StringComparison.Ordinal);

        // Output after an error must still reach the next command, in order.
        var after = await session.ExecuteInTabAsync(tab.TabId, "Write-Output 'after'", timeout: TimeSpan.FromSeconds(60));
        Assert.True(after.Success);
        Assert.Equal("after", after.Stdout);

        await session.CloseTabAsync(tab.TabId);
        Assert.Null(await session.GetTabAsync(tab.TabId));
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

    /// <summary>
    /// Stands in for wt.exe: "new-tab" starts the wrapper with pwsh directly; the window call is a no-op.
    /// </summary>
    private sealed class DirectTabLauncher : ITerminalProcessLauncher
    {
        private static readonly Regex WrapperArg = new("-File \"([^\"]+)\"", RegexOptions.Compiled);
        private readonly string _wrapperPath;
        private readonly List<Process> _processes;

        public DirectTabLauncher(string wrapperPath, List<Process> processes)
        {
            _wrapperPath = wrapperPath;
            _processes = processes;
        }

        public Process? Start(ProcessStartInfo startInfo)
        {
            var args = startInfo.Arguments;
            var pwshAt = args.IndexOf("pwsh ", StringComparison.Ordinal);
            if (pwshAt < 0)
            {
                return null;
            }

            var pwshArgs = WrapperArg.Replace(args[(pwshAt + 5)..], $"-File \"{_wrapperPath}\"", 1);
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "pwsh",
                Arguments = pwshArgs,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process is not null)
            {
                _processes.Add(process);
            }

            return process;
        }
    }
}
