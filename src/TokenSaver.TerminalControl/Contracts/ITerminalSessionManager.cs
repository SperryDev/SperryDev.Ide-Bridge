namespace TokenSaver.TerminalControl.Contracts;

using TokenSaver.TerminalControl.Models;

public interface ITerminalSessionManager
{
    /// <summary>
    /// Initializes the terminal manager by discovering terminal configuration (profiles, WSL, extensions).
    /// Should be called once at startup.
    /// </summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the discovered terminal configuration.
    /// </summary>
    TerminalConfiguration TerminalConfiguration { get; }

    /// <summary>
    /// Creates a new terminal session (elevated or non-elevated).
    /// </summary>
    Task<ITerminalSession> CreateSessionAsync(
        TerminalSessionConfig config,
        CancellationToken ct = default);

    /// <summary>
    /// Gets an active session by ID.
    /// </summary>
    ITerminalSession? GetSession(string sessionId);

    /// <summary>
    /// Lists all active sessions.
    /// </summary>
    IReadOnlyList<ITerminalSession> GetActiveSessions();

    /// <summary>
    /// Closes a session and removes it from the active sessions list.
    /// </summary>
    Task CloseSessionAsync(string sessionId, bool graceful = true, CancellationToken ct = default);

    /// <summary>
    /// Gracefully shuts down all active sessions and cleans up resources.
    /// </summary>
    Task ShutdownAsync(CancellationToken ct = default);
}
