import { readFileSync } from "node:fs";
import { describe, expect, test } from "vitest";
import { replyToBridgeRequest } from "./bridgeProtocol.js";

type Fixture = { request?: { action: string }; raw?: string; ok: boolean; reply_keys: string[] };
const fixtures = JSON.parse(readFileSync(new URL("../../conformance/fixtures.json", import.meta.url), "utf8")) as Record<string, Fixture>;

const health = {
    listener_running: true,
    pipe_name: "TokenSaver.VSCode.1234",
    started_utc: "2026-01-01T00:00:00.000Z",
    last_request_utc: null,
    requests_handled: 1,
    solution_path: null,
};

describe("shared IDE bridge conformance fixtures", () => {
    for (const [name, fixture] of Object.entries(fixtures)) {
        test(name, async () => {
            const line = fixture.raw ?? JSON.stringify(fixture.request);
            const reply = JSON.parse(await replyToBridgeRequest(line, {
                pid: 1234,
                health: () => health,
                runAction: async (action) => {
                    throw new Error(`Unknown action '${action}'.`);
                },
                isUnsupported: () => false,
            })) as Record<string, unknown>;

            expect(reply.ok).toBe(fixture.ok);
            expect(Object.keys(reply)).toEqual(fixture.reply_keys);
            if (name === "health") {
                expect(Object.keys(reply.result as object)).toEqual(expect.arrayContaining([
                    "listener_running", "pipe_name", "started_utc", "last_request_utc", "requests_handled", "solution_path",
                ]));
            }
        });
    }
});


