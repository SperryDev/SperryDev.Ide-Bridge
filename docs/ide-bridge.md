# IDE bridge (Visual Studio and VS Code)

The MCP server drives open IDE windows through a common set of `Ide*` tools. The same call works in
Visual Studio and VS Code. The reply's `ide` field says which IDE handled it.

## How it works

- Each IDE window runs a bridge on a named pipe, one pipe per window process:
  - Visual Studio: `InstanceRegistry.PipeNameFor(pid)` in the `SperryDev.Ide.Bridge` package (source: SperryDev/SperryDev.Ide-Bridge). The VS extension hosts it.
  - VS Code: `\\.\pipe\TokenSaver.VSCode.<pid>` (`vscode/src/ideBridge.ts`).
- Each window writes a manifest to `%ProgramData%\TokenSaver\vs-instances\<pid>.json`:
  `{ pid, pipeName, solutionPath, updatedUtc, ide }`. `ide` is `VisualStudio` or `VsCode`.
- The MCP server reads the manifests (`VsInstanceRegistry`, in the package) and routes each call (`IdeInstanceRouter`, in the package) to the window whose
  `solutionPath` matches. With one instance open, the solution is inferred.
- Protocol: one JSON line per request, `{"action":"...","args":{...}}`. The reply is one JSON line,
  `{"ok":true,"pid":N,"result":...}` or `{"ok":false,"pid":N,"error":"..."}`. The VS Code bridge adds
  `"unsupported":true` for actions it can't do. The VS bridge never sends that field.
- Watchdog: VS Code's bridge checks every 10 seconds and restarts its pipe and manifest if either is
  missing. The VS bridge uses the same pattern in `TokenSaverPackage`.
- `health` is answered by each bridge directly. It reports the listener state, request count, and pipe name.

## Tools

| Tool | Action | Notes |
|---|---|---|
| `IdeListInstances` | (MCP only) | Lists manifests and their solutions. |
| `IdeBridgeHealth` | `health` | Listener state and request counters. |
| `IdeStartDebugging` / `IdeStopDebugging` | `startDebugging` / `stopDebugging` | VS Code uses the selected or first launch configuration. |
| `IdeSetConfiguration` / `IdeListConfigurations` | `setConfiguration` / `listConfigurations` | VS Code keeps the selected name for `startDebugging`. |
| `IdeSetStartupProject` | `setStartupProject` | VS only. VS Code returns `unsupported`. |
| `IdeRunTests` | `runTests` | VS: `dotnet test` with TRX when `IdeTestRunMode` is `DotnetTest`, otherwise Test Explorer. VS Code: `testing.runAll`. |
| `IdeGetTestResults` | (MCP only) | Reads the newest TRX file in the solution's `TestResults` folder. |
| `IdeBuild`, `IdeClean`, `IdePublish` | `build`, `clean`, `publish` | VS: the IDE build. VS Code: `build` runs the build task; `clean` and `publish` run `dotnet` in the workspace folder. |
| `IdeGetBuildDiagnostics` | `getBuildDiagnostics` | Errors and warnings from the Error List (VS) or diagnostics (VS Code). |
| `IdeCodeCleanup` / `IdeFormatDocument` | `codeCleanup` / `formatDocument` | |
| `IdeGetOutput`, `IdeSearchOutput`, `IdeClearOutput` | `getOutput`, `searchOutput`, `clearOutput` | VS: the named Output pane. VS Code: only the lines this bridge recorded (see Limits). |
| `IdeSetBreakpoint`, `IdeRemoveBreakpoint`, `IdeListBreakpoints` | same names | |
| `IdeStep`, `IdeGetDebugState`, `IdeEvaluate`, `IdeGetLocals` | `step`, `debugState`, `evaluate`, `getLocals` | Locals and evaluate need a paused debugger. |
| `IdeAddWatch`, `IdeRemoveWatch`, `IdeListWatches` | `addWatch`, `removeWatch`, `listWatches` | Watches are kept by the bridge. Values show only while paused. |
| `IdeKillProcess` | `killProcess` | Kills only processes this bridge started. VS starts none, so it refuses every pid. VS Code kills its clean/publish processes. |
| `IdeLaunchInstance` / `IdeKillInstance` | (MCP lifecycle) | Launch or save-and-kill a VS instance. Kill refuses if any document is unsaved. |
| `TerminalSession` | `open`, `tab`, `exec`, `list`, `close` | Runs commands in visible Windows Terminal sessions or headless service-account sessions. See [terminal-session-contract.md](terminal-session-contract.md). |

## Limits

- **VS Code output:** VS Code's API can't read other output channels. `getOutput`, `searchOutput`, and
  `clearOutput` cover only the lines this bridge recorded from its own clean and publish runs.
- **VS Code watches:** no API lists them. The bridge evaluates its own list in the active session.
- **VS Code startup project:** no equivalent. Use the launch configuration (`setConfiguration`).
- **VS Test Explorer:** its results aren't exposed to extensions. Use `dotnet test` with TRX mode.
- **VS killProcess:** refused for every pid. Use `IdeKillInstance` to close a VS instance.

## Settings

- `IdeTestRunMode` in the MCP `appsettings.json` (`TokenSaver` section): `DotnetTest` or `TestExplorer`
  (default). The VS options page's "IDE test runs use dotnet test with TRX" checkbox writes it.
- The VS options page also edits the MCP advanced settings: verbose logging, context helper and token
  economy switches, context size limit, endpoint strategy, and endpoints. Endpoints are one per line as
  `name|url|primary|models`, and at least one must have `primary` set to `true` when the list isn't empty.

## Build and install

Visual Studio (from the TokenSaver folder):

```bash
pwsh -File scripts/update-vs-extension.ps1
```

This rebuilds the VSIX and opens VsixInstaller for each VS 2026 instance. Expect a UAC prompt and a
dialog per instance. Commit first: the version comes from git height, so an uncommitted rebuild can
install the same version as before and do nothing.

VS Code bridge source (from this repo's `vscode/` folder):

```bash
npm run build
npx vsce package --out tokensaver.vsix --no-dependencies
code --install-extension tokensaver.vsix --force
```

## Live checks

1. Stop the installed `TokenSaverMCP` service and start the dev server:
   `pwsh -File scripts/run-dev-server.ps1` (needs UAC).
2. VS Code: open a folder with a `.sln` or `.csproj`. The manifest appears in `vs-instances` once the
   extension activates. Send `{"action":"health"}` to its pipe and check `listener_running`.
3. Send `clean`, `searchOutput`, `killProcess` (with an untracked pid, which must be refused), and
   `addWatch` plus `listWatches`.
4. Visual Studio: install the VSIX, reload, and check the manifest, `health`, `runTests`, and
   `killProcess` (refused).
