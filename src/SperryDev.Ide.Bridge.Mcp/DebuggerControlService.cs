using System.Text.Json;

using TokenSaver.Common.Core.Contracts;

namespace TokenSaver.MCP.Services;

/// <summary>
///     Controls the debugger of a running IDE window (Visual Studio or VS Code) through its bridge. Every call acts on
///     the real debugger and reports what the IDE said; when no matching IDE is running, or the IDE refuses, the
///     call fails with that reason instead of reporting success. Breakpoint conditions and hit counts, and the
///     Pause and Restart commands, are not supported by the bridges and are refused rather than ignored.
/// </summary>
internal sealed class DebuggerControlService(IIdeBridge bridge) : IDebuggerControl
{
    public async Task<DebugSessionResult> StartDebugSessionAsync(
        string ideType, string configName, IDictionary<string, object>? args = null, CancellationToken ct = default)
    {
        string ide = BridgeIde(ideType);
        BridgeReply reply = await bridge.CallAsync(ide, "startDebugging", new { configuration = configName }, ct);
        return new DebugSessionResult(
            SessionId: SessionId(ide, reply),
            IdeType: ideType,
            Success: reply.Ok,
            Message: reply.Ok ? $"Debugging started in {ide}" : reply.Error ?? "The IDE did not start debugging.",
            ConfigName: configName);
    }

    public async Task StopDebugSessionAsync(string ideType, string? sessionId = null, CancellationToken ct = default)
    {
        BridgeReply reply = await bridge.CallAsync(BridgeIde(ideType), "stopDebugging", new { }, ct);
        Require(reply, "stop debugging");
    }

    public async Task<BreakpointResult> SetBreakpointAsync(
        string ideType, string filePath, int lineNumber, string? condition = null, int? hitCount = null, CancellationToken ct = default)
    {
        string id = BreakpointId(filePath, lineNumber);
        if (!string.IsNullOrWhiteSpace(condition) || hitCount is not null)
        {
            return new BreakpointResult(id, filePath, lineNumber, Verified: false,
                "Conditions and hit counts are not supported by the IDE bridge; no breakpoint was set.");
        }

        BridgeReply reply = await bridge.CallAsync(BridgeIde(ideType), "setBreakpoint", new { file = filePath, line = lineNumber }, ct);
        return new BreakpointResult(id, filePath, lineNumber, reply.Ok,
            reply.Ok ? "Breakpoint set" : reply.Error ?? "The IDE did not set the breakpoint.");
    }

    public async Task RemoveBreakpointAsync(string ideType, string breakpointId, CancellationToken ct = default)
    {
        (string file, int line) = ParseBreakpointId(breakpointId);
        BridgeReply reply = await bridge.CallAsync(BridgeIde(ideType), "removeBreakpoint", new { file, line }, ct);
        Require(reply, "remove the breakpoint");
    }

