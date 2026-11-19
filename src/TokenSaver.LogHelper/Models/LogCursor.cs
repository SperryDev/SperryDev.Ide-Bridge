using System.Text;

namespace TokenSaver.LogHelper.Models;

/// <summary>
/// Opaque, URL-safe cursor tokens. Each source defines its own payload; the token is self-contained
/// so any server instance can resume from it.
/// </summary>
public static class LogCursor
{
    public static string Encode(string payload)
    {
        string base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
        return base64.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static bool TryDecode(string? token, out string payload)
    {
        payload = string.Empty;
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        string base64 = token.Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 0:
                break;
            case 2:
                base64 += "==";
                break;
            case 3:
                base64 += "=";
                break;
            default:
                return false;
        }

        try
        {
            payload = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
