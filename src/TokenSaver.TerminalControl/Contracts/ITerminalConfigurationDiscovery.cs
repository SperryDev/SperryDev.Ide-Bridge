namespace TokenSaver.TerminalControl.Contracts;

using TokenSaver.TerminalControl.Models;

public interface ITerminalConfigurationDiscovery
{
    /// <summary>
    /// Discovers and returns the current Windows Terminal configuration including profiles, WSL instances, and extensions.
    /// This is typically called once at startup to establish baseline configuration.
    /// </summary>
    Task<TerminalConfiguration> DiscoverConfigurationAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets all available terminal profiles.
    /// </summary>
    Task<IReadOnlyList<TerminalProfile>> GetProfilesAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets all available WSL instances.
    /// </summary>
    Task<IReadOnlyList<WslInstance>> GetWslInstancesAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets all installed Windows Terminal extensions.
    /// </summary>
    Task<IReadOnlyList<TerminalExtension>> GetExtensionsAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the installed Windows Terminal version.
    /// </summary>
    Task<string?> GetTerminalVersionAsync(CancellationToken ct = default);
}
