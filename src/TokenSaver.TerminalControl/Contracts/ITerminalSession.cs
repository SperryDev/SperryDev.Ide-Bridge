namespace TokenSaver.TerminalControl.Contracts;

using TokenSaver.TerminalControl.Models;

public interface ITerminalSession
{
    string SessionId { get; }
    bool IsElevated { get; }
    int ProcessId { get; }
    TerminalSessionState State { get; }
    TerminalConfiguration Configuration { get; }

    /// <summary>
    /// Initializes the terminal session with the provided configuration.
    /// Launches the Windows Terminal process and creates the placeholder tab.
    /// </summary>
    Task InitializeAsync(TerminalSessionConfig config, CancellationToken ct = default);

    /// <summary>
    /// Creates a new tab in the terminal session.
    /// </summary>
    Task<TerminalTabInfo> CreateTabAsync(
        string tabName,
        string? initialCommand = null,
        CancellationToken ct = default);

    /// <summary>
    /// Executes a command in a specific tab and captures output.
    /// </summary>
    Task<TerminalExecutionResult> ExecuteInTabAsync(
        string tabId,
        string command,
        string? workingDirectory = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default);

    /// <summary>
    /// Closes a specific tab.
    /// </summary>
    Task CloseTabAsync(string tabId, CancellationToken ct = default);

    /// <summary>
    /// Terminates the entire terminal session.
    /// </summary>
    Task TerminateAsync(bool graceful = true, CancellationToken ct = default);

    /// <summary>
    /// Lists all active tabs in the session.
    /// </summary>
    IAsyncEnumerable<TerminalTabInfo> ListTabsAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets information about a specific tab.
    /// </summary>
    Task<TerminalTabInfo?> GetTabAsync(string tabId, CancellationToken ct = default);

    /// <summary>
    /// Checks if the terminal process is still alive.
    /// </summary>
    Task<bool> IsAliveAsync(CancellationToken ct = default);
}

public enum TerminalSessionState
{
    Initializing,
    Ready,
    Closing,
    Terminated
}
