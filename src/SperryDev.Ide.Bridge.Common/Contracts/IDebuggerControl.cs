namespace TokenSaver.Common.Core.Contracts;

/// <summary>
///     Contract for controlling debuggers in IDEs (VS Code, Visual Studio).
///     Allows starting/stopping debug sessions, setting breakpoints, and stepping through code.
///     See docs/debugger-control-contract.md.
/// </summary>
public interface IDebuggerControl
{
    /// <summary>
    ///     Start a debug session in an IDE.
    /// </summary>
    /// <param name="ideType">IDE type: "VsCode" or "VisualStudio2026"</param>
    /// <param name="configName">Debug configuration name (e.g., "launch", ".NET Core Attach")</param>
    /// <param name="args">Optional arguments to pass to the debugger (e.g., program path, port)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Debug session ID and status</returns>
    Task<DebugSessionResult> StartDebugSessionAsync(
        string ideType,
        string configName,
        IDictionary<string, object>? args = null,
        CancellationToken ct = default);

    /// <summary>
    ///     Stop an active debug session.
    /// </summary>
    /// <param name="ideType">IDE type to stop debugging in</param>
    /// <param name="sessionId">Optional: specific session ID (if multiple sessions active)</param>
    /// <param name="ct">Cancellation token</param>
    Task StopDebugSessionAsync(
        string ideType,
        string? sessionId = null,
        CancellationToken ct = default);

    /// <summary>
    ///     Set a breakpoint at a file location.
    /// </summary>
    /// <param name="ideType">IDE type</param>
    /// <param name="filePath">Absolute file path</param>
    /// <param name="lineNumber">1-indexed line number</param>
    /// <param name="condition">Optional: breakpoint condition (e.g., "x > 5")</param>
    /// <param name="hitCount">Optional: break when hit count reaches this value</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Breakpoint info with ID</returns>
    Task<BreakpointResult> SetBreakpointAsync(
        string ideType,
        string filePath,
        int lineNumber,
        string? condition = null,
        int? hitCount = null,
        CancellationToken ct = default);

    /// <summary>
    ///     Remove a breakpoint.
    /// </summary>
    /// <param name="ideType">IDE type</param>
    /// <param name="breakpointId">Breakpoint ID (from SetBreakpointAsync)</param>
    /// <param name="ct">Cancellation token</param>
    Task RemoveBreakpointAsync(
        string ideType,
        string breakpointId,
        CancellationToken ct = default);

    /// <summary>
    ///     List all breakpoints in a debug session.
    /// </summary>
    /// <param name="ideType">IDE type</param>
    /// <param name="sessionId">Optional: specific session ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of active breakpoints</returns>
    Task<IReadOnlyList<BreakpointInfo>> ListBreakpointsAsync(
        string ideType,
        string? sessionId = null,
        CancellationToken ct = default);

    /// <summary>
    ///     Execute a step command in the debugger.
    /// </summary>
    /// <param name="ideType">IDE type</param>
    /// <param name="command">"StepIn", "StepOver", "StepOut", "Continue", "Pause", "Restart"</param>
    /// <param name="threadId">Optional: specific thread/frame to step in</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Execution result with new position</returns>
    Task<ExecutionResult> ExecuteStepAsync(
        string ideType,
        DebugCommand command,
        string? threadId = null,
        CancellationToken ct = default);

    /// <summary>
    ///     Get current debug session state (call stack, variables, breakpoints).
    /// </summary>
    /// <param name="ideType">IDE type</param>
    /// <param name="sessionId">Optional: specific session ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Current debug state</returns>
    Task<DebugStateResult> GetDebugStateAsync(
        string ideType,
        string? sessionId = null,
        CancellationToken ct = default);
}

public enum DebugCommand
{
    StepIn,
    StepOver,
    StepOut,
    Continue,
    Pause,
    Restart
}

public sealed record DebugSessionResult(
    string SessionId,
    string IdeType,
    bool Success,
    string Message,
    string? ConfigName = null);

public sealed record BreakpointResult(
    string BreakpointId,
    string FilePath,
    int LineNumber,
    bool Verified,
    string Message);

public sealed record BreakpointInfo(
    string BreakpointId,
    string FilePath,
    int LineNumber,
    bool Verified,
    string? Condition = null,
    int? HitCount = null,
    int CurrentHitCount = 0);

public sealed record ExecutionResult(
    bool Success,
    DebugCommand Command,
    string? CurrentFile = null,
    int? CurrentLine = null,
    string? Reason = null, // e.g., "breakpoint", "step", "pause"
    string? Message = null);

public sealed record DebugStateResult(
    string SessionId,
    bool IsRunning,
    string? CurrentFile,
    int? CurrentLine,
    IReadOnlyList<StackFrame> CallStack,
    IDictionary<string, Variable> Variables);

public sealed record StackFrame(
    string Id,
    string Source,
    int Line,
    string Function,
    int Index);

public sealed record Variable(
    string Name,
    string Value,
    string Type,
    int? VariablesReference = null);
