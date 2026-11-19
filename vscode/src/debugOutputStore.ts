import { OutputBuffer } from "./outputBuffer.js";

/** The parts of a Debug Adapter Protocol message this store reads. */
export interface DapMessage {
    type?: string;
    event?: string;
    body?: { category?: string; output?: string };
}

const ENDED_EVENTS = new Set(["terminated", "exited"]);

/**
 * Keeps the program output of each debug session, separately, and remembers which sessions have ended.
 * Each session's end is a run boundary for the log helper. Only "stdout", "stderr" and "console"
 * output is kept; telemetry and other categories are dropped.
 */
export class DebugOutputStore {
    private readonly buffers = new Map<string, OutputBuffer>();
    private readonly ended: string[] = [];

    constructor(
        private readonly perSessionCapacity = 5000,
        private readonly keepEnded = 5,
    ) {}

    handle(sessionId: string, message: DapMessage): void {
        if (message.type !== "event" || message.event === undefined) {
            return;
        }

        if (message.event === "output") {
            const category = message.body?.category ?? "console";
            const text = message.body?.output;
            if (text === undefined || !["stdout", "stderr", "console"].includes(category)) {
                return;
            }
            this.bufferFor(sessionId).append(text);
            return;
        }

        if (ENDED_EVENTS.has(message.event) && !this.isEnded(sessionId)) {
            this.ended.push(sessionId);
            if (this.ended.length > this.keepEnded) {
                this.ended.splice(0, this.ended.length - this.keepEnded);
            }
        }
    }

    output(sessionId: string): OutputBuffer | undefined {
        return this.buffers.get(sessionId);
    }

    /** Sessions that have produced output, in the order they first appeared. */
    sessionIds(): string[] {
        return [...this.buffers.keys()];
    }

    isEnded(sessionId: string): boolean {
        return this.ended.includes(sessionId);
    }

    endedSessions(): string[] {
        return [...this.ended];
    }

    private bufferFor(sessionId: string): OutputBuffer {
        let buffer = this.buffers.get(sessionId);
        if (buffer === undefined) {
            buffer = new OutputBuffer(this.perSessionCapacity);
            this.buffers.set(sessionId, buffer);
        }
        return buffer;
    }
}
