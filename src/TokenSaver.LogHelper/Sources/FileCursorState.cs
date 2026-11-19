using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TokenSaver.LogHelper.Models;

namespace TokenSaver.LogHelper.Sources;

/// <summary>
/// Resume state for a file source. <see cref="Anchor"/> is a hash of the bytes just before
/// <see cref="Offset"/>; if the bytes there changed (truncation, rewrite) the cursor no longer matches.
/// <see cref="Format"/> is the resolved <see cref="LogFormatSpec"/> as JSON, so a channel is sniffed once.
/// <see cref="Stamp"/> is the file's last-write time (UTC ticks) when it was last read: a file rolled away since then
/// has a last-write time at or after it, which is how a rolled file is told apart from an old one. 0 means unknown.
/// </summary>
internal sealed record FileCursorState(string Path, long Offset, string Anchor, string? Format = null, long Stamp = 0)
{
    public const int AnchorBytes = 256;

    public static string Encode(FileCursorState state) =>
        LogCursor.Encode(JsonSerializer.Serialize(state));

    public static bool TryDecode(string? token, out FileCursorState? state)
    {
        state = null;
        if (!LogCursor.TryDecode(token, out var payload))
        {
            return false;
        }

        try
        {
            state = JsonSerializer.Deserialize<FileCursorState>(payload);
            return state is not null && state.Offset >= 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string ComputeAnchor(byte[] bytesBeforeOffset)
    {
        if (bytesBeforeOffset.Length == 0)
        {
            return string.Empty;
        }

        byte[] digest = SHA256.HashData(bytesBeforeOffset);
        return Convert.ToHexString(digest, 0, 16).ToLowerInvariant();
    }
}
