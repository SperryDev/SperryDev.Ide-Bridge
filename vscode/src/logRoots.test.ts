import { describe, expect, test } from "vitest";
import { exthostLogRoot } from "./logRoots";

describe("exthostLogRoot", () => {
    test("fromLogUri_returnsExthostFolder_stable", () => {
        const logPath = String.raw`C:\Users\me\AppData\Roaming\Code\logs\20261115T123608\window1\exthost\local.tokensaver`;

        expect(exthostLogRoot(logPath, undefined)).toBe(String.raw`C:\Users\me\AppData\Roaming\Code\logs\20261115T123608\window1\exthost`);
    });

    test("insiders_returnsExthostFolder", () => {
        const logPath = String.raw`C:\Users\me\AppData\Roaming\Code - Insiders\logs\20261115T1\window2\exthost\local.tokensaver`;

        expect(exthostLogRoot(logPath, undefined)).toBe(String.raw`C:\Users\me\AppData\Roaming\Code - Insiders\logs\20261115T1\window2\exthost`);
    });

    test("vscodium_returnsExthostFolder", () => {
        const logPath = "/home/me/.config/VSCodium/logs/20261115T1/window1/exthost/local.tokensaver";

        expect(exthostLogRoot(logPath, undefined)).toBe("/home/me/.config/VSCodium/logs/20261115T1/window1/exthost");
    });

    test("portable_returnsExthostFolder", () => {
        const logPath = String.raw`D:\VSCodePortable\data\logs\20261115T1\window1\exthost\local.tokensaver`;

        expect(exthostLogRoot(logPath, undefined)).toBe(String.raw`D:\VSCodePortable\data\logs\20261115T1\window1\exthost`);
    });

    test("noExthostSegment_returnsUndefined", () => {
        expect(exthostLogRoot(String.raw`C:\somewhere\else`, undefined)).toBeUndefined();
    });

    test("remoteWindow_returnsUndefined_logsAreOnTheRemoteHost", () => {
        const logPath = String.raw`C:\Users\me\AppData\Roaming\Code\logs\20261115T1\window1\exthost\local.tokensaver`;

        expect(exthostLogRoot(logPath, "wsl")).toBeUndefined();
    });
});
