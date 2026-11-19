namespace TokenSaver.LogHelper.Models;

/// <summary>
/// Addresses one log channel: optional host, a source scheme, and the source-specific name.
/// Text form: <c>[host/]scheme:name</c>. The name may contain colons (for example Windows drive letters).
/// </summary>
public sealed record ChannelId(string? Host, string Scheme, string Name)
{
    private static readonly HashSet<string> KnownSchemes = new(StringComparer.Ordinal)
    {
        "file", "vscode", "vsout", "debug", "eventlog", "syslog",
    };

    public static bool TryParse(string? text, out ChannelId? id)
    {
        id = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        int colon = text.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0)
        {
            return false;
        }

        string prefix = text[..colon];
        string? host = null;
        string scheme = prefix;

        int slash = prefix.IndexOf('/', StringComparison.Ordinal);
        if (slash >= 0)
        {
            host = prefix[..slash];
            scheme = prefix[(slash + 1)..];
            if (host.Length == 0)
            {
                return false;
            }
        }

        if (!KnownSchemes.Contains(scheme))
        {
            return false;
        }

        string name = text[(colon + 1)..];
        if (name.Length == 0)
        {
            return false;
        }

        id = new ChannelId(host, scheme, name);
        return true;
    }

    public static ChannelId Parse(string text)
    {
        if (TryParse(text, out var id) && id is not null)
        {
            return id;
        }

        throw new FormatException($"Invalid channel id '{text}'. Expected [host/]scheme:name with a known scheme.");
    }

    public override string ToString() =>
        Host is null ? $"{Scheme}:{Name}" : $"{Host}/{Scheme}:{Name}";
}
