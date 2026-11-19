import * as fs from "fs";
import * as net from "net";
import * as path from "path";
import * as vscode from "vscode";

import { runIdeAction, UnsupportedActionError } from "./ideActions.js";
import { registerDebugOutputTracker } from "./debugOutputTracker.js";
import { exthostLogRoot } from "./logRoots.js";

// Named-pipe bridge for this VS Code window, mirroring the Visual Studio bridge. Each request is one JSON line
// {"action":"...","args":{...}}; each reply is one JSON line {"ok":true,"pid":N,"result":...} or
// {"ok":false,"pid":N,"error":"..."}. The pipe name embeds the extension host's pid, so several windows run side by side.
// A manifest in %ProgramData%\TokenSaver\vs-instances\<pid>.json lets the MCP server find the window and its workspace.
// A watchdog restarts the pipe if it closes, and rewrites the manifest.

const WATCHDOG_MS = 10_000;

interface Request {
    action?: string;
    args?: Record<string, unknown>;
}

function instancesDirectory(): string {
    return path.join(process.env.ProgramData ?? "C:\\ProgramData", "TokenSaver", "vs-instances");
}

function pipeNameFor(pid: number): string {
    return `TokenSaver.VSCode.${pid}`;
}

function manifestPath(pid: number): string {
    return path.join(instancesDirectory(), `${pid}.json`);
}

class IdeBridge implements vscode.Disposable {
    private server: net.Server | undefined;
    private readonly startedUtc = new Date();
    private lastRequestUtc: Date | null = null;
    private requestsHandled = 0;
    private watchdog: NodeJS.Timeout | undefined;
    private readonly pid = process.pid;
    private readonly pipeName = pipeNameFor(process.pid);
    private readonly logRoots: string[];

    constructor(extensionLogPath: string) {
        const root = exthostLogRoot(extensionLogPath, vscode.env.remoteName);
        this.logRoots = root ? [root] : [];
    }

    start(): void {
        this.listen();
        this.writeManifest();
        this.watchdog = setInterval(() => this.check(), WATCHDOG_MS);
    }

    dispose(): void {
        if (this.watchdog) clearInterval(this.watchdog);
        this.server?.close();
        this.server = undefined;
        try {
            fs.unlinkSync(manifestPath(this.pid));
        } catch {
            // Already gone.
        }
    }

    private listen(): void {
        const server = net.createServer((socket) => this.handle(socket));
        server.on("error", (err) => {
            console.error(`TokenSaver IDE bridge error: ${err.message}`);
        });
        server.listen(`\\\\.\\pipe\\${this.pipeName}`);
        this.server = server;
    }

    // Restarts the pipe and rewrites the manifest if the listener has stopped or the manifest was removed.
    private check(): void {
        if (!this.server?.listening) {
            this.listen();
        }
        if (!fs.existsSync(manifestPath(this.pid))) {
            this.writeManifest();
        }
    }

    private writeManifest(): void {
        fs.mkdirSync(instancesDirectory(), { recursive: true });
        const solutionPath = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath ?? null;
        const manifest = {
            pid: this.pid,
            pipeName: this.pipeName,
            solutionPath,
            updatedUtc: new Date().toISOString(),
            ide: "VsCode",
            logRoots: this.logRoots,
        };
        const target = manifestPath(this.pid);
        const temp = `${target}.tmp`;
        fs.writeFileSync(temp, JSON.stringify(manifest, null, 2));
        fs.renameSync(temp, target);
    }

    private health(): unknown {
        return {
            ide: "VsCode",
            listener_running: this.server?.listening ?? false,
            started_utc: this.startedUtc.toISOString(),
            last_request_utc: this.lastRequestUtc?.toISOString() ?? null,
            requests_handled: this.requestsHandled,
            solution_path: vscode.workspace.workspaceFolders?.[0]?.uri.fsPath ?? null,
            pipe_name: this.pipeName,
        };
    }

    private handle(socket: net.Socket): void {
        let buffer = "";
        let answered = false;
        socket.setEncoding("utf8");
        socket.on("data", (chunk: string) => {
            buffer += chunk;
            const newline = buffer.indexOf("\n");
            if (newline < 0 || answered) return;
            answered = true;
            const line = buffer.slice(0, newline);
            void this.reply(line).then((text) => {
                socket.end(text + "\n");
            });
        });
        socket.on("error", () => {
            // The client went away; nothing to answer.
        });
    }

    private async reply(line: string): Promise<string> {
        this.requestsHandled++;
        this.lastRequestUtc = new Date();
        try {
            const request = JSON.parse(line) as Request;
            const action = request.action ?? "";
            if (action === "health") {
                return JSON.stringify({ ok: true, pid: this.pid, result: this.health() });
            }
            const result = await runIdeAction(action, request.args ?? {});
            return JSON.stringify({ ok: true, pid: this.pid, result });
        } catch (err) {
            const message = err instanceof Error ? err.message : String(err);
            const unsupported = err instanceof UnsupportedActionError;
            return JSON.stringify({ ok: false, pid: this.pid, error: message, unsupported });
        }
    }
}

let bridge: IdeBridge | undefined;

/** Starts the IDE bridge for this window. Called once from activate(). Disposed with the extension. */
export function startIdeBridge(context: vscode.ExtensionContext): void {
    registerDebugOutputTracker(context);
    if (bridge) return;
    bridge = new IdeBridge(context.logUri.fsPath);
    bridge.start();
    context.subscriptions.push(bridge);
}
