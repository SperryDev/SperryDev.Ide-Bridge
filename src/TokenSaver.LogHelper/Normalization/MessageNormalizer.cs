using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TokenSaver.LogHelper.Normalization;

/// <summary>
/// Reduces a log message to a template by masking volatile parts (times, ids, paths, numbers).
/// Bump <see cref="Version"/> whenever masking rules change; stored snapshots carry it so a
/// change produces a baseline reset instead of a flood of "new" entries.
/// </summary>
public static class MessageNormalizer
{
    public const int Version = 1;

    private static readonly (Regex Pattern, string Replacement)[] Rules =
    [
        (new Regex(@"\x1B\[[0-9;]*[A-Za-z]", RegexOptions.Compiled), string.Empty),
        (new Regex(@"\b\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:?\d{2})?", RegexOptions.Compiled), "<ts>"),
        (new Regex(@"\b(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\s+\d{1,2}\s\d{2}:\d{2}:\d{2}", RegexOptions.Compiled), "<ts>"),
        (new Regex(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b", RegexOptions.Compiled), "<guid>"),
        (new Regex(@"\b0x[0-9a-fA-F]+\b", RegexOptions.Compiled), "<hex>"),
        (new Regex(@"\b\d+(\.\d+)?\s?(ms|us|µs|ns|s)\b", RegexOptions.Compiled), "<dur>"),
        (new Regex(@"\b\d{1,2}:\d{2}:\d{2}(\.\d+)?\b", RegexOptions.Compiled), "<dur>"),
        (new Regex(@"[A-Za-z]:\\[^\s'""]+", RegexOptions.Compiled), "<path>"),
        (new Regex(@"(?<![\w.])/(?:[\w.\-]+/)+[\w.\-]*", RegexOptions.Compiled), "<path>"),
        (new Regex(@"\b\d+(\.\d+)?\b", RegexOptions.Compiled), "<n>"),
    ];

    public static string Normalize(string message)
    {
        string result = message;
        foreach (var (pattern, replacement) in Rules)
        {
            result = pattern.Replace(result, replacement);
        }

        return result;
    }

    /// <summary>Stable across processes: first 16 hex chars of SHA-256 over the UTF-8 template.</summary>
    public static string TemplateHash(string template)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(template));
        return Convert.ToHexString(digest, 0, 8).ToLowerInvariant();
    }
}
