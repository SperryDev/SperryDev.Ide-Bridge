using System.Text.Json;
using TokenSaver.LogHelper.Contracts;
using TokenSaver.LogHelper.Models;
using TokenSaver.LogHelper.Parsing;
using TokenSaver.LogHelper.Security;

namespace TokenSaver.LogHelper.Sources;

/// <summary>
/// Reads one log file incrementally, so only new lines are read. Only complete lines are consumed; a partial
/// trailing line is left for the next read. <c>maxEntries</c> is not enforced here; callers cap output.
/// <para>
/// A rolling log is followed across a roll. When the bytes before the cursor's offset are no longer in the file at
/// the path (it was renamed or copied and truncated, and a new file took its place), the rolled file is found by
/// content (see <see cref="RolledFiles"/>), its unread rest is read first, and reading continues at the start of the
/// new file. <see cref="LogReadBatch.Rotated"/> is then set, and no line is lost or repeated. If no rolled file is
/// found, the file was truncated or rewritten: reading restarts from the beginning and <see cref="LogReadBatch.Reset"/> is set.
/// </para>
/// </summary>
public sealed class FileLogSource : ILogSource
{
    private const int MaxChunkBytes = 8 * 1024 * 1024;

    private readonly string _path;
    private readonly LogHelperOptions _options;

    public FileLogSource(string path, LogHelperOptions options)
    {
        _path = path;
        _options = options;
    }

    public Task<LogReadBatch> ReadAsync(string? cursor, int maxEntries, CancellationToken ct) =>
        ReadAsync(cursor, maxEntries, null, ct);

    public async Task<LogReadBatch> ReadAsync(string? cursor, int maxEntries, LogFormatSpec? format, CancellationToken ct)
    {
        if (!PathGuard.TryResolve(_path, _options.AllowedRoots, out string? canonical, out string? reason) || canonical is null)
        {
            throw new UnauthorizedAccessException(reason);
        }

        FileCursorState? state = FileCursorState.TryDecode(cursor, out FileCursorState? decoded) && decoded is not null && decoded.Path == canonical
            ? decoded
            : null;

        if (!File.Exists(canonical))
        {
            return await ReadMissingAsync(canonical, cursor, state, format, ct);
        }

        using FileStream stream = OpenShared(canonical);
        long stamp = File.GetLastWriteTimeUtc(canonical).Ticks;
        long length = stream.Length;
        TextLayout layout = await ReadLayoutAsync(stream, length, ct);

        Resume resume = await ResumeAsync(stream, canonical, length, layout, state, ct);
        return await ReadFromAsync(stream, canonical, length, layout, resume, format, stamp, ct);
    }

    internal static FileStream OpenShared(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 4096, useAsync: true);

    internal static async Task<TextLayout> ReadLayoutAsync(FileStream stream, long length, CancellationToken ct)
    {
        int headLength = (int)Math.Min(4, length);
        byte[] head = new byte[headLength];
        await ReadExactlyAsync(stream, 0, head, ct);
        return EncodingSniffer.Sniff(head);
    }

    internal static async Task<string> AnchorAsync(FileStream stream, long offset, CancellationToken ct)
    {
        int count = (int)Math.Min(FileCursorState.AnchorBytes, offset);
        if (count <= 0)
        {
            return string.Empty;
        }

        byte[] bytes = new byte[count];
        await ReadExactlyAsync(stream, offset - count, bytes, ct);
        return FileCursorState.ComputeAnchor(bytes);
    }

