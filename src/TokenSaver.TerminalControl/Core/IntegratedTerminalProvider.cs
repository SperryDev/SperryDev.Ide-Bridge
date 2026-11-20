namespace TokenSaver.TerminalControl.Core;

using Microsoft.Extensions.Logging;
using TokenSaver.TerminalControl.Contracts;
using TokenSaver.TerminalControl.Models;
using TokenSaver.TerminalControl.Output;

public sealed class IntegratedTerminalProvider : ITerminalProvider
{
    private readonly NamedPipeManager _pipeManager;
    private readonly ILogger<IntegratedTerminalProvider> _logger;

    public TerminalProviderType ProviderType => TerminalProviderType.Integrated;

    public IntegratedTerminalProvider(
        NamedPipeManager pipeManager,
        ILogger<IntegratedTerminalProvider> logger)
    {
        _pipeManager = pipeManager;
        _logger = logger;
    }

    public Task<TerminalProviderAvailability> CheckAvailabilityAsync(CancellationToken ct = default)
    {
        // Integrated terminal (PowerShell/CMD) is always available on Windows
        _logger.LogInformation("Integrated terminal provider is available (PowerShell/CMD)");

        return Task.FromResult(new TerminalProviderAvailability(
            Provider: TerminalProviderType.Integrated,
            IsInstalled: true,
            Version: GetPowerShellVersion()));
    }

    public async Task<ITerminalSession> CreateSessionAsync(
        TerminalSessionConfig config,
        CancellationToken ct = default)
    {
        // Integrated terminal uses headless PowerShell/CMD sessions
        // For now, reuse the session implementations but with direct process execution
        // Future: create IntegratedTerminalSession for headless-specific implementation

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

    public string GetDisplayName() => "Integrated Terminal (PowerShell/CMD)";

    private static string? GetPowerShellVersion()
    {
        try
        {
            var psVersion = $"{System.Environment.OSVersion.Version}";
            return psVersion;
        }
        catch
        {
            return null;
        }
    }
}
