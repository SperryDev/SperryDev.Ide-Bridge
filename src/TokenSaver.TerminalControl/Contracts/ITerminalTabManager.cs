namespace TokenSaver.TerminalControl.Contracts;

using TokenSaver.TerminalControl.Models;

public interface ITerminalTabManager
{
    /// <summary>
    /// Creates a managed tab with automatic naming and tracking.
    /// </summary>
    Task<string> CreateManagedTabAsync(
        string sessionId,
        string tabNamePattern,
        CancellationToken ct = default);

    /// <summary>
    /// Waits for a tab to be ready for command execution.
    /// </summary>
    Task<TerminalTabInfo> WaitForTabReadyAsync(
        string tabId,
        TimeSpan? timeout = null,
        CancellationToken ct = default);

    /// <summary>
    /// Checks if a tab is still alive and active.
    /// </summary>
    Task<bool> IsTabAliveAsync(string tabId, CancellationToken ct = default);

    /// <summary>
    /// Gets auto-incremented name for a tab type.
    /// </summary>
    string GenerateTabName(string sessionId, string typePrefix);
}
