import * as vscode from "vscode";
import { DebugOutputStore, type DapMessage } from "./debugOutputStore.js";

/** Program output of debug sessions, shared with the bridge's getDebugOutput and listDebugSessions actions. */
export const debugOutputStore = new DebugOutputStore();

/** Observes Debug Adapter Protocol traffic for every debug session. Output events are captured without changing the session. */
export function registerDebugOutputTracker(context: vscode.ExtensionContext): void {
    context.subscriptions.push(
        vscode.debug.registerDebugAdapterTrackerFactory("*", {
            createDebugAdapterTracker(session: vscode.DebugSession) {
                return {
                    onDidSendMessage(message: unknown) {
                        debugOutputStore.handle(session.id, message as DapMessage);
                    },
                };
            },
        }),
    );
}
