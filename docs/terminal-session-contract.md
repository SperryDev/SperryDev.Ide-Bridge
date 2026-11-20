# Terminal session tool contract

MCP tool: `TerminalSession` (`src/SperryDev.Ide.Bridge.Mcp/TerminalSessionTools.cs`).

## Parameters

| Name | Used by | Meaning |
| --- | --- | --- |
| `action` | all | `open`, `tab`, `exec`, `list`, `close`. Default `list`. |
| `launchMode` | `open` | `InteractiveUser` (default) or `ServiceAccount`. Case-insensitive. |
| `sessionId` | `tab`, `exec`, `close` | Session returned by `open`. |
| `tabId` | `exec` | Tab returned by `open` or `tab`. |
| `tabName` | `open`, `tab` | Tab title. Default `main`. |
| `command` | `exec` | PowerShell command text. |
| `timeoutSeconds` | `exec` | Default 60. |

## Launch modes

- **InteractiveUser**: the terminal is visible on the logged-on user's desktop. When the bridge runs as
  LocalSystem it starts `wt.exe` in that user's session (`WTSQueryUserToken` + `CreateProcessAsUser`) and grants
  only that user's SID read/write on the session pipes. When TokenSaver runs as a normal user, it starts `wt.exe`
  directly, as before.
- **ServiceAccount**: no window. Tabs run the wrapper under `pwsh` with the bridge's own account, used only through
  the pipes.

## Errors

Validation errors and runtime failures return text starting with `Error:`. The tool never throws for those cases.

## Limits

- The LocalSystem interactive path is not verified in tests (it needs the service running as SYSTEM).
- `ServiceAccount` tabs have no window; output comes only through the wrapper's pipes.
