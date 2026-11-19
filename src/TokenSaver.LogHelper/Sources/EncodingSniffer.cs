using System.Text;

namespace TokenSaver.LogHelper.Sources;

internal enum NewlineKind
{
    Utf8,
    Utf16Le,
    Utf16Be,
}

internal readonly record struct TextLayout(int BomLength, NewlineKind Newline, Encoding Encoding);

/// <summary>Detects UTF-8 (with or without BOM) and UTF-16 LE/BE BOMs. Anything else is read as UTF-8.</summary>
internal static class EncodingSniffer
{
    public static TextLayout Sniff(byte[] head)
    {
        if (head.Length >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF)
        {
            return new TextLayout(3, NewlineKind.Utf8, new UTF8Encoding(false));
        }

        if (head.Length >= 2 && head[0] == 0xFF && head[1] == 0xFE)
        {
            return new TextLayout(2, NewlineKind.Utf16Le, Encoding.Unicode);
        }

        if (head.Length >= 2 && head[0] == 0xFE && head[1] == 0xFF)
        {
            return new TextLayout(2, NewlineKind.Utf16Be, Encoding.BigEndianUnicode);
        }

        return new TextLayout(0, NewlineKind.Utf8, new UTF8Encoding(false));
    }

    /// <summary>Returns the byte length up to and including the last complete newline, or 0.</summary>
    public static int LastCompleteLineEnd(byte[] buffer, int length, NewlineKind newline)
    {
        switch (newline)
        {
            case NewlineKind.Utf8:
                for (int i = length - 1; i >= 0; i--)
                {
                    if (buffer[i] == 0x0A)
                    {
                        return i + 1;
                    }
                }

                return 0;

            case NewlineKind.Utf16Le:
                for (int i = length - 2; i >= 0; i -= 2)
                {
                    if (buffer[i] == 0x0A && buffer[i + 1] == 0x00)
                    {
                        return i + 2;
                    }
                }

                return 0;

            default:
                for (int i = length - 2; i >= 0; i -= 2)
                {
                    if (buffer[i] == 0x00 && buffer[i + 1] == 0x0A)
                    {
                        return i + 2;
                    }
                }

                return 0;
        }
    }
}
