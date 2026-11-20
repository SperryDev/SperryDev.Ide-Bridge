namespace TokenSaver.TerminalControl.Contracts;

using TokenSaver.TerminalControl.Models;

public interface ITerminalSettingsManager
{
    /// <summary>
    /// Temporarily applies terminal settings for the duration of the returned IDisposable.
    /// Settings are reverted when the disposable is disposed.
    /// </summary>
    Task<IAsyncDisposable> ApplyTemporarySettingsAsync(
        TemporaryTerminalSettings settings,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the current active terminal settings.
    /// </summary>
    Task<TerminalConfiguration> GetCurrentSettingsAsync(CancellationToken ct = default);

    /// <summary>
    /// Applies settings to a specific profile temporarily.
    /// </summary>
    Task<IAsyncDisposable> ApplyProfileSettingsAsync(
        string profileGuid,
        ProfileSettings settings,
        CancellationToken ct = default);
}

public sealed record TemporaryTerminalSettings(
    string? DefaultProfileGuid = null,
    IReadOnlyDictionary<string, string>? EnvironmentVariables = null,
    bool? AlwaysShowTabs = null,
    string? ColorScheme = null)
{
}

public sealed record ProfileSettings(
    string? TabTitle = null,
    string? CommandLine = null,
    string? StartingDirectory = null,
    bool? UseAcrylic = null,
    double? AcrylicOpacity = null,
    string? ColorScheme = null)
{
}
