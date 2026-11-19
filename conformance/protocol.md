# IDE bridge protocol (conformance)

Every IDE bridge follows these rules. The C# bridge (`SperryDev.Ide.Bridge`) and the VS Code bridge
(`vscode/src/ideBridge.ts`) both must pass the fixtures in `fixtures.json`.

## Pipe

- Name: `TokenSaver.VS.<pid>` for Visual Studio, `TokenSaver.VSCode.<pid>` for VS Code. `<pid>` is the
  process that hosts the bridge.
- Local named pipe on `\\.\pipe\<name>`.

## Request and reply

- One JSON line per request: `{"action":"...","args":{...}}`.
- One JSON line per reply:
  - success: `{"ok":true,"pid":N,"result":...}`
  - failure: `{"ok":false,"pid":N,"error":"..."}`
- `unsupported` is optional. Only bridges that can refuse an action (VS Code) may add `"unsupported":true`.
  Readers must accept replies with or without it.
- A malformed request line gets a failure reply, never a dropped connection.

## health

- `{"action":"health"}` is answered by the bridge itself, not by the action handler.
- The `result` object has `listener_running` (bool), `pipe_name`, `started_utc`, `last_request_utc`,
  `requests_handled`, and `solution_path` (string or null).

## Manifest

- Path: `%ProgramData%\TokenSaver\vs-instances\<pid>.json`.
- Fields: `pid`, `pipeName`, `solutionPath` (string or null), `updatedUtc`, `ide`, `logRoots`.
- `ide` is `VisualStudio` or `VsCode`. `logRoots` is an array (empty for Visual Studio).
- Written by the bridge on start and rewritten by the watchdog. Removed on dispose.

## Watchdog

- Checks every 10 seconds. Restarts the pipe if the listener stopped, and rewrites the manifest if it is missing.
