# Windows Terminal standard session

Branch: `feat/wt-standard-session` (from `wip`). Scope: standard session only. Elevated stays a stub until a pipe integrity test passes.

## Decisions
- Output: a wrapper script streams stdout and stderr into the existing named pipes.
- Visibility: a real wt window the user can see.

## Units
- [x] U0. Launcher seam: `ITerminalProcessLauncher` in Contracts, `ProcessTerminalLauncher` default, `RecordingTerminalLauncher` in tests.
- [x] U1. Tests (red): compile failure recorded before U0 existed (`ITerminalProcessLauncher` not found). Green after U0–U3.
- [x] U2. `InitializeAsync` launches `wt.exe -w tokensaver-<session>`, stores `_process`, sets Ready. Missing wt throws, state Terminated. Tests: 3 new, all pass.
- [x] U3. `CreateTabAsync` runs `wt.exe -w <window> new-tab --title ... pwsh -File <wrapper> -StdoutPipe ... -StderrPipe ...`. Tabs tracked in `_tabs`. Launch failure closes both pipes.
- Known gap at this checkpoint: `tokensaver-tab.ps1` (U4) does not exist yet, so new tabs will show a missing-file error.
- [x] U4. `Scripts/tokensaver-tab.ps1` connects to stdout and stderr as a client, creates the command pipe as a server, runs each `<id> <command>` line, and writes `__TOKENSAVER_END__ <id> <exitcode>`. Protocol unit tests: `TabProtocolTests` (4). Red run recorded before `TabProtocol` existed.
- [x] U5. `ExecuteInTabAsync` connects to the command pipe (retry to 10 s), writes the command, reads stdout until its own end marker, honors timeout and cancellation. A timed-out request stays queued so its late output is discarded. End-to-end test `StandardTerminalSessionE2ETests` runs the real wrapper under `pwsh` directly and passes.
- [x] U6. `CloseTabAsync` sends the close command, so the wrapper exits and the wt tab closes. `ListTabsAsync`, `GetTabAsync`, and `IsAliveAsync` use the tab dictionary. `-NoExit` removed so tabs close when the wrapper exits.
- [x] U7. Stderr fixed: errors go in-band on the stdout pipe as `__TOKENSAVER_ERR__ <id> <text>`, so they stay in order with the end marker. The stderr pipe is no longer read.
- [x] U8. Command pipe direction reversed: TokenSaver is the server (`NamedCommandPipe`, PipeDirection.Out) and the wrapper is the client. Elevated processes can't write to a pipe created at a lower integrity level, so this direction is the safer one for the elevated path. Tests: `NamedCommandPipeTests` (5), red run recorded.
- [x] U9. Real Windows Terminal smoke test re-run after the protocol change: passes.
- [x] Host packaging: `TokenSaver.MCP` references `TokenSaver.TerminalControl`, and `Scripts/tokensaver-tab.ps1` is copied to the host output (`build/staging/TokenSaver.MCP/.../Scripts`). Host builds with 0 errors.
- Elevated path: not started. Chosen design: one UAC prompt, one elevated wt window; tabs opened from the non-elevated side.
- Blocker (probe needed): it is unverified that a non-elevated `wt -w <name> new-tab` lands in an elevated window with that name, and that the elevated wrapper can use the pipes. The agent's shells here are not reliably one or the other, so the probe must run from a clearly non-elevated terminal.
  - Probe steps: (1) open Windows Terminal as administrator and run `wt -w elevprobe`; (2) from a normal, non-admin PowerShell run `wt -w elevprobe new-tab pwsh -NoExit -Command "whoami /groups | Select-String Mandatory"`; (3) note whether the new tab shows "High Mandatory Level" and appears in the elevated window.
