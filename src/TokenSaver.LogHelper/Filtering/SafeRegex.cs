using System.Text.RegularExpressions;
using TokenSaver.LogHelper.Models;

namespace TokenSaver.LogHelper.Filtering;

/// <summary>
/// Builds regexes for untrusted input. Always uses <see cref="RegexOptions.NonBacktracking"/>, so
/// matching is linear-time; constructs it cannot run (lookaround, backreferences) are rejected
/// with a message that names the construct.
/// </summary>
public static class SafeRegex
{
    public static bool TryCreate(string pattern, LogHelperOptions options, out Regex? regex, out string? error)
    {
        regex = null;
        error = null;

        if (pattern.Length > options.MaxRegexLength)
        {
            error = $"Pattern is too long ({pattern.Length} chars; max {options.MaxRegexLength}).";
            return false;
        }

        try
        {
            regex = new Regex(pattern, RegexOptions.NonBacktracking | RegexOptions.CultureInvariant);
            return true;
        }
        catch (NotSupportedException ex)
        {
            error = ClassifyUnsupported(pattern, ex.Message);
            return false;
        }
        catch (ArgumentException ex)
        {
            error = $"Invalid regex: {ex.Message}";
            return false;
        }
    }

    private static string ClassifyUnsupported(string pattern, string message)
    {
        if (pattern.Contains("(?=", StringComparison.Ordinal)
            || pattern.Contains("(?!", StringComparison.Ordinal)
            || pattern.Contains("(?<=", StringComparison.Ordinal)
            || pattern.Contains("(?<!", StringComparison.Ordinal))
        {
            return "Unsupported: lookaround assertions are not allowed. Remove the (?=, (?!, (?<= or (?<! construct.";
        }

        return $"Unsupported: backreferences and other backtracking-only constructs are not allowed ({message}).";
    }
}
