# IDE Bridge Self-Test

Use this checklist to exercise the standalone `SperryDev.Ide.Bridge.Mcp` server and a live IDE bridge. Load the tool schemas before calling tools. If there is no registered IDE instance, record the live checks as unavailable rather than launching or changing an IDE as part of a read-only pass.

## Read-only checks

- `IdeListInstances` — confirm the registered IDE kind, process ID, and solution path.
- `IdeBridgeHealth` — check the listener and request counters; pass `solutionPath` when more than one IDE instance is open.
- `IdeGetOutput` and `IdeSearchOutput` — inspect an existing Build or Debug output pane.
- `IdeGetBuildDiagnostics` — read the current Error List.
- `IdeListConfigurations` — read the solution configurations.
- `IdeGetDebugState`, `IdeListBreakpoints`, and `IdeListWatches` — read the debugger state.
- `IdeGetTestResults` — read the newest TRX result for a supplied solution path.
- `IdeGetLocals` — inspect locals only when the debugger is already stopped at a breakpoint.

Record each result as PASS, FAIL, or NOT RUN with the exact tool arguments and any error message.

## Actions requiring a deliberate live check

These tools change IDE or project state. Run them only when the test operator intends the specific action and has a suitable scratch solution:

- `IdeEvaluate` — evaluates an expression in the live debug context and may have side effects.
- `IdeRunTests`, `IdeBuild`, `IdeClean`, and `IdePublish`.
- `IdeSetStartupProject` and `IdeSetConfiguration`.
- `IdeStartDebugging`, `IdeStopDebugging`, `IdeStep`, `IdeSetBreakpoint`, and `IdeRemoveBreakpoint`.
- `IdeFormatDocument` and `IdeCodeCleanup`.
- `IdeLaunchInstance`, `IdeKillInstance`, and `IdeKillProcess`.

Record the selected solution, expected state change, result, and final state. Do not run kill or publish checks against a user's active work.
