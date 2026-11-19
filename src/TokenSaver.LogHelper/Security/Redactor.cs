using System.Text.RegularExpressions;
using TokenSaver.LogHelper.Models;

namespace TokenSaver.LogHelper.Security;

/// <summary>
/// Masks secrets before any text leaves the helper. Each match is replaced with a
/// <c>&lt;redacted:kind&gt;</c> marker so the AI still sees that something was present.
/// On by default; <see cref="LogHelperOptions.RedactionEnabled"/> turns it off.
/// </summary>
public static class Redactor
{
    private static readonly (Regex Pattern, string Replacement)[] Rules =
    [
        (new Regex(
            @"(?i)\b(password|passwd|pwd|secret|api[_-]?key|token|access[_-]?key)\s*[=:]\s*[^;\s,'""]+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant),
            "$1=<redacted:secret>"),
        (new Regex(
            @"(?i)\bBearer\s+[A-Za-z0-9._~+/=-]+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant),
            "Bearer <redacted:bearer>"),
        (new Regex(
            @"\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant),
            "<redacted:jwt>"),
        (new Regex(
            @"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}",
            RegexOptions.Compiled | RegexOptions.CultureInvariant),
            "<redacted:email>"),
        (new Regex(
            @"\b(AKIA|ASIA)[0-9A-Z]{16}\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant),
            "<redacted:cloud-key>"),
    ];

    public static string Redact(string text, LogHelperOptions options)
    {
        if (!options.RedactionEnabled)
        {
            return text;
        }

        string result = text;
        foreach (var (pattern, replacement) in Rules)
        {
            result = pattern.Replace(result, replacement);
        }

        return result;
    }
}
