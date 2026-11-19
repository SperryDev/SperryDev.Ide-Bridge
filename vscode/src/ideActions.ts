import { spawn, type ChildProcess } from "child_process";
import * as path from "path";
import * as vscode from "vscode";
import { OutputBuffer } from "./outputBuffer.js";
import { debugOutputStore } from "./debugOutputTracker.js";

// Common IDE actions for VS Code. The names match the Visual Studio bridge (TokenSaver.Vs.Extension
// Bridge/IdeActions.cs), so the MCP caller uses the same calls for either IDE. Actions VS Code's API cannot
// perform throw an "not supported" error instead of guessing, so the caller always knows what happened.
//
// Output and watches are kept by this bridge, because VS Code has no API to read other output channels or
// watch expressions. Output holds the lines from clean and publish runs this bridge started. Watches are
// evaluated in the active debug session on each listWatches call, so their values only show while paused.

type Args = Record<string, unknown>;

export class UnsupportedActionError extends Error {
    constructor(action: string) {
        super(`Action '${action}' is not supported in VS Code (no VS Code API for it).`);
    }
}

const OUTPUT_LIMIT = 5000;
const output = new OutputBuffer(OUTPUT_LIMIT);
const trackedProcesses = new Map<number, ChildProcess>();
const watches: string[] = [];
let selectedConfiguration: string | undefined;

function requireString(args: Args, key: string): string {
    const value = args[key];
    if (typeof value !== "string" || value.length === 0) {
        throw new Error(`Missing required argument '${key}'.`);
    }
    return value;
}

function optionalString(args: Args, key: string): string | undefined {
    const value = args[key];
    return typeof value === "string" && value.length > 0 ? value : undefined;
}

function optionalInt(args: Args, key: string): number | undefined {
    const value = args[key];
    return typeof value === "number" ? value : undefined;
}

function resolveFile(file: string): vscode.Uri {
    if (path.isAbsolute(file)) {
        return vscode.Uri.file(file);
    }
    const root = vscode.workspace.workspaceFolders?.[0]?.uri;
    if (!root) {
        throw new Error(`Relative path '${file}' needs an open workspace folder.`);
    }
    return vscode.Uri.joinPath(root, file);
}

function launchConfigurationNames(): string[] {
    const configs = vscode.workspace.getConfiguration("launch").get<Array<{ name?: string }>>("configurations") ?? [];
    return configs.map((c) => c.name ?? "").filter((n) => n.length > 0);
}

function workspaceRoot(): string {
    const root = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
    if (!root) {
        throw new Error("This action needs an open workspace folder.");
    }
    return root;
}

function appendOutput(text: string): void {
    output.append(text);
}

// Runs `dotnet clean` or `dotnet publish` in the workspace folder. Output goes to the bridge's output buffer.
function runDotnet(verb: "clean" | "publish", project: string | undefined): Promise<unknown> {
    const cwd = workspaceRoot();
    const args = project ? [verb, project] : [verb];
    const command = `dotnet ${args.join(" ")}`;
    appendOutput(`> ${command}\n`);
    return new Promise((resolve, reject) => {
        const child = spawn("dotnet", args, { cwd, windowsHide: true });
        const pid = child.pid;
        if (pid === undefined) {
            reject(new Error("dotnet did not start."));
            return;
        }
        trackedProcesses.set(pid, child);
        child.stdout?.on("data", (data: Buffer) => appendOutput(data.toString("utf8")));
        child.stderr?.on("data", (data: Buffer) => appendOutput(data.toString("utf8")));
        child.on("error", (err) => {
            trackedProcesses.delete(pid);
            reject(err);
        });
        child.on("close", (code) => {
            trackedProcesses.delete(pid);
            resolve({ ok: code === 0, exit_code: code, command, cwd, pid });
        });
    });
}

// Kills a process tree this bridge started. taskkill /T takes the children that dotnet spawns too.
function killTracked(pid: number): Promise<unknown> {
    if (!trackedProcesses.has(pid)) {
        throw new Error(`Process ${pid} was not started by this bridge; it will not be killed.`);
    }
    return new Promise((resolve, reject) => {
        const killer = spawn("taskkill", ["/T", "/F", "/PID", String(pid)], { windowsHide: true });
        killer.on("error", reject);
        killer.on("close", (code) => {
            trackedProcesses.delete(pid);
            resolve({ killed: code === 0, pid, exit_code: code });
        });
    });
}

async function evaluateWatches(): Promise<Array<Record<string, unknown>>> {
    const session = vscode.debug.activeDebugSession;
    const frame = vscode.debug.activeStackItem;
    const frameId = frame && "frameId" in frame ? frame.frameId : undefined;
    const results: Array<Record<string, unknown>> = [];
    for (const expression of watches) {
        if (!session) {
            results.push({ expression, error: "No active debug session." });
            continue;
        }
        try {
            const reply = await session.customRequest("evaluate", { expression, frameId, context: "watch" });
            results.push({ expression, value: reply.result, type: reply.type ?? null });
        } catch (err) {
            results.push({ expression, error: err instanceof Error ? err.message : String(err) });
        }
    }
    return results;
}

