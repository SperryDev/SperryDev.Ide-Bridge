namespace TokenSaver.TerminalControl.Contracts;

using TokenSaver.TerminalControl.Models;

/// <summary>
/// Registry for tracking and managing active terminal sessions with metadata.
/// </summary>
public interface ISessionRegistry
{
    /// <summary>
    /// Registers a new session in the registry.
    /// </summary>
    Task<SessionMetadata> RegisterSessionAsync(
        string sessionId,
        string name,
        string openedBy,
        bool isElevated,
        CancellationToken ct = default);

    /// <summary>
    /// Gets metadata for a registered session.
    /// </summary>
    Task<SessionMetadata?> GetSessionAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Lists all active sessions.
    /// </summary>
    Task<IReadOnlyList<SessionMetadata>> GetActiveSessionsAsync(CancellationToken ct = default);

    /// <summary>
    /// Lists all sessions (active and inactive).
    /// </summary>
    Task<IReadOnlyList<SessionMetadata>> GetAllSessionsAsync(CancellationToken ct = default);

    /// <summary>
    /// Updates the current activity for a session.
    /// </summary>
    Task UpdateActivityAsync(
        string sessionId,
        string activity,
        string? tabId = null,
        string? details = null,
        CancellationToken ct = default);

    /// <summary>
    /// Adds a tab to a session's tab list.
    /// </summary>
    Task AddTabToSessionAsync(
        string sessionId,
        string tabId,
        CancellationToken ct = default);

    /// <summary>
    /// Removes a tab from a session's tab list.
    /// </summary>
    Task RemoveTabFromSessionAsync(
        string sessionId,
        string tabId,
        CancellationToken ct = default);

    /// <summary>
    /// Closes and unregisters a session.
    /// </summary>
    Task CloseSessionAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Gets the activity history for a session.
    /// </summary>
    Task<IReadOnlyList<SessionActivity>> GetActivityHistoryAsync(
        string sessionId,
        int? maxEntries = 100,
        CancellationToken ct = default);

    /// <summary>
    /// Clears the activity history for a session.
    /// </summary>
    Task ClearActivityHistoryAsync(string sessionId, CancellationToken ct = default);
}
