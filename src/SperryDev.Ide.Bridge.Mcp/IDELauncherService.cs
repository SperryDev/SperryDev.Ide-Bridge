using TokenSaver.Common.Core.Contracts;

using Microsoft.Extensions.Logging;

using System.Diagnostics;

namespace TokenSaver.MCP.Services;

/// <summary>
/// Launches and manages IDE instances (VS Code, Visual Studio, etc.).
/// Handles process management, readiness polling, and status queries.
/// </summary>
internal sealed class IDELauncherService : IIDELauncher
{
    private readonly ILogger<IDELauncherService> _logger;
    private readonly Dictionary<string, IDEProcess> _runningIDEs = new();
    private readonly object _lock = new();

    public IDELauncherService(ILogger<IDELauncherService> logger)
    {
        _logger = logger;
    }

    public async Task<IDELaunchResult> LaunchAsync(
        string ideType,
        string workspaceRoot,
        bool waitForReady = true,
        CancellationToken ct = default)
    {
        ValidateIdeType(ideType);

        if (!Directory.Exists(workspaceRoot))
        {
            return new IDELaunchResult(
                ProcessId: 0,
                Port: null,
                Ready: false,
                Message: $"Workspace root does not exist: {workspaceRoot}");
        }

        try
        {
            lock (_lock)
            {
                if (_runningIDEs.TryGetValue(ideType, out var existing) && !existing.Process.HasExited)
                {
                    return new IDELaunchResult(
                        ProcessId: existing.Process.Id,
                        Port: existing.Port,
                        Ready: true,
                        Message: $"{ideType} is already running");
                }
            }

            var (process, port) = ideType switch
            {
                "VsCode" => LaunchVsCode(workspaceRoot),
                "VisualStudio2026" => LaunchVisualStudio(workspaceRoot),
                _ => throw new ArgumentException($"Unknown IDE type: {ideType}")
            };

            lock (_lock)
            {
                _runningIDEs[ideType] = new IDEProcess { Process = process, Port = port };
            }

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Launched {IdeType} (PID {ProcessId}) for {WorkspaceRoot}", ideType, process.Id, workspaceRoot);
            }

            if (waitForReady)
            {
                await WaitForReadyAsync(ideType, port, ct);
            }

            return new IDELaunchResult(
                ProcessId: process.Id,
                Port: port,
                Ready: true,
                Message: $"{ideType} started successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch {IdeType}", ideType);
            return new IDELaunchResult(
                ProcessId: 0,
                Port: null,
                Ready: false,
                Message: $"Failed to launch {ideType}: {ex.Message}");
        }
    }

    public Task<IDEStatusResult> QueryStatusAsync(string ideType, CancellationToken ct = default)
    {
        ValidateIdeType(ideType);

        lock (_lock)
        {
            if (!_runningIDEs.TryGetValue(ideType, out var ide))
            {
                return Task.FromResult(new IDEStatusResult(
                    IdeType: ideType,
                    IsRunning: false,
                    ProcessId: null,
                    Port: null,
                    Ready: false,
                    WorkspaceRoot: null));
            }

            bool isRunning = !ide.Process.HasExited;
            return Task.FromResult(new IDEStatusResult(
                IdeType: ideType,
                IsRunning: isRunning,
                ProcessId: isRunning ? ide.Process.Id : null,
                Port: ide.Port,
                Ready: isRunning,
                WorkspaceRoot: null));
        }
    }

    public async Task StopAsync(string ideType, bool graceful = true, CancellationToken ct = default)
    {
        ValidateIdeType(ideType);

        // Look up under the lock, but wait for the process outside it so no thread blocks while holding _lock.
        IDEProcess? ide = null;
        lock (_lock)
        {
            if (_runningIDEs.TryGetValue(ideType, out var candidate) && !candidate.Process.HasExited)
            {
                ide = candidate;
            }
        }

        if (ide is null)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("{IdeType} is not running", ideType);
            }
            return;
        }

        try
        {
            if (graceful)
            {
                ide.Process.CloseMainWindow();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                try
                {
                    await ide.Process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    _logger.LogWarning("Graceful shutdown timeout for {IdeType}, force-killing", ideType);
                    ide.Process.Kill();
                }
            }
            else
            {
                ide.Process.Kill();
            }

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Stopped {IdeType}", ideType);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping {IdeType}", ideType);
        }
        finally
        {
            lock (_lock)
            {
                _ = _runningIDEs.Remove(ideType);
            }
        }

        await Task.CompletedTask;
    }

    private (Process, int?) LaunchVsCode(string workspaceRoot)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "code",
                Arguments = $"\"{workspaceRoot}\"",
                UseShellExecute = false,
                CreateNoWindow = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        process.Start();
        ManagedProcessRegistry.Track(process, "IDE launch");
        return (process, null);
    }

    private (Process, int?) LaunchVisualStudio(string workspaceRoot)
    {
        // VS uses devenv.exe; ideally we'd find the latest VS installation
        var vsPath = FindVisualStudioPath();

        if (vsPath == null)
        {
            throw new InvalidOperationException("Visual Studio 2026+ installation not found");
        }

        var slnFile = FindSolutionFile(workspaceRoot);
        var targetPath = slnFile ?? workspaceRoot;

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = vsPath,
                Arguments = $"\"{targetPath}\"",
                UseShellExecute = false,
                CreateNoWindow = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        process.Start();
        ManagedProcessRegistry.Track(process, "IDE launch");
        return (process, null);
    }

    private async Task WaitForReadyAsync(string ideType, int? port, CancellationToken ct)
    {
        var maxRetries = 30;
        var retryCount = 0;

        while (retryCount < maxRetries && !ct.IsCancellationRequested)
        {
            try
            {
                lock (_lock)
                {
                    if (_runningIDEs.TryGetValue(ideType, out var ide) && !ide.Process.HasExited)
                    {
                        if (_logger.IsEnabled(LogLevel.Information))
                        {
                            _logger.LogInformation("{IdeType} is ready", ideType);
                        }
                        return;
                    }
                }

                await Task.Delay(500, ct);
                retryCount++;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Readiness check cancelled");
                break;
            }
        }
    }

    internal static string? FindVisualStudioPath()
    {
        // Common VS 2022/2026 paths on Windows
        var possiblePaths = new[]
        {
            @"C:\Program Files\Microsoft Visual Studio\2026\Preview\Common7\IDE\devenv.exe",
            @"C:\Program Files\Microsoft Visual Studio\2026\Professional\Common7\IDE\devenv.exe",
            @"C:\Program Files (x86)\Microsoft Visual Studio\2022\Professional\Common7\IDE\devenv.exe",
            @"C:\Program Files\Microsoft Visual Studio\2022\Professional\Common7\IDE\devenv.exe"
        };

        return possiblePaths.FirstOrDefault(File.Exists);
    }

    private string? FindSolutionFile(string workspaceRoot)
    {
        try
        {
            return Directory.EnumerateFiles(workspaceRoot, "*.sln", SearchOption.TopDirectoryOnly)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static void ValidateIdeType(string ideType)
    {
        if (ideType != "VsCode" && ideType != "VisualStudio2026")
        {
            throw new ArgumentException($"Invalid IDE type '{ideType}'. Must be 'VsCode' or 'VisualStudio2026'.", nameof(ideType));
        }
    }

    private sealed class IDEProcess
    {
        public required Process Process { get; set; }
        public int? Port { get; set; }
    }
}