- [x] U10. Build and run TerminalControl tests: 84/84 pass (`dotnet test src\Core\TokenSaver.TerminalControl.Tests`).
- [x] Review fixes: removed unused `NamedPipeManager.MonitorPipeOutputAsync` (no callers, wrong parameter type); documented `CloseTabAsync` limit (wt tab closes only when the wrapper exits). Already in place: `_process` not `readonly`, fake launcher injected in `TerminalSessionPipeIntegrationTests`, connect timeout (`CommandConnectTimeout`).
- [x] NamedCommandPipe tests: one client accepted and a second rejected (`CommandPipe_RejectsSecondClient`); no reconnect after disconnect (`CommandPipe_DoesNotAcceptReconnectAfterClientDisconnects`). TerminalControl tests: 85/85 pass.
- [ ] Elevated `runas` path (commit df257c2) is kept per user decision; it is ahead of the blocker probe. Probe still needed before merge.
  - Probe run (non-admin shell, `wt -w elevprobe` admin window): the new tab ran at **Medium** integrity, not High. A non-elevated `wt -w <name> new-tab` does not produce an elevated tab, so the one-elevated-window design fails as written. The `runas` path is the remaining option. Admin-side step (1) was done by the user; the window name `elevprobe` may not have matched an elevated window.

## Session accounts (`feat/wt-session-accounts`, commit cf796c9)
- [x] `TerminalLaunchMode` on `TerminalSessionConfig`: `InteractiveUser` (default) and `ServiceAccount` (headless).
- [x] `ServiceAccount`: no window; tabs run `tokensaver-tab.ps1` under `pwsh` directly, used only through the pipes.
- [x] `InteractiveUser` under LocalSystem: `ConsoleUserTerminalLauncher` starts `wt.exe` in the logged-on user's session (`WTSQueryUserToken` + `CreateProcessAsUser`).
- [x] Pipe ACL (`PipeSecurityFactory`): LocalSystem and Administrators full control, the client SID read/write, nobody else. Default ACL kept when no client SID is given.
- [x] Tests: headless mode opens no window; pipe ACL grants only the intended SIDs. TerminalControl tests: 88/88 pass.
- [x] Exposed through the MCP `TerminalSession` tool (`open` with `launchMode`, `tab`, `exec`, `list`, `close`). Contract: `docs/terminal-session-contract.md`. Host tests: 9/9 for the tool; the manager initializes on first use, not at startup.
- [x] Shared token and session code: new `TokenSaver.WindowsSession` project (`UserSession`, `UserToken`). WslControl and TerminalControl use it instead of two copies of the same Win32 declarations. Each caller keeps its own session choice and process launch.
  - Behavior change: in WslControl, a failed session binding (`SetTokenInformation`) now throws. It was ignored before.
- [x] `TokenSaver.WindowsSession` added to `TokenSaver.slnx`; full solution builds with 0 errors.
- [ ] Not verified: the LocalSystem interactive launch (needs the service running as SYSTEM).
- [ ] Not verified: `InteractiveUser` under LocalSystem through `TerminalSession`; the tool's tests cover validation only.

## Risks
- Pipe connection order: server pipes must listen before the wrapper connects.
- The elevated path is out of scope here.

## Review (Haiku, read-only; no Sonnet approval doc found in mcp repo)
- U0 (new): add `ITerminalProcessLauncher` seam with `Process? Start(ProcessStartInfo)`; default impl calls `Process.Start`. Needed before U1.
- U2: `_process` is `readonly ... = null`; drop `readonly`. `wt.exe -w` can exit while its window stays alive, so do not rely on `HasExited` for liveness. Track the launcher's process only as a best-effort handle.
- U3: CreateTabAsync must launch the tab and keep a tab dictionary. Clean up pipes if the launch fails.
- U5: start the pipe reader before sending the command. `MonitorPipeOutputAsync` has no caller and a wrong parameter type (NamedPipeManager.cs:123-125). Add a connect timeout to `WaitForConnectionAsync` (NamedPipeManager.cs:234).
- U6: wt has no external close-tab command. CloseTabAsync removes pipes and sends the wrapper an exit request; it does not close the tab. Document this limit.
- New unit: NamedPipeManager tests (MaxInstances 1 rejects a second client; no reconnect after disconnect).
- Existing tests in `TerminalSessionPipeIntegrationTests` call `InitializeAsync` and would now launch real wt. Inject the fake launcher there.

## Open questions
- None blocking.
