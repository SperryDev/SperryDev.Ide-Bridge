using System;
using System.Security.Cryptography;
using System.Text;

namespace SperryDev.Ide.Bridge;

/// <summary>
/// One ranged read of a pane's text. <see cref="NextOffset"/> and <see cref="Anchor"/> are the cursor for the next call.
/// <see cref="Reset"/> is true when the cursor no longer matches the pane (cleared, refilled or shorter); the slice then starts at 0.
/// </summary>
public sealed record OutputSliceResult(string Text, int NextOffset, string Anchor, bool Reset);

/// <summary>
/// Pure ranged read over a pane's full text. The anchor is a hash of the characters just before the offset, so a
/// pane that was cleared and refilled past the old offset is still detected. The slice is cut at a line boundary
/// when one fits in <c>maxChars</c>; otherwise it is cut mid-line.
/// </summary>
public static class OutputPaneSlice
{
    public const int AnchorChars = 64;

    public static OutputSliceResult Slice(string text, int fromOffset, int maxChars, string? anchor)
    {
        bool reset = fromOffset < 0
            || fromOffset > text.Length
            || (anchor is not null && !string.Equals(AnchorOf(text, fromOffset), anchor, StringComparison.Ordinal));
        int offset = reset ? 0 : fromOffset;

        int available = text.Length - offset;
        int take = Math.Min(available, Math.Max(0, maxChars));
        if (take < available)
        {
            int newline = text.LastIndexOf('\n', offset + take - 1, take);
            if (newline >= offset)
            {
                take = newline + 1 - offset;
            }
        }

        string slice = text.Substring(offset, take);
        int next = offset + take;
        return new OutputSliceResult(slice, next, AnchorOf(text, next), reset);
    }

    private static string AnchorOf(string text, int offset)
    {
        int start = Math.Max(0, offset - AnchorChars);
        int length = offset - start;
        if (length <= 0)
        {
            return string.Empty;
        }

        using var sha = SHA256.Create();
        byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(text.Substring(start, length)));
        return BitConverter.ToString(digest, 0, 16).Replace("-", string.Empty).ToLowerInvariant();
    }
}
