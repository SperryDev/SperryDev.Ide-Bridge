> **Historical contract:** This page describes the former combined TokenSaver MCP surface and may not match the current bridge MCP tools. For current behavior, use [the IDE bridge guide](../ide-bridge.md) and [the bridge protocol](../../conformance/protocol.md).
# Debugger Control Contract

**Status**: Public MCP tool surface  
**Version**: 1.0  
**Last Updated**: 2026-07-11

## Overview

The Debugger Control contract defines how the MCP server manages debug sessions, breakpoints, and execution control in VS Code and Visual Studio. This enables calling AI to start debugging, set breakpoints, step through code, and inspect program state.

## Architecture

```
MCP Server ← Named Pipe → IDE Extension ← VSCode.debug API / VS.Debugger API
            (debug commands)              (actual debug control)
```

The MCP sends commands to the IDE extension via named pipes. The extension executes commands in the actual debugger (VS Code Debug Adapter, Visual Studio debugger) and reports results back.

## Public Interface

Six MCP tools expose debugger control:

### StartDebugSession

Start a debug session in an IDE.

**Input:**
```json
{
  "ideType": "VsCode",
  "configName": "launch",
  "argsJson": "{\"port\": 5000, \"program\": \"bin/Debug/app.dll\"}"
}
```

**Output:**
```json
{
  "success": true,
  "session_id": "a1b2c3d4e5f6",
  "ide_type": "VsCode",
  "config_name": "launch",
  "message": "Debug session started for launch"
}
```

**Behavior:**
- Requires a debug configuration to exist in the IDE (e.g., `launch.json` in VS Code)
- Optional `argsJson` overrides/provides arguments to the configuration
- Returns a session ID for use in other commands
- Fails gracefully if configuration doesn't exist

### StopDebugSession

Stop an active debug session.

**Input:**
```json
{
  "ideType": "VsCode",
  "sessionId": "a1b2c3d4e5f6"
}
```

**Output:**
```json
{
  "success": true,
  "message": "Debug session stopped for VsCode"
}
```

**Behavior:**
- If `sessionId` not provided, stops all debug sessions for that IDE
- Always returns success even if session doesn't exist (idempotent)

### SetBreakpoint

Set a breakpoint at a file location.

**Input:**
```json
{
  "ideType": "VsCode",
  "filePath": "F:\\mcp\\TokenSaver\\src\\Program.cs",
  "lineNumber": 42,
  "condition": "x > 0",
  "hitCount": 5
}
```

**Output:**
```json
{
  "success": true,
  "breakpoint_id": "bp_deadbeef",
  "file_path": "F:\\mcp\\TokenSaver\\src\\Program.cs",
  "line_number": 42,
  "verified": true,
  "message": "Breakpoint set at F:\\mcp\\TokenSaver\\src\\Program.cs:42"
}
```

**Behavior:**
- File must exist and be accessible to the IDE
- `condition` and `hitCount` are optional (language/debugger dependent)
- `verified` indicates whether the debugger could set the breakpoint
- Returns breakpoint ID for later removal or query

### RemoveBreakpoint

Remove a breakpoint by ID.

**Input:**
```json
{
  "ideType": "VsCode",
  "breakpointId": "bp_deadbeef"
}
```

**Output:**
```json
{
  "success": true,
  "message": "Breakpoint bp_deadbeef removed"
}
```

### ListBreakpoints

List all breakpoints in a session.

**Input:**
```json
{
  "ideType": "VsCode",
  "sessionId": "a1b2c3d4e5f6"
}
```

**Output:**
```json
{
  "success": true,
  "breakpoint_count": 3,
  "breakpoints": [
    {
      "breakpoint_id": "bp_1",
      "file_path": "F:\\mcp\\TokenSaver\\src\\Program.cs",
      "line_number": 42,
      "verified": true,
      "condition": "x > 0",
      "hit_count": 5,
      "current_hit_count": 0
    }
  ]
}
```

### ExecuteStep

Execute a step command in the debugger.

**Input:**
```json
{
  "ideType": "VsCode",
  "command": "StepIn",
  "threadId": "thread_1"
}
```

**Output:**
```json
{
  "success": true,
  "command": "StepIn",
  "current_file": "F:\\mcp\\TokenSaver\\src\\Helper.cs",
  "current_line": 15,
  "reason": "step",
  "message": "StepIn executed"
}
```

**Behavior:**
- Commands: `StepIn`, `StepOver`, `StepOut`, `Continue`, `Pause`, `Restart`
- `StepIn` — step into function calls
- `StepOver` — step to next line, skipping function calls
- `StepOut` — step out of current function
- `Continue` — resume execution until next breakpoint
- `Pause` — pause execution at current position
- `Restart` — restart the debug session
- Optional `threadId` targets a specific thread (multi-threaded debugging)

### GetDebugState

Get the current debug state: call stack, variables, current position.

**Input:**
```json
{
  "ideType": "VsCode",
  "sessionId": "a1b2c3d4e5f6"
}
```

**Output:**
```json
{
  "success": true,
  "session_id": "a1b2c3d4e5f6",
  "is_running": false,
  "current_file": "F:\\mcp\\TokenSaver\\src\\Program.cs",
  "current_line": 42,
  "call_stack": [
    {
      "frame_id": "frame_0",
      "source": "F:\\mcp\\TokenSaver\\src\\Program.cs",
      "line": 42,
      "function": "Main",
      "index": 0
    },
    {
      "frame_id": "frame_1",
      "source": "F:\\mcp\\TokenSaver\\src\\Helper.cs",
      "line": 15,
      "function": "DoWork",
      "index": 1
    }
  ],
  "variables": [
    {
      "name": "x",
      "value": "42",
      "type": "int",
      "variables_reference": null
    },
    {
      "name": "config",
      "value": "{...}",
      "type": "TokenSaverConfig",
      "variables_reference": 1
    }
  ]
}
```

