namespace TokenSaver.Common.Core.Contracts;

/// <summary>
///     Contract for launching IDE instances. Implementations handle opening Visual Studio Code,
///     Visual Studio, or other IDEs with specified workspace roots.
///     See docs/ide-launcher-contract.md.
/// </summary>
public interface IIDELauncher
{
    /// <summary>
    ///     Launch an IDE instance with the specified workspace root.
    /// </summary>
    /// <param name="ideType">IDE type: "VsCode" or "VisualStudio2026" (future: "Rider", "Sublime", etc.)</param>
    /// <param name="workspaceRoot">Absolute path to the workspace root to open</param>
    /// <param name="waitForReady">If true, wait for the IDE to report as ready before returning</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>
    ///     Diagnostic info including process ID, port (if HTTP-based), and readiness status.
    ///     Format: { "processId": 1234, "port": 11437, "ready": true, "message": "Visual Studio Code started" }
    /// </returns>
    Task<IDELaunchResult> LaunchAsync(
        string ideType,
        string workspaceRoot,
        bool waitForReady = true,
        CancellationToken ct = default);

    /// <summary>
    ///     Query the status of a running IDE instance.
    /// </summary>
    /// <param name="ideType">IDE type to query</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Status including process ID, port, and readiness</returns>
    Task<IDEStatusResult> QueryStatusAsync(string ideType, CancellationToken ct = default);

    /// <summary>
    ///     Stop a running IDE instance.
    /// </summary>
    /// <param name="ideType">IDE type to stop</param>
    /// <param name="graceful">If true, request graceful shutdown; if false, force-kill</param>
    /// <param name="ct">Cancellation token</param>
    Task StopAsync(string ideType, bool graceful = true, CancellationToken ct = default);
}

public sealed record IDELaunchResult(
    int ProcessId,
    int? Port,
    bool Ready,
    string Message,
    IDictionary<string, object>? Metadata = null);

public sealed record IDEStatusResult(
    string IdeType,
    bool IsRunning,
    int? ProcessId,
    int? Port,
    bool Ready,
    string? WorkspaceRoot = null);
