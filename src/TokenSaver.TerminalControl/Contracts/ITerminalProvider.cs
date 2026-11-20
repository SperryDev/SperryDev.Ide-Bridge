namespace TokenSaver.TerminalControl.Contracts;

using TokenSaver.TerminalControl.Models;

/// <summary>
/// Base interface for terminal providers (Integrated or Windows Terminal)
/// </summary>
public interface ITerminalProvider
{
    TerminalProviderType ProviderType { get; }

    /// <summary>
    /// Checks if this provider is available on the system.
    /// </summary>
    Task<TerminalProviderAvailability> CheckAvailabilityAsync(CancellationToken ct = default);

    /// <summary>
    /// Starts a new session using this provider.
    /// </summary>
    Task<ITerminalSession> CreateSessionAsync(
        TerminalSessionConfig config,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the display name for this provider.
    /// </summary>
    string GetDisplayName();
}

/// <summary>
/// Manages selection and fallback between terminal providers.
/// </summary>
public interface ITerminalProviderSelector
{
    /// <summary>
    /// Selects a provider based on availability and preference.
    /// </summary>
    Task<ITerminalProvider> SelectProviderAsync(
        TerminalProviderSelection selection,
        CancellationToken ct = default);

    /// <summary>
    /// Gets all available providers and their availability status.
    /// </summary>
    Task<IReadOnlyList<TerminalProviderAvailability>> GetAvailableProvidersAsync(
        CancellationToken ct = default);

    /// <summary>
    /// Validates that the preferred provider is available, with user-friendly error.
    /// </summary>
    Task<TerminalProviderValidationResult> ValidateProviderSelectionAsync(
        TerminalProviderSelection selection,
        CancellationToken ct = default);
}

/// <summary>
/// Result of provider validation with actionable error information.
/// </summary>
public sealed record TerminalProviderValidationResult(
    bool IsValid,
    string? ErrorMessage = null,
    Uri? ActionUrl = null,
    string? ActionText = null)
{
    public bool HasInstallAction => ActionUrl is { Scheme: "http" or "https" };
}
