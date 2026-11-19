import { describe, expect, test } from "vitest";
import { OutputBuffer } from "./outputBuffer";

describe("OutputBuffer", () => {
    test("append_splitAcrossChunks_yieldsOneLine", () => {
        const buf = new OutputBuffer(100);
        buf.append("ab");
        buf.append("c\n");

        expect(buf.read(1, 10).lines).toEqual(["abc"]);
    });

    test("append_keepsEmptyLines", () => {
        const buf = new OutputBuffer(100);
        buf.append("a\n\nb\n");

        expect(buf.read(1, 10).lines).toEqual(["a", "", "b"]);
    });

    test("append_crlfSplitAcrossChunks_noPhantomLine", () => {
        const buf = new OutputBuffer(100);
        buf.append("a\r");
        buf.append("\nb\n");

        expect(buf.read(1, 10).lines).toEqual(["a", "b"]);
    });

    test("read_fromSeq_returnsOnlyNewer", () => {
        const buf = new OutputBuffer(100);
        buf.append("1\n2\n3\n");

        const result = buf.read(2, 10);

        expect(result.lines).toEqual(["2", "3"]);
        expect(result.nextSeq).toBe(4);
        expect(result.dropped).toBe(0);
    });

    test("read_cursorOlderThanHead_reportsDropped", () => {
        const buf = new OutputBuffer(2);
        buf.append("1\n2\n3\n4\n5\n");

        const result = buf.read(1, 10);

        expect(result.lines).toEqual(["4", "5"]);
        expect(result.dropped).toBe(3);
    });

    test("limit_evictsOldest_seqStaysMonotonic", () => {
        const buf = new OutputBuffer(3);
        buf.append(Array.from({ length: 10 }, (_, i) => `line ${i + 1}`).join("\n") + "\n");

        const result = buf.read(1, 10);

        expect(result.lines).toEqual(["line 8", "line 9", "line 10"]);
        expect(result.nextSeq).toBe(11);
    });

    test("tail_returnsLastN_oldestFirst", () => {
        const buf = new OutputBuffer(100);
        buf.append("1\n2\n3\n4\n");

        expect(buf.tail(2)).toEqual(["3", "4"]);
        expect(buf.tail(10)).toEqual(["1", "2", "3", "4"]);
    });

    test("clear_keepsSequencesMonotonic_andReportsDroppedForOldCursor", () => {
        const buf = new OutputBuffer(100);
        buf.append("old\n");
        buf.clear();
        buf.append("new\n");

        const fresh = buf.read(1, 10);

        expect(fresh.lines).toEqual(["new"]);
        expect(fresh.dropped).toBe(1);
        expect(fresh.nextSeq).toBe(3);
    });
});