export async function runIdeAction(action: string, args: Args): Promise<unknown> {
    switch (action) {
        case "status":
            return {
                workspace_folders: vscode.workspace.workspaceFolders?.map((f) => f.uri.fsPath) ?? [],
                active_file: vscode.window.activeTextEditor?.document.uri.fsPath ?? null,
                debug_session: vscode.debug.activeDebugSession?.name ?? null,
            };

        case "startDebugging": {
            const folder = vscode.workspace.workspaceFolders?.[0];
            const name = optionalString(args, "configuration") ?? selectedConfiguration ?? launchConfigurationNames()[0];
            if (!name) {
                throw new Error("No launch configuration found in launch.json.");
            }
            const started = await vscode.debug.startDebugging(folder, name);
            return { started, configuration: name };
        }

        case "stopDebugging":
            await vscode.commands.executeCommand("workbench.action.debug.stop");
            return { stopped: true };

        case "runTests":
            await vscode.commands.executeCommand("testing.runAll");
            return { command: "testing.runAll" };

        case "formatDocument":
            if (!vscode.window.activeTextEditor) {
                throw new Error("No active editor to format.");
            }
            await vscode.commands.executeCommand("editor.action.formatDocument");
            return { formatted: true };

        case "build":
            await vscode.commands.executeCommand("workbench.action.tasks.build");
            return { command: "workbench.action.tasks.build" };

        case "clean":
            return runDotnet("clean", optionalString(args, "project"));

        case "publish":
            return runDotnet("publish", optionalString(args, "project"));

        case "getOutput": {
            const maxLines = optionalInt(args, "maxLines") ?? 200;
            const fromSeq = optionalInt(args, "fromSeq");
            if (fromSeq === undefined) {
                const tail = output.tail(maxLines);
                return { count: tail.length, lines: tail };
            }
            const page = output.read(fromSeq, maxLines);
            return { count: page.lines.length, lines: page.lines, nextSeq: page.nextSeq, dropped: page.dropped };
        }

        case "searchOutput": {
            const pattern = new RegExp(requireString(args, "pattern"));
            const lines = output.read(1, Number.MAX_SAFE_INTEGER).lines.filter((line) => pattern.test(line));
            return { count: lines.length, lines };
        }

        case "clearOutput":
            output.clear();
            return { cleared: true };

        case "listDebugSessions":
            return {
                sessions: debugOutputStore.sessionIds().map((id) => ({ id, ended: debugOutputStore.isEnded(id) })),
                endedRuns: debugOutputStore.endedSessions(),
            };

        case "getDebugOutput": {
            const session = requireString(args, "session");
            const fromSeq = optionalInt(args, "fromSeq") ?? 1;
            const maxLines = optionalInt(args, "maxLines") ?? 200;
            const buffer = debugOutputStore.output(session);
            if (buffer === undefined) {
                return { count: 0, lines: [], nextSeq: fromSeq, dropped: 0, ended: debugOutputStore.isEnded(session) };
            }
            const page = buffer.read(fromSeq, maxLines);
            return {
                count: page.lines.length,
                lines: page.lines,
                nextSeq: page.nextSeq,
                dropped: page.dropped,
                ended: debugOutputStore.isEnded(session),
            };
        }

        case "getBuildDiagnostics": {
            const severity = typeof args.severity === "string" ? args.severity : "all";
            const wanted = severity === "error" ? [vscode.DiagnosticSeverity.Error]
                : severity === "warning" ? [vscode.DiagnosticSeverity.Warning]
                : [vscode.DiagnosticSeverity.Error, vscode.DiagnosticSeverity.Warning];
            const items: Array<{ file: string; line: number; severity: string; message: string }> = [];
            for (const [uri, diagnostics] of vscode.languages.getDiagnostics()) {
                for (const d of diagnostics) {
                    if (!wanted.includes(d.severity)) continue;
                    items.push({
                        file: uri.fsPath,
                        line: d.range.start.line + 1,
                        severity: vscode.DiagnosticSeverity[d.severity],
                        message: d.message,
                    });
                }
            }
            return { count: items.length, items };
        }

        case "codeCleanup":
            await vscode.commands.executeCommand("editor.action.sourceAction", { kind: "source.fixAll", apply: "first" });
            return { command: "editor.action.sourceAction", kind: "source.fixAll" };

        case "setBreakpoint": {
            const uri = resolveFile(requireString(args, "file"));
            const line = optionalInt(args, "line");
            if (line === undefined) throw new Error("Missing required argument 'line'.");
            vscode.debug.addBreakpoints([new vscode.SourceBreakpoint(new vscode.Location(uri, new vscode.Position(line - 1, 0)))]);
            return { file: uri.fsPath, line };
        }

        case "removeBreakpoint": {
            const uri = resolveFile(requireString(args, "file"));
            const line = optionalInt(args, "line");
            const matches = vscode.debug.breakpoints.filter(
                (b): b is vscode.SourceBreakpoint =>
                    b instanceof vscode.SourceBreakpoint &&
                    b.location.uri.fsPath === uri.fsPath &&
                    (line === undefined || b.location.range.start.line + 1 === line),
            );
            vscode.debug.removeBreakpoints(matches);
            return { removed: matches.length };
        }

        case "listBreakpoints":
            return {
                breakpoints: vscode.debug.breakpoints
                    .filter((b): b is vscode.SourceBreakpoint => b instanceof vscode.SourceBreakpoint)
                    .map((b) => ({
                        file: b.location.uri.fsPath,
                        line: b.location.range.start.line + 1,
                        enabled: b.enabled,
                        condition: b.condition ?? null,
                    })),
            };

        case "step": {
            // The MCP ide_step tool sends "mode" (as the Visual Studio bridge expects); older callers sent "kind".
            const kind = optionalString(args, "kind") ?? requireString(args, "mode");
            const commands: Record<string, string> = {
                over: "workbench.action.debug.stepOver",
                into: "workbench.action.debug.stepInto",
                out: "workbench.action.debug.stepOut",
                continue: "workbench.action.debug.continue",
            };
            const command = commands[kind];
            if (!command) throw new Error(`Unknown step kind '${kind}'. Use over, into, out, or continue.`);
            await vscode.commands.executeCommand(command);
            return { step: kind };
        }

        case "debugState": {
            const session = vscode.debug.activeDebugSession;
            const frame = vscode.debug.activeStackItem;
            return {
                mode: session ? "debugging" : "none",
                session: session?.name ?? null,
                thread_id: frame && "threadId" in frame ? frame.threadId : null,
            };
        }

        case "evaluate": {
            const session = vscode.debug.activeDebugSession;
            if (!session) throw new Error("No active debug session.");
            const expression = requireString(args, "expression");
            const frame = vscode.debug.activeStackItem;
            const frameId = frame && "frameId" in frame ? frame.frameId : undefined;
            const reply = await session.customRequest("evaluate", { expression, frameId, context: "repl" });
            return { expression, result: reply.result, type: reply.type ?? null };
        }

        case "addWatch": {
            const expression = requireString(args, "expression");
            if (!watches.includes(expression)) {
                watches.push(expression);
            }
            return { added: expression, count: watches.length };
        }

        case "removeWatch": {
            const expression = requireString(args, "expression");
            const index = watches.indexOf(expression);
            if (index >= 0) {
                watches.splice(index, 1);
            }
            return { removed: index >= 0, count: watches.length };
        }

        case "listWatches":
            return { count: watches.length, watches: await evaluateWatches() };

        case "getLocals": {
            const session = vscode.debug.activeDebugSession;
            const frame = vscode.debug.activeStackItem;
            if (!session || !frame || !("frameId" in frame)) {
                throw new Error("Not stopped at a breakpoint; locals need a paused debug session.");
            }
            const scopes = (await session.customRequest("scopes", { frameId: frame.frameId })).scopes as Array<{ name: string; variablesReference: number }>;
            const locals: Array<{ scope: string; name: string; value: string; type: string | null }> = [];
            for (const scope of scopes) {
                const vars = (await session.customRequest("variables", { variablesReference: scope.variablesReference })).variables as Array<{ name: string; value: string; type?: string }>;
                for (const v of vars) {
                    locals.push({ scope: scope.name, name: v.name, value: v.value, type: v.type ?? null });
                }
            }
            return { count: locals.length, locals };
        }

        case "listConfigurations":
            return { configurations: launchConfigurationNames(), selected: selectedConfiguration ?? null };

        case "setConfiguration": {
            const configuration = requireString(args, "configuration");
            if (!launchConfigurationNames().includes(configuration)) {
                throw new Error(`No launch configuration named '${configuration}' in launch.json.`);
            }
            selectedConfiguration = configuration;
            return { configuration };
        }

        case "killProcess": {
            const pid = optionalInt(args, "pid");
            if (pid === undefined) throw new Error("Missing required argument 'pid'.");
            return killTracked(pid);
        }

        case "health":
            return { ide: "VsCode" };

        case "setStartupProject":
        case "prepareKill":
            throw new UnsupportedActionError(action);

        default:
            throw new Error(`Unknown action '${action}'.`);
    }
}
