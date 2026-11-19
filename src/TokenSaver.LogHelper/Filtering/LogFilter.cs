using TokenSaver.LogHelper.Models;

namespace TokenSaver.LogHelper.Filtering;

public sealed record LogFilterResult(
    IReadOnlyList<LogEntry> Kept,
    IReadOnlyList<LogEntry> Dropped,
    string? Error);

/// <summary>
/// Applies a filter. Regexes match against <see cref="LogEntry.Raw"/> so stack-trace content is searchable.
/// Entries with <see cref="LogSeverity.Unknown"/> rank below every level, so any MinLevel drops them.
/// </summary>
public static class LogFilter
{
    public static LogFilterResult Apply(IEnumerable<LogEntry> entries, LogFilterSpec spec, LogHelperOptions options)
    {
        var match = CompileOrFail(spec.Match, options, out string? error);
        if (error is not null)
        {
            return new LogFilterResult(Array.Empty<LogEntry>(), Array.Empty<LogEntry>(), error);
        }

        var exclude = CompileOrFail(spec.Exclude, options, out error);
        if (error is not null)
        {
            return new LogFilterResult(Array.Empty<LogEntry>(), Array.Empty<LogEntry>(), error);
        }

        var kept = new List<LogEntry>();
        var dropped = new List<LogEntry>();

        foreach (LogEntry entry in entries)
        {
            if (Keep(entry, spec, match, exclude))
            {
                kept.Add(entry);
            }
            else
            {
                dropped.Add(entry);
            }
        }

        return new LogFilterResult(kept, dropped, null);
    }

    private static bool Keep(LogEntry entry, LogFilterSpec spec, System.Text.RegularExpressions.Regex? match, System.Text.RegularExpressions.Regex? exclude)
    {
        if (match is not null && !match.IsMatch(entry.Raw))
        {
            return false;
        }

        if (exclude is not null && exclude.IsMatch(entry.Raw))
        {
            return false;
        }

        if (spec.MinLevel is { } min && entry.Severity < min)
        {
            return false;
        }

        if (spec.Category is { Length: > 0 } category
            && (entry.Category is null || !entry.Category.StartsWith(category, StringComparison.Ordinal)))
        {
            return false;
        }

        foreach (FieldTerm term in spec.FieldTerms)
        {
            if (entry.Fields is null
                || !entry.Fields.TryGetValue(term.Key, out string? value)
                || !string.Equals(value, term.Value, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Parses a comma-separated list of <c>key=value</c> terms, for example <c>level=ERROR,service=api</c>.</summary>
    public static bool TryParseFieldTerms(string? text, out IReadOnlyList<FieldTerm> terms, out string? error)
    {
        var parsed = new List<FieldTerm>();
        terms = parsed;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        foreach (string part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int equals = part.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0 || equals == part.Length - 1)
            {
                error = $"Invalid field term '{part}'. Use key=value.";
                return false;
            }

            parsed.Add(new FieldTerm(part[..equals].Trim(), part[(equals + 1)..].Trim()));
        }

        return true;
    }

    private static System.Text.RegularExpressions.Regex? CompileOrFail(string? pattern, LogHelperOptions options, out string? error)
    {
        error = null;
        if (string.IsNullOrEmpty(pattern))
        {
            return null;
        }

        if (!SafeRegex.TryCreate(pattern, options, out var regex, out error))
        {
            return null;
        }

        return regex;
    }
}