**Behavior:**
- Returns current call stack (most recent frame first)
- Returns local variables and their current values
- `variables_reference` non-null indicates a complex object that can be expanded
- `is_running` indicates whether execution is paused at a breakpoint

## Implementation Details

### Service: `IDebuggerControl` (TokenSaver.Common.Core)

Implemented by `DebuggerControlService` in `TokenSaver.MCP/Services`. It holds no state of its own: every call is
forwarded to the debugger of a running IDE window and reports what the IDE answered. With no matching IDE running, or
when the IDE refuses, the call fails with that reason (start/breakpoint results carry `Success`/`Verified: false` and the
message; the other calls throw, which the HTTP layer turns into an error response). It never reports success the IDE did
not give.

### IDE bridge

The service talks to the same per-window named-pipe bridges as the `ide_*` tools (`IdeInstanceRouter`, in the `SperryDev.Ide.Bridge` package). A Visual Studio
window serves `TokenSaver.VS.<pid>`, a VS Code window `TokenSaver.VSCode.<pid>`; `ideType` selects the kind
(`VsCode`, or any `VisualStudio…` meaning Visual Studio). One JSON line in (`{"action","args"}`), one out
(`{"ok","result"|"error"}`).

| Contract call | Bridge action | Notes |
|---|---|---|
| StartDebugSession | `startDebugging` (`configuration`) | Session id is `<ide>-<pid>` of the window that answered |
| StopDebugSession | `stopDebugging` | |
| SetBreakpoint | `setBreakpoint` (`file`, `line`) | Id is `file:line`. `condition` and `hitCount` are refused, not ignored |
| RemoveBreakpoint | `removeBreakpoint` | Parses the `file:line` id |
| ListBreakpoints | `listBreakpoints` | |
| ExecuteStep | `step` (`mode` and `kind`: into, over, out) | Continue: `startDebugging` in Visual Studio, `step` continue in VS Code. Pause and Restart are refused |
| GetDebugState | `debugState` | Run state and, in Visual Studio, the current function; no file/line, call stack or variables yet |

### Supported Debuggers

| IDE | Debugger | Launch Config | Notes |
|-----|----------|---------------|-------|
| VS Code | Debug Adapter | launch.json | Full support via vscode.debug API |
| Visual Studio 2026 | Visual Studio Debugger | launchSettings.json or .vscode/launch.json | Via DTE or Debug Protocol |
| Future: Rider | JetBrains Debugger | run config | Via IDE plugin API |

## Usage Example

**Scenario**: Claude is debugging a TokenSaver bug and wants to step through code interactively.

```
Claude: "I found the bug in Program.cs:42. Let me debug it interactively."

Claude (via MCP StartDebugSession):
  {
    "ideType": "VsCode",
    "configName": "launch"
  }

MCP → IDE Extension → VS Code debugger
  → Debug session started, program paused at first breakpoint
  
MCP Response:
  {
    "success": true,
    "session_id": "a1b2c3d4e5f6"
  }

Claude (via MCP SetBreakpoint):
  {
    "ideType": "VsCode",
    "filePath": "F:\\mcp\\TokenSaver\\src\\Program.cs",
    "lineNumber": 42,
    "condition": "x > 0"
  }

MCP Response:
  {
    "success": true,
    "breakpoint_id": "bp_1",
    "verified": true
  }

Claude (via MCP ExecuteStep):
  {
    "ideType": "VsCode",
    "command": "Continue"
  }

MCP → VS Code debugger → Continue execution
  → Program pauses at breakpoint on line 42 (condition met)

MCP Response:
  {
    "success": true,
    "current_file": "F:\\mcp\\TokenSaver\\src\\Program.cs",
    "current_line": 42
  }

Claude (via MCP GetDebugState):
  {
    "ideType": "VsCode"
  }

MCP Response:
  {
    "is_running": false,
    "current_line": 42,
    "variables": [
      {"name": "x", "value": "42", "type": "int"},
      {"name": "result", "value": null, "type": "object"}
    ]
  }

Claude: "The program is paused at line 42 with x=42. This matches the condition. 
Let me step into this function to see what's happening."
```

## Error Handling

| Error Case | Behavior |
|-----------|----------|
| Debug config doesn't exist | StartDebugSession returns error message |
| IDE not running | StartDebugSession returns `Success: false` with the reason; the other calls throw it |
| File doesn't exist | SetBreakpoint returns error |
| Invalid step command | ExecuteStep returns error with valid commands list |
| Session doesn't exist | GetDebugState returns is_running: false |

## Future Extensions

- **Conditional breakpoints**: Advanced condition evaluation
- **Log points**: Print values without pausing
- **Data breakpoints**: Break on variable value changes
- **Watch expressions**: Custom variable watches
- **Multi-threaded debugging**: Thread-specific breakpoints and steps
- **Remote debugging**: Debug programs on different machines
- **Memory inspection**: Read/write memory directly
- **Exception handling**: Catch/break on specific exceptions
- **Breakpoint persistence**: Save breakpoints between sessions

## Revision History

| Date | Version | Changes |
|------|---------|---------|
| 2026-07-11 | 1.0 | Initial contract: start/stop sessions, breakpoints, step, state |

