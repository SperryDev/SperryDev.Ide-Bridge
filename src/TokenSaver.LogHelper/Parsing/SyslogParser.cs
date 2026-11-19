using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using TokenSaver.LogHelper.Models;

namespace TokenSaver.LogHelper.Parsing;

/// <summary>
/// Parses one syslog line: RFC 3164 (BSD, no year), RFC 5424, or a journald JSON record. Anything else becomes a
/// plain entry. Severity is the PRI value's low three bits, or journald's PRIORITY. RFC 3164 has no year, so it is
/// taken from <c>now</c>, and a December line read in early January is placed in the previous year. Times without
/// a zone are read as UTC, because the zone is not in the line.
/// </summary>
public static class SyslogParser
{
    private static readonly Regex Rfc3164 = new(
        @"^<(?<pri>\d{1,3})>(?<mon>[A-Z][a-z]{2}) +(?<day>\d{1,2}) (?<time>\d{2}:\d{2}:\d{2}) (?<host>\S+) (?<tag>[^:\[\s]+)(?:\[\d+\])?: ?(?<msg>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Rfc5424 = new(
        @"^<(?<pri>\d{1,3})>1 (?<ts>\S+) (?<host>\S+) (?<app>\S+) (?<procid>\S+) (?<msgid>\S+) (?<sd>-|\[.*?\])(?: (?<msg>.*))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static LogEntry Parse(string line, DateTimeOffset now)
    {
        if (line.StartsWith('{'))
        {
            LogEntry? journal = TryJournal(line);
            if (journal is not null)
            {
                return journal;
            }
        }

        Match m3164 = Rfc3164.Match(line);
        if (m3164.Success)
        {
            DateTimeOffset? stamp = Rfc3164Time(m3164.Groups["mon"].Value, int.Parse(m3164.Groups["day"].Value, CultureInfo.InvariantCulture), m3164.Groups["time"].Value, now);
            return new LogEntry(stamp, SeverityOf(m3164.Groups["pri"].Value), m3164.Groups["tag"].Value, m3164.Groups["msg"].Value, line);
        }

        Match m5424 = Rfc5424.Match(line);
        if (m5424.Success)
        {
            DateTimeOffset.TryParse(m5424.Groups["ts"].Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed);
            string msg = m5424.Groups["msg"].Success ? m5424.Groups["msg"].Value : string.Empty;
            return new LogEntry(parsed, SeverityOf(m5424.Groups["pri"].Value), m5424.Groups["app"].Value, msg, line);
        }

        return new LogEntry(null, LogSeverity.Unknown, null, line, line);
    }

    private static LogEntry? TryJournal(string line)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("MESSAGE", out JsonElement message))
            {
                return null;
            }

            LogSeverity severity = LogSeverity.Information;
            if (root.TryGetProperty("PRIORITY", out JsonElement priority))
            {
                string digits = priority.ValueKind == JsonValueKind.Number ? priority.GetInt32().ToString(CultureInfo.InvariantCulture) : priority.GetString() ?? "6";
                severity = SeverityOf(digits);
            }

            string? category = root.TryGetProperty("SYSLOG_IDENTIFIER", out JsonElement id) ? id.GetString() : null;

            DateTimeOffset? stamp = null;
            if (root.TryGetProperty("__REALTIME_TIMESTAMP", out JsonElement micros) && micros.ValueKind == JsonValueKind.String
                && long.TryParse(micros.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out long us))
            {
                stamp = DateTimeOffset.FromUnixTimeMilliseconds(us / 1000);
            }

            return new LogEntry(stamp, severity, category, message.GetString() ?? string.Empty, line);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static DateTimeOffset? Rfc3164Time(string mon, int day, string time, DateTimeOffset now)
    {
        if (!DateTime.TryParseExact($"{mon} {day} {now.Year} {time}", "MMM d yyyy HH:mm:ss",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime candidate))
        {
            return null;
        }

        // A line dated more than a day in the future must be from the previous year.
        if (candidate > now.UtcDateTime.AddDays(1))
        {
            candidate = candidate.AddYears(-1);
        }

        return new DateTimeOffset(DateTime.SpecifyKind(candidate, DateTimeKind.Utc));
    }

    private static LogSeverity SeverityOf(string pri)
    {
        if (!int.TryParse(pri, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
        {
            return LogSeverity.Information;
        }

        return (value & 7) switch
        {
            0 or 1 or 2 => LogSeverity.Critical,
            3 => LogSeverity.Error,
            4 => LogSeverity.Warning,
            5 or 6 => LogSeverity.Information,
            _ => LogSeverity.Debug,
        };
    }
}
