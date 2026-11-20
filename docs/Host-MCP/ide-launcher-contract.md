> **Historical contract:** This page describes the former combined TokenSaver MCP surface and may not match the current bridge MCP tools. For current behavior, use [the IDE bridge guide](../ide-bridge.md) and [the bridge protocol](../../conformance/protocol.md).
# IDE Launcher Contract

**Status**: Public MCP tool surface  
**Version**: 1.0  
**Last Updated**: 2026-07-11

## Overview

The IDE Launcher contract defines how the MCP server (`TokenSaver.MCP`) launches and controls IDE instances (Visual Studio Code, Visual Studio, future IDEs) on the user's machine. This enables the MCP to orchestrate the development environment, opening the right IDE for a workspace and querying/managing running instances.

## Public Interface

Three MCP tools expose the launcher:

### LaunchIde

Launch an IDE instance with a specified workspace root.

**Input:**
```json
{
  "ideType": "VsCode" | "VisualStudio2026",
  "workspaceRoot": "/path/to/workspace",
  "waitForReady": true
}
```

**Output:**
```json
{
  "success": true,
  "ide_type": "VsCode",
  "process_id": 1234,
  "port": null,
  "ready": true,
  "message": "Visual Studio Code launched successfully",
  "metadata": {
    "workspaceRoot": "/path/to/workspace",
    "launchedAt": "2026-07-11T10:30:00Z",
    "alreadyRunning": false
  }
}
```

**Behavior:**
- If the workspace root does not exist, returns an error immediately.
- If the IDE is already running, returns its existing process info (alreadyRunning: true) without launching again.
- `waitForReady`: if true, waits ~500ms for the IDE to initialize before returning.
- Returns the process ID and optional port (future HTTP-based IDEs may expose a port).

### QueryIDEStatus

Query the status of a running IDE instance.

**Input:**
```json
{
  "ideType": "VsCode" | "VisualStudio2026"
}
```

**Output:**
```json
{
  "ide_type": "VsCode",
  "is_running": true,
  "process_id": 1234,
  "port": null,
  "ready": true,
  "workspace_root": "/path/to/workspace"
}
```

**Behavior:**
- Returns `is_running: false` if the IDE is not currently running or the process has exited.
- Cleans up stale process records automatically.

### StopIDE

Stop a running IDE instance.

**Input:**
```json
{
  "ideType": "VsCode" | "VisualStudio2026",
  "graceful": true
}
```

**Output:**
```json
{
  "success": true,
  "message": "VsCode stop requested"
}
```

**Behavior:**
- `graceful: true` (default): requests a graceful shutdown, waiting up to 5 seconds for the process to exit, then force-kills if needed.
- `graceful: false`: force-kills the process immediately.
- No-op if the IDE is not running (returns success nonetheless).

## Implementation Details

### Service: `IIDELauncher` (TokenSaver.Common.Core)

The interface lives in `TokenSaver.Common.Core.Contracts` and is implemented by `IDELauncherService` in `TokenSaver.Library.Services`.

**Responsibilities:**
- Process lifecycle management (launch, query, stop).
- Per-IDE-type instance tracking.
- Graceful vs. force shutdown handling.
- Platform-specific IDE discovery (e.g., `devenv.exe` search paths on Windows).

### Supported IDEs

| IDE | Launch Method | Discovery | Future Features |
|-----|---------------|-----------|-----------------|
| Visual Studio Code | `code "<workspace>"` | Requires `code` CLI on PATH | HTTP-based port detection, extension communication |
| Visual Studio 2026 | `devenv.exe "<sln>"` | Auto-search Professional/Enterprise/Community editions; first .sln in workspace | HTTP-based IdeHub protocol, live project file sync |

New IDEs are added by extending the `LaunchAsync` switch statement in `IDELauncherService.CreateXxxStartInfo()` and registering a new path-to-devenv in the discovery logic.

### Port Detection

Currently returns `null` for all IDEs. Future versions (when VS Code extension or Visual Studio exposes an HTTP port) will:
- Probe the process for open ports via Windows API or netstat parsing.
- Return the port so MCP clients can communicate directly with the IDE's extension API.

### Thread Safety

All instance tracking is protected by a lock. Concurrent `Launch` calls for the same IDE type are safe; the second caller gets back the existing process info.

## Usage Example

**Scenario**: Claude is working on a bug in the TokenSaver codebase and wants to open VS Code.

```
User: Open VS Code at F:\mcp\TokenSaver

Claude (via MCP LaunchIde tool):
  {
    "ideType": "VsCode",
    "workspaceRoot": "F:\\mcp\\TokenSaver",
    "waitForReady": true
  }

MCP Response:
  {
    "success": true,
    "ide_type": "VsCode",
    "process_id": 5678,
    "port": null,
    "ready": true,
    "message": "Visual Studio Code launched successfully",
    "metadata": {
      "workspaceRoot": "F:\\mcp\\TokenSaver",
      "launchedAt": "2026-07-11T10:30:00Z"
    }
  }

Claude: "VS Code is now open at F:\mcp\TokenSaver (PID 5678). I can now write code and test it interactively."
```

## Error Handling

| Error Case | Behavior |
|-----------|----------|
| Workspace root does not exist | Returns error immediately with clear message |
| IDE binary not found (e.g., `code` not on PATH) | Returns error; user must install IDE or add to PATH |
| Process launch fails (permission denied, etc.) | Returns error with underlying exception details |
| IDE already running (same type) | Returns success with alreadyRunning: true + existing process info |
| Stop on non-running IDE | Returns success (no-op) |

## Future Extensions

- **Named Pipe Discovery**: IDEs broadcast their presence/capabilities to the MCP via named pipes (e.g., `\\.\pipe\TokenSaver\VsCode` or `\\.\pipe\TokenSaver\VisualStudio2026`), allowing the MCP to discover which IDEs are available without hardcoded paths.
- **Multi-IDE Orchestration**: One MCP instance coordinates across multiple open IDEs (e.g., VS Code for TypeScript frontend, VS 2026 for C# backend).
- **IDE-to-MCP Callbacks**: IDE extensions notify the MCP of file changes, build events, test results, etc., enabling real-time context updates.
- **Debug Session Control**: LaunchIde extended to start debuggers, breakpoints, and step commands.

## Revision History

| Date | Version | Changes |
|------|---------|---------|
| 2026-07-11 | 1.0 | Initial contract: LaunchIde, QueryIDEStatus, StopIDE for VS Code and VS 2026 |

