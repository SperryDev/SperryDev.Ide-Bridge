import { describe, expect, test } from "vitest";
import { DebugOutputStore } from "./debugOutputStore";

const output = (category: string, text: string) => ({
    type: "event",
    event: "output",
    body: { category, output: text },
});

describe("DebugOutputStore", () => {
    test("outputEvent_stdout_appended", () => {
        const store = new DebugOutputStore();
        store.handle("s1", output("stdout", "hello\n"));

        expect(store.output("s1")?.tail(10)).toEqual(["hello"]);
    });

    test("telemetryCategory_ignored", () => {
        const store = new DebugOutputStore();
        store.handle("s1", output("telemetry", "secret-ish telemetry\n"));

        expect(store.output("s1")?.tail(10) ?? []).toEqual([]);
    });

    test("twoSessions_keptSeparate", () => {
        const store = new DebugOutputStore();
        store.handle("s1", output("stdout", "from one\n"));
        store.handle("s2", output("stderr", "from two\n"));

        expect(store.output("s1")?.tail(10)).toEqual(["from one"]);
        expect(store.output("s2")?.tail(10)).toEqual(["from two"]);
    });

    test("sessionEnd_marksRunBoundary", () => {
        const store = new DebugOutputStore();
        store.handle("s1", output("stdout", "work\n"));
        store.handle("s1", { type: "event", event: "terminated" });

        expect(store.isEnded("s1")).toBe(true);
        expect(store.endedSessions()).toEqual(["s1"]);
    });

    test("nonOutputEvents_ignored", () => {
        const store = new DebugOutputStore();
        store.handle("s1", { type: "event", event: "stopped", body: { reason: "breakpoint" } });

        expect(store.output("s1")).toBeUndefined();
        expect(store.isEnded("s1")).toBe(false);
    });

    test("sessionIds_listsSessionsThatProducedOutput", () => {
        const store = new DebugOutputStore();
        store.handle("s1", output("stdout", "a\n"));
        store.handle("s2", { type: "event", event: "stopped" });
        store.handle("s3", output("stdout", "b\n"));

        expect(store.sessionIds()).toEqual(["s1", "s3"]);
    });

    test("keepsLastFiveEndedSessions", () => {
        const store = new DebugOutputStore();
        for (let i = 1; i <= 7; i++) {
            store.handle(`s${i}`, { type: "event", event: "exited" });
        }

        expect(store.endedSessions()).toEqual(["s3", "s4", "s5", "s6", "s7"]);
    });
});
