export interface OutputReadResult {
    /** Complete lines with sequence numbers at or after the requested cursor, oldest first. */
    lines: string[];
    /** Cursor for the next read. */
    nextSeq: number;
    /** Lines between the requested cursor and the oldest retained line that were evicted or cleared. */
    dropped: number;
}

interface Entry {
    seq: number;
    text: string;
}

/**
 * Line buffer fed by stream chunks. A line can span chunks; only complete lines are stored.
 * Each stored line gets a monotonic sequence number that survives eviction and clear, so a
 * caller holding an old cursor learns how many lines it missed.
 */
export class OutputBuffer {
    private readonly entries: Entry[] = [];
    private partial = "";
    private nextSeqValue = 1;

    constructor(private readonly capacity: number) {}

    append(chunk: string): void {
        const parts = (this.partial + chunk).split("\n");
        this.partial = parts.pop() ?? "";
        for (const raw of parts) {
            this.push(raw.endsWith("\r") ? raw.slice(0, -1) : raw);
        }
    }

    read(fromSeq: number, maxLines: number): OutputReadResult {
        const firstRetained = this.entries.length > 0 ? this.entries[0].seq : this.nextSeqValue;
        const dropped = Math.max(0, firstRetained - fromSeq);
        const start = Math.max(fromSeq, firstRetained);
        const selected = this.entries.filter((e) => e.seq >= start).slice(0, maxLines);
        const nextSeq = selected.length > 0 ? selected[selected.length - 1].seq + 1 : this.nextSeqValue;
        return { lines: selected.map((e) => e.text), nextSeq, dropped };
    }

    /** The last `maxLines` retained lines, oldest first. */
    tail(maxLines: number): string[] {
        return this.entries.slice(Math.max(0, this.entries.length - maxLines)).map((e) => e.text);
    }

    clear(): void {
        this.entries.length = 0;
    }

    private push(text: string): void {
        this.entries.push({ seq: this.nextSeqValue++, text });
        if (this.entries.length > this.capacity) {
            this.entries.shift();
        }
    }
}
