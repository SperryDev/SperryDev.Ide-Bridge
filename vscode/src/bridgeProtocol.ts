export interface BridgeRequest {
    action?: string;
    args?: Record<string, unknown>;
}

export interface BridgeReplyContext {
    pid: number;
    health(): unknown;
    runAction(action: string, args: Record<string, unknown>): Promise<unknown>;
    onRequest?(): void;
    isUnsupported?(error: unknown): boolean;
}

/** Converts one JSON request line into the protocol's JSON reply line. */
export async function replyToBridgeRequest(line: string, context: BridgeReplyContext): Promise<string> {
    context.onRequest?.();
    try {
        const request = JSON.parse(line) as BridgeRequest;
        const action = request.action ?? "";
        if (action === "health") {
            return JSON.stringify({ ok: true, pid: context.pid, result: context.health() });
        }
        const result = await context.runAction(action, request.args ?? {});
        return JSON.stringify({ ok: true, pid: context.pid, result });
    } catch (error) {
        const message = error instanceof Error ? error.message : String(error);
        const reply: { ok: false; pid: number; error: string; unsupported?: true } = {
            ok: false,
            pid: context.pid,
            error: message,
        };
        if (context.isUnsupported?.(error)) {
            reply.unsupported = true;
        }
        return JSON.stringify(reply);
    }
}