    public async Task<IReadOnlyList<BreakpointInfo>> ListBreakpointsAsync(string ideType, string? sessionId = null, CancellationToken ct = default)
    {
        BridgeReply reply = await bridge.CallAsync(BridgeIde(ideType), "listBreakpoints", new { }, ct);
        Require(reply, "list breakpoints");

        List<BreakpointInfo> list = [];
        if (reply.Result is { } result && result.TryGetProperty("breakpoints", out JsonElement items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in items.EnumerateArray())
            {
                string? file = item.TryGetProperty("file", out JsonElement f) ? f.GetString() : null;
                if (file is null || !item.TryGetProperty("line", out JsonElement l) || !l.TryGetInt32(out int line))
                {
                    continue;
                }

                bool enabled = !item.TryGetProperty("enabled", out JsonElement en) || en.ValueKind != JsonValueKind.False;
                string? condition = item.TryGetProperty("condition", out JsonElement c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                list.Add(new BreakpointInfo(BreakpointId(file, line), file, line, enabled, string.IsNullOrEmpty(condition) ? null : condition));
            }
        }

        return list;
    }

    public async Task<ExecutionResult> ExecuteStepAsync(string ideType, DebugCommand command, string? threadId = null, CancellationToken ct = default)
    {
        string ide = BridgeIde(ideType);
        BridgeReply reply;
        switch (command)
        {
            case DebugCommand.StepIn:
            case DebugCommand.StepOver:
            case DebugCommand.StepOut:
                string kind = command switch { DebugCommand.StepIn => "into", DebugCommand.StepOver => "over", _ => "out" };
                // VS reads "mode", VS Code reads "kind".
                reply = await bridge.CallAsync(ide, "step", new { mode = kind, kind }, ct);
                break;
            case DebugCommand.Continue:
                // In Visual Studio "Go" continues a paused debugger; VS Code has an explicit continue step.
                reply = ide == "VsCode"
                    ? await bridge.CallAsync(ide, "step", new { mode = "continue", kind = "continue" }, ct)
                    : await bridge.CallAsync(ide, "startDebugging", new { }, ct);
                break;
            default:
                return new ExecutionResult(false, command, Message: $"{command} is not supported by the IDE bridge.");
        }

        return new ExecutionResult(reply.Ok, command, Message: reply.Ok ? $"{command} done" : reply.Error);
    }

    public async Task<DebugStateResult> GetDebugStateAsync(string ideType, string? sessionId = null, CancellationToken ct = default)
    {
        string ide = BridgeIde(ideType);
        BridgeReply reply = await bridge.CallAsync(ide, "debugState", new { }, ct);
        Require(reply, "read the debugger state");

        // Visual Studio: debugger_mode "dbgRunMode" | "dbgBreakMode" | "dbgDesignMode". VS Code: mode "debugging" | "none".
        string mode = reply.Result is { } r
            ? Text(r, "debugger_mode") ?? Text(r, "mode") ?? ""
            : "";
        bool running = mode.Contains("Run", StringComparison.OrdinalIgnoreCase) || mode.Equals("debugging", StringComparison.OrdinalIgnoreCase);
        string? function = reply.Result is { } fr ? Text(fr, "function") : null;

        return new DebugStateResult(
            SessionId: SessionId(ide, reply),
            IsRunning: running,
            CurrentFile: null,
            CurrentLine: null,
            CallStack: function is null ? [] : [new StackFrame("0", "", 0, function, 0)],
            Variables: new Dictionary<string, Variable>());
    }

    /// <summary>The bridge's IDE kind for an IDE type: "VsCode" stays, any "VisualStudio…" is "VisualStudio".</summary>
    internal static string BridgeIde(string ideType)
    {
        if (ideType.Equals("VsCode", StringComparison.OrdinalIgnoreCase))
        {
            return "VsCode";
        }

        if (ideType.StartsWith("VisualStudio", StringComparison.OrdinalIgnoreCase))
        {
            return "VisualStudio";
        }

        throw new ArgumentException($"Unknown IDE type: {ideType}. Expected 'VsCode' or 'VisualStudio2026'", nameof(ideType));
    }

    internal static string BreakpointId(string file, int line) => $"{file}:{line}";

    internal static (string File, int Line) ParseBreakpointId(string breakpointId)
    {
        int colon = breakpointId.LastIndexOf(':');
        if (colon <= 0 || !int.TryParse(breakpointId[(colon + 1)..], out int line) || line < 1)
        {
            throw new ArgumentException($"'{breakpointId}' is not a breakpoint id (expected 'file:line').", nameof(breakpointId));
        }

        return (breakpointId[..colon], line);
    }

    private static string SessionId(string ide, BridgeReply reply) => reply.Pid is { } pid ? $"{ide}-{pid}" : ide;

    private static void Require(BridgeReply reply, string what)
    {
        if (!reply.Ok)
        {
            throw new InvalidOperationException(reply.Error ?? $"The IDE could not {what}.");
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
