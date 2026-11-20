# Remaining work (SperryDev.Ide-Bridge), 2026-11-19

Status as of 2026-11-19. Source: the approved split plan (mcp docs/plans/ide-bridge-split.md) and the current branch state.

## Done

- [x] Package SperryDev.Ide.Bridge: moved types, tests, solution, .gitignore, IBridgeLogger, shared manifest record (Ide and LogRoots required), net48 and net10.0-windows targets. 15 tests pass.
- [x] Published to GitHub Packages as 0.1.0 (sperry-github feed); restore verified.
- [x] Re-sync merged into dev through PR #1 (dev at 8f060ab).
- [x] Repo public; branch protection on dev (PRs required, no force push, no deletion; 0 approvals).

## Moved from the mcp and TokenSaver plans

- [x] TokenSaver consumer switch and bridge removal merged into local TokenSaver `wip`; terminal stack removal is at `c33734d`, and the parent `mcp` pointer is at `1ff63d9`.
- [x] The bridge's local `wip` had unpushed commits beyond `origin/dev` and no upstream after PR #4 deleted the remote branch. Published them on `codex/ide-bridge-stage9` and prepared a PR after the release request; merges into `dev` still require approval.
- [x] Stale stash vs-services-bridge-wip: already gone. Nothing to do.
- [x] docs/ide-bridge.md: updated the VS Code source path and confirmed `unsupported` is only emitted as `true`.
- [x] Skip TokenSaver `docs/folder-index.md` regeneration; `code_index` is intentionally not being used.
- [ ] Verify end to end: builds and test suites in both repos; MCP IdeBridgeHealth and IdeListInstances against a live VS instance. Both builds and test suites pass (see stage 8); live MCP and IDE checks remain.
- [x] Keep SperryDev.Ide-Bridge and SperryDev.Docs as separate repositories, not mcp submodules.
- [x] Close out the worktree copy `F:\source\mcp-wt-root\docs\plans\ide-bridge-split.md`: the `F:\source\mcp-wt-root` worktree no longer exists.

## VS 2026 and VS Code bridge code (TokenSaver copies removed in stage 7)

- [x] Extracted the VS 2026 bridge package wiring, IdeActions, and Services page bridge controls into `src/SperryDev.Ide.Bridge.Vsix`. The TokenSaver copy was removed in stage 7.
- [x] VS Code bridge extracted under `vscode/` with its unit tests. All shared fixtures pass on the TypeScript side; the TokenSaver copy was removed in stage 7.
- [x] `src/SperryDev.Ide.Bridge.Vsix/` belongs here and is wired into the solution.

## Migration: everything IDE-bridge-related moves here

Decisions so far: the bridge has its own MCP server and its own LogHelper; TokenSaver has no LogHelper or LogViewer; the VS Code and Visual Studio
bridges are separate implementations in this repo; TokenSaver keeps no IDE bridge code.

Stages (implementation is complete through stage 9; stage 8 verification remains pending):
1. [x] Add src/SperryDev.Ide.Bridge.Mcp (ModelContextProtocol host) and reference the bridge package.
2. [x] Copy the generic LogHelper core into this repo for the Bridge host (196 tests passing). Stage 7 removes LogHelper and LogViewer completely from TokenSaver.
3. [x] Complete (commit 0df42ff plus local wiring): moved the IDE tools and services into the bridge MCP, registered IdeTools with the server, and moved their IDE tool, debugger control, test-run, debug-context, and IDE log-source tests.
4. [x] Extract the VS Code bridge (ideBridge.ts, ideActions, outputBuffer, debugOutputTracker, debugOutputStore, logRoots) under `vscode/` as its own implementation. The TokenSaver copy was removed in stage 7; it is not shared with the Visual Studio bridge.
5. [x] Extract the Visual Studio host (package wiring, IdeActions, and bridge controls) under the VSIX project. The TokenSaver copy was removed in stage 7.
6. [x] Move the three Host-MCP contracts (labeled historical with links to current docs), extract the IDE bridge self-test to `docs/self_test.md`, and add standalone VSIX build/update scripts under `scripts/`. The old TokenSaver scripts are retained because they package the combined TokenSaver extension and contain no bridge-only sections; no bridge-only script section could be moved out of them.
7. [x] Remove the extracted Bridge MCP tools/control services and tests, old IDE control HTTP endpoints/contracts, IDE-backed log providers, VS Code bridge files, duplicate Visual Studio host/controls, VSCode debugger adapter project, package references, solution entries, and the `IdeTestRunMode` setting from TokenSaver. Remove LogHelper and LogViewer completely, including their MCP tools, projects, configuration, skills, and contracts. TerminalControl stayed until stage 9 moved it; TokenSaver now has no IDE bridge or terminal session tools. Folder-index regeneration remains pending because the TokenSaver `code_index` MCP tool is unavailable here.
8. [ ] Build and test: both repos green; the bridge MCP starts; TokenSaver MCP starts without bridge tools. Both builds and suites pass: Ide-Bridge full solution build succeeded with Visual Studio 18 MSBuild and its three test projects passed (196 LogHelper, 120 bridge MCP, 88 TerminalControl); TokenSaver full solution build succeeded with zero warnings/errors and its suite passed (967 tests). Live MCP startup and live IDE checks remain outstanding.
9. [x] Move TerminalControl, WindowsSession support, terminal MCP tool and tests, contract, and wt standard-session plan into this repo. Remove the terminal stack from TokenSaver while leaving its separate WindowsSession utility for WslControl. Verification remains pending.

## Open

- [x] PR #2 (`feature/ide-bridge-ci` into `dev`) was merged as `748bf19`, then reverted by PR #3 (`fa5c7f1`) because the merge lacked a release request. PR #4 later merged the earlier `wip` snapshot as `7f86d5d`; the remote `wip` branch was deleted.
- [ ] Reintroduced CI in this release-requested PR; it builds with Visual Studio MSBuild for the VSIX. Enable build-and-test as a required check after the workflow passes.
- [ ] Publish on a v* tag: confirm the publish job pushes to the SperryDev feed with GITHUB_TOKEN (not tested).
- [x] Add a README that links to docs/plans, the local protocol doc, and the TokenSaver guide (docs/ide-bridge.md stays there).
- [x] Investigate the reported stale extensionDir: this project is already an in-process VSSDK/net48 VSIX. Its source manifest has no `extensionDir`; the current package metadata uses `kklsjkso.obx`, and the reported `j30o0obs.15i` path is absent from this profile's per-user extension directory. An in-process port does not apply to this project.
- [ ] Verify the extension in VS 18 Manage Extensions and run “Show IDE Bridge Status”; the install/load state is still unconfirmed.
- [ ] Verify end to end: MCP IdeBridgeHealth and IdeListInstances against a live Visual Studio instance.
- [x] Keep this repo separate from mcp; do not add it as a submodule.
