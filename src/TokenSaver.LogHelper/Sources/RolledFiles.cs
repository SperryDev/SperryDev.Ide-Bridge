using TokenSaver.LogHelper.Models;
using TokenSaver.LogHelper.Security;

namespace TokenSaver.LogHelper.Sources;

/// <summary>The lines read from the rest of a rolled file, and how many older lines were skipped because the rest was too large.</summary>
internal sealed record RolledTail(IReadOnlyList<string> Lines, int Dropped);

/// <summary>
/// Finds and reads the file a rolling log was moved to. A rolling log is renamed (app.log to app.1.log) or copied
/// and truncated, and a new file takes its place. The rolled file is found by content: a sibling whose bytes before the
/// saved offset hash to the cursor's anchor is the file the cursor was reading. That works for any naming scheme and
/// needs no operating-system file identity.
/// </summary>
internal static class RolledFiles
{
    private const int MaxCandidates = 16;
    private const long MaxTailBytes = 64L * 1024 * 1024;

    private static readonly HashSet<string> CompressedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".gz", ".zip", ".7z", ".bz2", ".xz", ".zst",
    };

    /// <summary>
    /// Returns the path of the sibling file the cursor was reading, or null when there is none. Candidates share the
    /// log's stem (the file name up to its first dot), are inside an allowed root, have not been written since before
    /// the cursor's stamp, and are at least as long as the saved offset. The most recently written is tried first.
    /// </summary>
    public static async Task<string?> FindAsync(string canonical, FileCursorState state, LogHelperOptions options, CancellationToken ct)
    {
        if (state.Offset <= 0 || string.IsNullOrEmpty(state.Anchor))
        {
            return null;
        }

        string? directory = Path.GetDirectoryName(canonical);
        string stem = StemOf(Path.GetFileName(canonical));
        if (directory is null || stem.Length == 0 || !Directory.Exists(directory))
        {
            return null;
        }

        List<FileInfo> candidates;
        try
        {
            candidates = new DirectoryInfo(directory)
                .EnumerateFiles(stem + "*")
                .Where(f => !string.Equals(f.FullName, canonical, StringComparison.OrdinalIgnoreCase)
                    && !CompressedExtensions.Contains(f.Extension)
                    && f.Length >= state.Offset
                    && (state.Stamp == 0 || f.LastWriteTimeUtc.Ticks >= state.Stamp))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(MaxCandidates)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        foreach (FileInfo candidate in candidates)
        {
            if (!PathGuard.TryResolve(candidate.FullName, options.AllowedRoots, out string? allowed, out _) || allowed is null)
            {
                continue;
            }

            try
            {
                using FileStream stream = FileLogSource.OpenShared(allowed);
                if (string.Equals(await FileLogSource.AnchorAsync(stream, state.Offset, ct), state.Anchor, StringComparison.Ordinal))
                {
                    return allowed;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Moved or locked while checking: try the next candidate.
            }
        }

        return null;
    }

    /// <summary>
    /// Reads a rolled file from <paramref name="from"/> to its end. The file is final, so an unterminated last line is
    /// a line. A tail over 64 MB is cut to its last 64 MB at a line boundary and the skipped lines are counted.
    /// </summary>
    public static async Task<RolledTail> ReadTailAsync(string path, long from, CancellationToken ct)
    {
        using FileStream stream = FileLogSource.OpenShared(path);
        long length = stream.Length;
        TextLayout layout = await FileLogSource.ReadLayoutAsync(stream, length, ct);

        long start = Math.Max(from, layout.BomLength);
        long skipped = 0;
        bool dropFirst = false;
        if (length - start > MaxTailBytes)
        {
            long cut = length - MaxTailBytes;
            if (layout.Newline != NewlineKind.Utf8)
            {
                cut &= ~1L;
            }

            skipped = await CountNewlinesAsync(stream, start, cut, ct);
            start = cut;
            dropFirst = true;
        }

        if (length <= start)
        {
            return new RolledTail(Array.Empty<string>(), 0);
        }

        byte[] bytes = new byte[length - start];
        await FileLogSource.ReadExactlyAsync(stream, start, bytes, ct);

        var lines = layout.Encoding.GetString(bytes).Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        if (dropFirst && lines.Count > 0)
        {
            // The cut can fall inside a line.
            lines.RemoveAt(0);
            skipped++;
        }

        return new RolledTail(lines, skipped > int.MaxValue ? int.MaxValue : (int)skipped);
    }

    internal static string StemOf(string fileName)
    {
        int dot = fileName.Length > 1 ? fileName.IndexOf('.', 1) : -1;
        return dot < 0 ? fileName : fileName[..dot];
    }

    private static async Task<long> CountNewlinesAsync(FileStream stream, long from, long to, CancellationToken ct)
    {
        long count = 0;
        byte[] buffer = new byte[64 * 1024];
        long position = from;
        while (position < to)
        {
            int want = (int)Math.Min(buffer.Length, to - position);
            stream.Position = position;
            int read = await stream.ReadAsync(buffer.AsMemory(0, want), ct);
            if (read == 0)
            {
                break;
            }

            count += buffer.AsSpan(0, read).Count((byte)0x0A);
            position += read;
        }

        return count;
    }
}
