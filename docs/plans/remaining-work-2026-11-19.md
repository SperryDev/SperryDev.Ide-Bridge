# Remaining work (SperryDev.Ide-Bridge), 2026-11-19

Status as of 2026-11-19. Source: the approved split plan (mcp docs/plans/ide-bridge-split.md) and the current branch state.

## Done

- [x] Package SperryDev.Ide.Bridge: moved types, tests, solution, .gitignore, IBridgeLogger, shared manifest record (Ide and LogRoots required), net48 and net10.0-windows targets. 15 tests pass.
- [x] Published to GitHub Packages as 0.1.0 (sperry-github feed); restore verified.
- [x] Re-sync merged into dev through PR #1 (dev at 8f060ab).
- [x] Repo public; branch protection on dev (PRs required, no force push, no deletion; 0 approvals).

## Open

- [ ] PR #2 (feature/ide-bridge-ci into dev): CI workflow and solution fix. Review, then merge on approval (merges into dev need a release request).
- [ ] CI has not run on GitHub yet. Once it runs, add build-and-test as a required check on dev.
- [ ] Publish on a v* tag: confirm the publish job pushes to the SperryDev feed with GITHUB_TOKEN (not tested).
- [ ] Add a README that links to docs/plans and the protocol doc in TokenSaver (docs/ide-bridge.md stays there).
- [ ] VS extension install is blocked by a stale extensionDir in the VSIX manifest (VSExtensions\j30o0obs.15i). Investigate the in-process VSSDK port as the fix; not started.
- [ ] Verify end to end: MCP IdeBridgeHealth and IdeListInstances against a live Visual Studio instance.
- [ ] Decide whether the repo becomes a submodule of mcp; if yes, push the submodule first, then the pointer.
