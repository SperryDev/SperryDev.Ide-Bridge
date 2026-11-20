namespace TokenSaver.TerminalControl.Core;

using Microsoft.Extensions.Logging;
using TokenSaver.TerminalControl.Contracts;
using TokenSaver.TerminalControl.Models;
using TokenSaver.TerminalControl.Output;

public sealed class TerminalProviderSelector : ITerminalProviderSelector
{
    private readonly NamedPipeManager _pipeManager;
    private readonly ILogger<TerminalProviderSelector> _logger;
    private readonly Dictionary<TerminalProviderType, ITerminalProvider> _providers;

    public TerminalProviderSelector(
        NamedPipeManager pipeManager,
        ILogger<TerminalProviderSelector> logger)
    {
        _pipeManager = pipeManager;
        _logger = logger;

        using var loggerFactory = LoggerFactory.Create(b => b.AddDebug());
        var wtLogger = loggerFactory.CreateLogger<WindowsTerminalProvider>();
        var intLogger = loggerFactory.CreateLogger<IntegratedTerminalProvider>();

        _providers = new Dictionary<TerminalProviderType, ITerminalProvider>
        {
            { TerminalProviderType.WindowsTerminal, new WindowsTerminalProvider(pipeManager, wtLogger) },
            { TerminalProviderType.Integrated, new IntegratedTerminalProvider(pipeManager, intLogger) }
        };
    }

    public async Task<ITerminalProvider> SelectProviderAsync(
        TerminalProviderSelection selection,
        CancellationToken ct = default)
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Selecting terminal provider: Preferred={Preferred}, Fallback={Fallback}",
                selection.PreferredProvider,
                selection.FallbackProvider);
        }

        var preferred = _providers[selection.PreferredProvider];
        var availability = await preferred.CheckAvailabilityAsync(ct);

        if (availability.IsAvailable)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Using preferred provider: {Provider}", preferred.GetDisplayName());
            }
            return preferred;
        }

        _logger.LogWarning(
            "Preferred provider {Preferred} not available, falling back to {Fallback}",
            selection.PreferredProvider,
            selection.FallbackProvider);

        return _providers[selection.FallbackProvider];
    }

    public async Task<IReadOnlyList<TerminalProviderAvailability>> GetAvailableProvidersAsync(
        CancellationToken ct = default)
    {
        var results = new List<TerminalProviderAvailability>();

        foreach (var provider in _providers.Values)
        {
            var availability = await provider.CheckAvailabilityAsync(ct);
            results.Add(availability);
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Provider {Provider}: Available={IsAvailable}",
                    provider.GetDisplayName(),
                    availability.IsAvailable);
            }
        }

        return results;
    }

    public async Task<TerminalProviderValidationResult> ValidateProviderSelectionAsync(
        TerminalProviderSelection selection,
        CancellationToken ct = default)
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Validating provider selection: {Provider}", selection.PreferredProvider);
        }

        var preferred = _providers[selection.PreferredProvider];
        var availability = await preferred.CheckAvailabilityAsync(ct);

        if (availability.IsAvailable)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Provider validation passed: {Provider} is available",
                    preferred.GetDisplayName());
            }

            return new TerminalProviderValidationResult(IsValid: true);
        }

        // Build user-friendly error message with action
        var errorMessage = $"{preferred.GetDisplayName()} is not installed on this system. " +
                          "Please install it from the Microsoft Store to use this provider, " +
                          "or switch to the Integrated Terminal option.";

        _logger.LogWarning("Provider validation failed: {Provider} - {Error}", preferred.GetDisplayName(), errorMessage);

        return new TerminalProviderValidationResult(
            IsValid: false,
            ErrorMessage: errorMessage,
            ActionUrl: availability.InstallUrl,
            ActionText: "Install from Microsoft Store");
    }
}
