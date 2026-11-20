namespace TokenSaver.TerminalControl.Core;

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TokenSaver.TerminalControl.Contracts;
using TokenSaver.TerminalControl.Models;
using TokenSaver.TerminalControl.Output;

public sealed class WindowsTerminalProvider : ITerminalProvider
{
    private readonly NamedPipeManager _pipeManager;
    private readonly ILogger<WindowsTerminalProvider> _logger;

    public TerminalProviderType ProviderType => TerminalProviderType.WindowsTerminal;

    public WindowsTerminalProvider(
        NamedPipeManager pipeManager,
        ILogger<WindowsTerminalProvider> logger)
    {
        _pipeManager = pipeManager;
        _logger = logger;
    }

    public async Task<TerminalProviderAvailability> CheckAvailabilityAsync(CancellationToken ct = default)
    {
        // Guard against infinite recursion or excessive retries
        const int MaxAttempts = 1;
        int attempts = 0;

        while (attempts < MaxAttempts)
        {
            attempts++;

            try
            {
                var wtPath = FindWindowsTerminal();
                if (string.IsNullOrEmpty(wtPath))
                {
                    return new TerminalProviderAvailability(
                        Provider: TerminalProviderType.WindowsTerminal,
                        IsInstalled: false,
                        Version: null,
                        InstallUrl: new Uri("https://www.microsoft.com/store/productId/9N0DX20HK701"),
                        ErrorMessage: "Windows Terminal is not installed");
                }

                var version = await GetWindowsTerminalVersionAsync(ct);

                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation(
                        "Windows Terminal detected: {Path}, Version: {Version}",
                        wtPath,
                        version ?? "Unknown");
                }

                return new TerminalProviderAvailability(
                    Provider: TerminalProviderType.WindowsTerminal,
                    IsInstalled: true,
                    Version: version);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking Windows Terminal availability (attempt {Attempt}/{MaxAttempts})", attempts, MaxAttempts);

                if (attempts >= MaxAttempts)
                {
                    return new TerminalProviderAvailability(
                        Provider: TerminalProviderType.WindowsTerminal,
                        IsInstalled: false,
                        ErrorMessage: $"Error checking availability: {ex.Message}",
                        InstallUrl: new Uri("https://www.microsoft.com/store/productId/9N0DX20HK701"));
                }
            }
        }

        // Fallback (should not reach here)
        return new TerminalProviderAvailability(
            Provider: TerminalProviderType.WindowsTerminal,
            IsInstalled: false,
            ErrorMessage: "Unable to check Windows Terminal availability",
            InstallUrl: new Uri("https://www.microsoft.com/store/productId/9N0DX20HK701"));
    }

    public async Task<ITerminalSession> CreateSessionAsync(
        TerminalSessionConfig config,
        CancellationToken ct = default)
    {
        // For now, use elevated/standard session implementations
        ITerminalSession session;
        if (config.IsElevated)
        {
            using var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(b => b.AddDebug());
            session = new ElevatedTerminalSession(
                loggerFactory.CreateLogger<ElevatedTerminalSession>(),
                _pipeManager);
        }
        else
        {
            using var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(b => b.AddDebug());
            session = new StandardTerminalSession(
                loggerFactory.CreateLogger<StandardTerminalSession>(),
                _pipeManager);
        }

        await session.InitializeAsync(config, ct);
        return session;
    }

    public string GetDisplayName() => "Windows Terminal";

    private static string? FindWindowsTerminal()
    {
        // Check common Windows Terminal locations
        var possiblePaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WindowsApps", "wt.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "WindowsApps", "wt.exe"),
            "wt.exe"  // Try PATH
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path) || TryResolveInPath(path))
            {
                return path;
            }
        }

        return null;
    }

    private static bool TryResolveInPath(string exeName)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "where.exe",
                    Arguments = exeName,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadLine();
            process.WaitForExit();

            return !string.IsNullOrEmpty(output) && File.Exists(output);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string?> GetWindowsTerminalVersionAsync(CancellationToken ct = default)
    {
        try
        {
            // Avoid running wt.exe to prevent Help dialog recursion
            // If we found the executable, that's enough to confirm installation
            // Version detection is non-critical and can fail silently
            await Task.Delay(0, ct); // Preserve async signature
            return "installed";
        }
        catch
        {
            return null;
        }
    }
}
