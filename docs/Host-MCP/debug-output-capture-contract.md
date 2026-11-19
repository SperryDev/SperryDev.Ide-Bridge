> **Historical contract:** This page describes the former combined TokenSaver MCP surface and may not match the current bridge MCP tools. For current behavior, use [the IDE bridge guide](../ide-bridge.md) and [the bridge protocol](../../conformance/protocol.md).
# Debug Output Capture Contract

**Status**: Public MCP tool surface  
**Version**: 1.0  
**Last Updated**: 2026-07-11

## Overview

The Debug Output Capture contract defines how the MCP server captures and exposes stdout/stderr from launched processes and IDE instances. This enables calling AI to read stack traces, build errors, compilation output, and other diagnostic information in real time.

## Public Interface

Three MCP tools expose the capture service:

### GetDebugOutput

Retrieve captured stdout/stderr from a launched process or IDE.

**Input:**
```json
{
  "processId": 5678,
  "ideType": "VsCode",
  "linesLimit": 1000
}
```

**Output:**
```json
{
  "success": true,
  "process_id": 5678,
  "ide_type": "VsCode",
  "captured_at": "2026-07-11T10:30:00Z",
  "line_count": 142,
  "lines": [
    {
      "line_number": 1,
      "timestamp": "10:30:00.123",
      "severity": "Info",
      "stream": "stdout",
      "content": "Extension activated"
    },
    {
      "line_number": 2,
      "timestamp": "10:30:01.456",
      "severity": "Error",
      "stream": "stderr",
      "content": "Failed to connect to MCP: ECONNREFUSED"
    }
  ]
}
```

**Behavior:**
- Query by `processId` or `ideType` (one is required).
- Returns up to `linesLimit` most recent lines (default 1000, max 5000).
- Lines include timestamps, severity (Info/Warning/Error/Fatal), and which stream (stdout/stderr).
- Severity is automatically inferred from content keywords (e.g., "error", "fatal", "exception").

### SearchDebugOutput

Search captured output for stack traces, errors, or other patterns.

**Input:**
```json
{
  "pattern": "exception|stack trace",
  "processId": 5678,
  "severity": "Error"
}
```

**Output:**
```json
{
  "success": true,
  "pattern": "exception|stack trace",
  "match_count": 3,
  "matches": [
    {
      "line_number": 45,
      "timestamp": "10:30:15.789",
      "severity": "Error",
      "stream": "stderr",
      "content": "System.InvalidOperationException: The operation completed unsuccessfully"
    }
  ]
}
```

**Behavior:**
- `pattern` can be a regex or plain substring (auto-detected; invalid regex treated as literal).
- Optional `severity` filter: "Info", "Warning", "Error", "Fatal".
- Returns all matching lines with context.
- Searches all processes if `processId` is not provided.

### ClearDebugOutput

Clear captured output buffer to free memory.

**Input:**
```json
{
  "processId": 5678
}
```

**Output:**
```json
{
  "success": true,
  "message": "Cleared debug output buffer for process 5678"
}
```

**Behavior:**
- Removes the in-memory buffer for a process.
- Safe to call multiple times (no-op if buffer doesn't exist).

## Implementation Details

### Service: `IDebugOutputCapture` (TokenSaver.Common.Core)

The interface lives in `TokenSaver.Common.Core.Contracts` and is implemented by `DebugOutputCaptureService` in `TokenSaver.Library.Services`.

**Responsibilities:**
- Capture stdout/stderr from processes.
- Store recent output with timestamps and severity levels.
- Manage buffer capacity (5000 lines / 10MB per process).
- Search across all or specific process buffers.
- Clean up old entries when capacity is exceeded.

### Severity Classification

Severity is inferred from line content:

| Keyword | Severity |
|---------|----------|
| "fatal", "crash" | Fatal |
| "error", "failed", "exception" | Error |
| "warn" | Warning |
| (default) | Info |

Custom classifications can be added to `DetermineSeverity()` in `DebugOutputCaptureService`.

### Buffer Capacity

- **Max lines**: 5000 per process/IDE
- **Max size**: 10MB per process/IDE
- **Behavior**: Oldest lines dropped first when capacity exceeded
- **Trim strategy**: FIFO (first-in, first-out)

### Integration Points

1. **LaunchIde** → Automatically starts capturing when an IDE is launched
2. **Subprocesses** → Can be wired up for any child process by calling `StartCapturing(process)`
3. **Background services** → Capture can be started from any service that owns a process

## Usage Example

**Scenario**: Claude launches VS Code, needs to debug why the extension isn't connecting to the MCP.

```
Claude (via MCP LaunchIde):
  {
    "ideType": "VsCode",
    "workspaceRoot": "F:\\mcp\\TokenSaver"
  }

MCP Response:
  {
    "process_id": 5678,
    "ready": true
  }

Claude (via MCP GetDebugOutput):
  {
    "ideType": "VsCode",
    "linesLimit": 100
  }

MCP Response:
  {
    "line_count": 12,
    "lines": [
      {"timestamp": "10:30:00.123", "severity": "Info", "content": "Extension activated"},
      {"timestamp": "10:30:01.456", "severity": "Error", "content": "Failed to connect to MCP: ECONNREFUSED"}
    ]
  }

Claude: "The extension can't reach the MCP. The error shows ECONNREFUSED on port 11436 — the MCP server isn't running yet. Let me start the dev server."
```

## Error Handling

| Error Case | Behavior |
|-----------|----------|
| Process not found | Returns empty output (success: true, line_count: 0) |
| Invalid regex pattern | Falls back to substring matching |
| Buffer capacity exceeded | Automatically trims oldest lines |
| Search finds no matches | Returns match_count: 0 with empty matches array |

## Future Extensions

- **Real-time streaming**: WebSocket or Server-Sent Events for live output as it happens
- **Output filtering**: Line-level filtering by source, module, or component
- **Log aggregation**: Merge output from multiple processes into a single searchable view
- **Telemetry**: Track which processes generate the most errors, latency metrics
- **Output redirection**: Automatic redirection of IDE stdout/stderr for capture (currently requires process launch with RedirectStandardOutput)
- **Persistence**: Store output to disk for post-mortem analysis

## Revision History

| Date | Version | Changes |
|------|---------|---------|
| 2026-07-11 | 1.0 | Initial contract: GetDebugOutput, SearchDebugOutput, ClearDebugOutput |