    internal static async Task ReadExactlyAsync(FileStream stream, long position, byte[] buffer, CancellationToken ct)
    {
        stream.Position = position;
        int read = 0;
        while (read < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(read), ct);
            if (n == 0)
            {
                break;
            }

            read += n;
        }
    }

    // Where to resume in the file at the path, and which rolled file (if any) holds the unread rest of the old one.
    private sealed record Resume(long Offset, bool Reset, string? Format, string? RolledPath, long RolledFrom);

    private async Task<Resume> ResumeAsync(FileStream stream, string canonical, long length, TextLayout layout, FileCursorState? state, CancellationToken ct)
    {
        if (state is null)
        {
            return new Resume(layout.BomLength, false, null, null, 0);
        }

        if (state.Offset <= length && string.Equals(await AnchorAsync(stream, state.Offset, ct), state.Anchor, StringComparison.Ordinal))
        {
            return new Resume(Math.Max(state.Offset, layout.BomLength), false, state.Format, null, 0);
        }

        // The bytes the cursor stood on are gone from this path: rolled away, or truncated and rewritten.
        string? rolled = await RolledFiles.FindAsync(canonical, state, _options, ct);
        return rolled is null
            ? new Resume(layout.BomLength, true, null, null, 0)
            : new Resume(layout.BomLength, false, state.Format, rolled, state.Offset);
    }

    private async Task<LogReadBatch> ReadFromAsync(
        FileStream stream, string canonical, long length, TextLayout layout, Resume resume, LogFormatSpec? format, long stamp, CancellationToken ct)
    {
        // An explicit format from the caller wins. Otherwise the format stored in the cursor is reused, so a
        // channel is sniffed once and then stays on the same format until the cursor resets.
        LogFormatSpec? explicitSpec = format is not null && StructuredParser.NameOf(format) != "auto" ? format : null;
        LogFormatSpec? spec = explicitSpec ?? ReadSpec(resume.Format);

        var entries = new List<LogEntry>();
        int dropped = 0;
        if (resume.RolledPath is not null)
        {
            RolledTail tail = await RolledFiles.ReadTailAsync(resume.RolledPath, resume.RolledFrom, ct);
            dropped = tail.Dropped;
            if (tail.Lines.Count > 0)
            {
                spec ??= FormatSniffer.Resolve(format, tail.Lines.ToList(), _options);
                entries.AddRange(ParseWith(spec, tail.Lines));
            }
        }

        long offset = resume.Offset;
        List<string> lines = [];
        long consumed = 0;
        if (length - offset > 0)
        {
            (lines, consumed) = await ReadCompleteLinesAsync(stream, offset, length, layout, ct);
        }

        if (lines.Count > 0)
        {
            spec ??= FormatSniffer.Resolve(format, lines, _options);
            entries.AddRange(ParseWith(spec, lines));
        }

        long newOffset = offset + consumed;
        string anchor = await AnchorAsync(stream, newOffset, ct);
        string? carried = spec is not null ? JsonSerializer.Serialize(spec) : (resume.Reset ? null : resume.Format);
        string cursor = FileCursorState.Encode(new FileCursorState(canonical, newOffset, anchor, carried, stamp));

        return new LogReadBatch(entries, cursor, resume.Reset, dropped, Rotated: resume.RolledPath is not null);
    }

    // The file is gone. With a cursor into it, keep the cursor: it is either rolled away (read the rest of it now) or
    // deleted (the next file at this path is then read from its start once it appears).
    private async Task<LogReadBatch> ReadMissingAsync(string canonical, string? cursor, FileCursorState? state, LogFormatSpec? format, CancellationToken ct)
    {
        if (state is null || state.Offset <= 0 || cursor is null)
        {
            return Empty(FileCursorState.Encode(new FileCursorState(canonical, 0, string.Empty)), reset: false);
        }

        string? rolled = await RolledFiles.FindAsync(canonical, state, _options, ct);
        if (rolled is null)
        {
            return Empty(cursor, reset: false);
        }

        RolledTail tail = await RolledFiles.ReadTailAsync(rolled, state.Offset, ct);
        LogFormatSpec? explicitSpec = format is not null && StructuredParser.NameOf(format) != "auto" ? format : null;
        LogFormatSpec? spec = explicitSpec ?? ReadSpec(state.Format);
        var entries = new List<LogEntry>();
        if (tail.Lines.Count > 0)
        {
            spec ??= FormatSniffer.Resolve(format, tail.Lines.ToList(), _options);
            entries.AddRange(ParseWith(spec, tail.Lines));
        }

        // Offset 0 with no anchor: the next file at this path is read from its start.
        string next = FileCursorState.Encode(new FileCursorState(canonical, 0, string.Empty, spec is not null ? JsonSerializer.Serialize(spec) : state.Format));
        return new LogReadBatch(entries, next, Reset: false, tail.Dropped, Rotated: true);
    }

    private static async Task<(List<string> Lines, long Consumed)> ReadCompleteLinesAsync(
        FileStream stream, long offset, long length, TextLayout layout, CancellationToken ct)
    {
        int toRead = (int)Math.Min(length - offset, MaxChunkBytes);
        byte[] buffer = new byte[toRead];
        await ReadExactlyAsync(stream, offset, buffer, ct);

        int consumed = EncodingSniffer.LastCompleteLineEnd(buffer, toRead, layout.Newline);
        if (consumed == 0)
        {
            return ([], 0);
        }

        string text = layout.Encoding.GetString(buffer, 0, consumed);
        // The consumed text always ends with '\n', so the final split element is empty and is dropped.
        string[] parts = text.Split('\n');
        return (parts.Take(parts.Length - 1).Select(l => l.TrimEnd('\r')).ToList(), consumed);
    }

    private IReadOnlyList<LogEntry> ParseWith(LogFormatSpec spec, IReadOnlyList<string> lines)
    {
        if (StructuredParser.NameOf(spec) == "plain")
        {
            return new LogParser(_options).Parse(lines);
        }

        if (!StructuredParser.TryCreate(spec, _options, out StructuredParser? parser, out string? error) || parser is null)
        {
            throw new ArgumentException(error, nameof(spec));
        }

        return parser.ParseLines(lines);
    }

    private static LogFormatSpec? ReadSpec(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<LogFormatSpec>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static LogReadBatch Empty(string cursor, bool reset) =>
        new(Array.Empty<LogEntry>(), cursor, reset, 0);
}
