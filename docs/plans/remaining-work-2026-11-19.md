# Remaining work (SperryDev.Ide-Bridge), 2026-11-19

Status as of 2026-11-19. Source: the approved split plan (mcp docs/plans/ide-bridge-split.md) and the current branch state.

## Done

- [x] Package SperryDev.Ide.Bridge: moved types, tests, solution, .gitignore, IBridgeLogger, shared manifest record (Ide and LogRoots required), net48 and net10.0-windows targets. 15 tests pass.
- [x] Published to GitHub Packages as 0.1.0 (sperry-github feed); restore verified.
- [x] Re-sync merged into dev through PR #1 (dev at 8f060ab).
- [x] Repo public; branch protection on dev (PRs required, no force push, no deletion; 0 approvals).

## Moved from the mcp and TokenSaver plans

- [x] TokenSaver consumer switch committed on feature/ide-bridge-consumer (8238748, 7ec53a2). Not pushed. Merge into local wip is done (c73d089, in TokenSaver).
- [ ] Merge feature/ide-bridge-consumer into TokenSaver wip and open a wip -> dev PR. Needs approval before any merge into dev.
- [x] Stale stash vs-services-bridge-wip: already gone. Nothing to do.
- [x] docs/ide-bridge.md: updated the VS Code source path and confirmed `unsupported` is only emitted as `true`.
- [ ] TokenSaver `docs/folder-index.md`: regenerate after the bridge code moves.
- [ ] Verify end to end: dotnet build and test in both repos; MCP IdeBridgeHealth and IdeListInstances against a live VS instance.
- [ ] Submodule decision: whether this repo and SperryDev.Docs become submodules of mcp. If yes, push the submodule first, then the pointer.
- [ ] Close out the worktree copy F:\source\mcp-wt-root\docs\plans\ide-bridge-split.md (superseded by this plan).

## VS 2026 and VS Code bridge code (TokenSaver copies removed in stage 7)

- [x] Extracted the VS 2026 bridge package wiring, IdeActions, and Services page bridge controls into `src/SperryDev.Ide.Bridge.Vsix`. The TokenSaver copy was removed in stage 7.
- [x] VS Code bridge extracted under `vscode/` with its unit tests. All shared fixtures pass on the TypeScript side; the TokenSaver copy was removed in stage 7.
- [x] `src/SperryDev.Ide.Bridge.Vsix/` belongs here and is wired into the solution.

## Migration: everything IDE-bridge-related moves here

Decisions so far: the bridge has its own MCP server and its own LogHelper; TokenSaver has no LogHelper or LogViewer; the VS Code and Visual Studio
bridges are separate implementations in this repo; TokenSaver keeps no IDE bridge code.

Stages (each builds and tests before the next):
1. [x] Add src/SperryDev.Ide.Bridge.Mcp (ModelContextProtocol host) and reference the bridge package.
2. [x] Copy the generic LogHelper core into this repo for the Bridge host (196 tests passing). Stage 7 removes LogHelper and LogViewer completely from TokenSaver.
3. [x] Complete (commit 0df42ff plus local wiring): moved the IDE tools and services into the bridge MCP, registered IdeTools with the server, and moved their IDE tool, debugger control, test-run, debug-context, and IDE log-source tests.
4. [x] Extract the VS Code bridge (ideBridge.ts, ideActions, outputBuffer, debugOutputTracker, debugOutputStore, logRoots) under `vscode/` as its own implementation. The TokenSaver copy was removed in stage 7; it is not shared with the Visual Studio bridge.
5. [x] Extract the Visual Studio host (package wiring, IdeActions, and bridge controls) under the VSIX project. The TokenSaver copy was removed in stage 7.
6. [x] Move the three Host-MCP contracts (labeled historical with links to current docs), extract the IDE bridge self-test to `docs/self_test.md`, and add standalone VSIX build/update scripts under `scripts/`. The old TokenSaver scripts are retained because they package the combined TokenSaver extension and contain no bridge-only sections; no bridge-only script section could be moved out of them.
7. [x] Remove the extracted Bridge MCP tools/control services and tests, old IDE control HTTP endpoints/contracts, IDE-backed log providers, VS Code bridge files, duplicate Visual Studio host/controls, VSCode debugger adapter project, package references, solution entries, and the `IdeTestRunMode` setting from TokenSaver. Remove LogHelper and LogViewer completely, including their MCP tools, projects, configuration, skills, and contracts. Keep TerminalControl and its current config until stage 9 moves it; `TerminalLaunchMode` still serves TokenSaver terminal tools. Folder-index regeneration remains pending because the TokenSaver `code_index` MCP tool is unavailable here.
8. [ ] Build and test: both repos green; the bridge MCP starts; TokenSaver MCP starts without bridge tools. Not run yet.
9. [x] Move TerminalControl, WindowsSession support, terminal MCP tool and tests, contract, and wt standard-session plan into this repo. Remove the terminal stack from TokenSaver while leaving its separate WindowsSession utility for WslControl. Verification remains pending.

## Open

- [ ] PR #2 (feature/ide-bridge-ci into dev): CI workflow and solution fix. Review, then merge on approval (merges into dev need a release request).
- [ ] CI has not run on GitHub yet. Once it runs, add build-and-test as a required check on dev.
- [ ] Publish on a v* tag: confirm the publish job pushes to the SperryDev feed with GITHUB_TOKEN (not tested).
- [ ] Add a README that links to docs/plans and the protocol doc in TokenSaver (docs/ide-bridge.md stays there).
- [ ] VS extension install is blocked by a stale extensionDir in the VSIX manifest (VSExtensions\j30o0obs.15i). Investigate the in-process VSSDK port as the fix; not started.
- [ ] Verify end to end: MCP IdeBridgeHealth and IdeListInstances against a live Visual Studio instance.
- [ ] Decide whether the repo becomes a submodule of mcp; if yes, push the submodule first, then the pointer.
