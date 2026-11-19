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
- [ ] docs/ide-bridge.md: now in this repo; the VS Code bridge section is current. Check the `unsupported` field against the VS Code bridge.
- [ ] TokenSaver `docs/folder-index.md`: regenerate after the bridge code moves.
- [ ] Verify end to end: dotnet build and test in both repos; MCP IdeBridgeHealth and IdeListInstances against a live VS instance.
- [ ] Submodule decision: whether this repo and SperryDev.Docs become submodules of mcp. If yes, push the submodule first, then the pointer.
- [ ] Close out the worktree copy F:\source\mcp-wt-root\docs\plans\ide-bridge-split.md (superseded by this plan).

## VS 2026 and VS Code bridge code (still in TokenSaver)

- [ ] VS 2026 extension: TokenSaverPackage.cs, IdeActions.cs, and the bridge section of ServicesViewModel.cs stay in TokenSaver. Decide whether they move here.
- [ ] VS Code bridge: src/Tooling/TokenSaver.VsCodeExt/src/ideBridge.ts stays in TokenSaver. Run the conformance/fixtures.json cases on the TypeScript side (needs a way to share the fixtures).
- [ ] src/SperryDev.Ide.Bridge.Vsix/ is untracked in the working tree. Decide whether it belongs here.

## Migration: everything IDE-bridge-related moves here

Decisions so far: the bridge has its own MCP server; LogHelper moves here; the VS Code and Visual Studio
bridges are separate implementations in this repo; TokenSaver keeps no bridge code.

Stages (each builds and tests before the next):
1. [ ] Add src/SperryDev.Ide.Bridge.Mcp (ModelContextProtocol host) and reference the bridge package.
2. [ ] Copy LogHelper pieces the Ide* log sources need (LogHelper.Models, Contracts, Parsing, Sources) into this repo.
3. [ ] Move IdeTools, IdeBridgeClient, IdeInstanceLifecycle, the IDE and VS log sources, DebuggerControlService,
       ClientDebugContextService and DebugSessionLogSourceProvider into the bridge MCP. Move their tests.
4. [ ] Move the VS Code plugin (ideBridge.ts, ideActions, outputBuffer, debugOutputTracker, debugOutputStore, logRoots) here
       under vscode/ as its own implementation. It is not shared with the Visual Studio bridge.
5. [ ] Move the Visual Studio host (TokenSaverPackage bridge wiring, IdeActions, Services bridge section) here, under the Vsix project.
6. [ ] Move docs: Host-MCP/debug-output-capture-contract.md, debugger-control-contract.md, ide-launcher-contract.md,
       and the bridge sections of self_test.md and scripts (update-vs-extension, build-vs-extension-vsix).
7. [ ] Remove all of the above from TokenSaver (sources, tests, csproj references, slnx entries, ProgramRegistration and
       ServiceRegistrationExtensions wiring, behavior config and terminal session config fields that exist only for the bridge).
8. [ ] Build and test: both repos green; the bridge MCP starts; TokenSaver MCP starts without bridge tools.

## Open

- [ ] PR #2 (feature/ide-bridge-ci into dev): CI workflow and solution fix. Review, then merge on approval (merges into dev need a release request).
- [ ] CI has not run on GitHub yet. Once it runs, add build-and-test as a required check on dev.
- [ ] Publish on a v* tag: confirm the publish job pushes to the SperryDev feed with GITHUB_TOKEN (not tested).
- [ ] Add a README that links to docs/plans and the protocol doc in TokenSaver (docs/ide-bridge.md stays there).
- [ ] VS extension install is blocked by a stale extensionDir in the VSIX manifest (VSExtensions\j30o0obs.15i). Investigate the in-process VSSDK port as the fix; not started.
- [ ] Verify end to end: MCP IdeBridgeHealth and IdeListInstances against a live Visual Studio instance.
- [ ] Decide whether the repo becomes a submodule of mcp; if yes, push the submodule first, then the pointer.
