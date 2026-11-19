# Visual Studio VSIX for SperryDev.Ide.Bridge, 2026-11-19

Status as of 2026-11-19. Branch: feature/ide-bridge-vsix (from origin/dev). Work is uncommitted.

## Done

- [x] VSIX project at src/SperryDev.Ide.Bridge.Vsix (classic VSSDK, net48, in-process).
- [x] Package starts the bridge on load (BridgePackage) and registers a Tools-menu command "Show IDE Bridge Status" that writes to an Output pane (BridgeStatusCommand).
- [x] Command table (VSCommandTable.vsct) compiles with 0 errors.
- [x] Builds with 0 errors using the VS-installed MSBuild.exe (`dotnet build` cannot run the VSSDK tasks).
- [x] Produces src/SperryDev.Ide.Bridge.Vsix/SperryDev.Ide.Bridge.Vsix.vsix.
- [x] Install was retried after closing Visual Studio; the installer exited with code 0.

## Open

- [ ] Verify the install: the VSIX is not confirmed in VS 18 Extensions. Check Extensions > Manage Extensions, then open the Tools menu and run "Show IDE Bridge Status".
- [ ] Remove hardcoded VS 18 Community path in the csproj (VsSdkTargetsPath, TemplateOutputDirectory). Resolve it from the VS install instead.
- [ ] Align Microsoft.VisualStudio.SDK with the resolved version (18.11.1036 is used; NU1603 warns that 18.4.33 was not found).
- [ ] Remove obsolete InstalledProductRegistration attribute (CS0618) and use the manifest only.
- [ ] Commit the VSIX project on feature/ide-bridge-vsix, then open a PR to dev (merges into dev need approval).
- [ ] Verify end to end: IdeBridgeHealth from MCP against the running VS instance.

## Not part of this plan

- Media view playback controls (play/pause, reverse, fast-forward) under the media, as a reusable control. Location unknown; the user did not name the project. Needs the repo or path before any work.
- wip has 11 commits ahead of origin/wip from another session (migration stage 3). Do not touch without checking.
