using System.Text.Json;
using System.Text.RegularExpressions;
using TokenSaver.LogHelper.Contracts;
using TokenSaver.LogHelper.Models;
using TokenSaver.LogHelper.Parsing;

namespace TokenSaver.LogHelper.Sources;

/// <summary>
/// Serves <c>syslog:</c> channels on Linux. <c>syslog:journal/&lt;unit&gt;</c> reads journalctl JSON (or
/// <c>journal/all</c> for every unit) from a cursor. <c>syslog:file/&lt;name&gt;</c> reads <c>/var/log/&lt;name&gt;</c>
/// through <see cref="FileLogSource"/>. Journal channels are not listed on Windows; on Windows the WSL host is the source.
/// Unit and file names are validated before anything runs.
/// </summary>
public sealed class SyslogSourceProvider : ILogSourceProvider
{
    private static readonly Regex SafeName = new(@"^[A-Za-z0-9@._-]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IProcessRunner _runner;
    private readonly Func<DateTimeOffset> _now;
    private readonly bool _isWindows;

    public SyslogSourceProvider(IProcessRunner runner, Func<DateTimeOffset> now, bool? isWindows = null)
    {
        _runner = runner;
        _now = now;
        _isWindows = isWindows ?? OperatingSystem.IsWindows();
    }

    public string Scheme => "syslog";

    public Task<IReadOnlyList<LogChannelInfo>> ListChannelsAsync(CancellationToken ct)
    {
        IReadOnlyList<LogChannelInfo> channels = _isWindows
            ? Array.Empty<LogChannelInfo>()
            : new[] { new LogChannelInfo(ChannelId.Parse("syslog:journal/all"), "journalctl, all units", null) };
        return Task.FromResult(channels);
    }

    public ILogSource Open(ChannelId id)
    {
        if (id.Name.StartsWith("journal/", StringComparison.Ordinal))
        {
            return new JournalSource(_runner, _now, id.Name["journal/".Length..]);
        }

        if (id.Name.StartsWith("file/", StringComparison.Ordinal))
        {
            return new SyslogFileSource(id.Name["file/".Length..]);
        }

        throw new ArgumentException($"Unknown syslog channel '{id.Name}'. Use journal/<unit> or file/<name>.");
    }

    internal static bool IsSafeName(string name) =>
        !string.IsNullOrEmpty(name) && name != "." && name != ".." && SafeName.IsMatch(name);

    private sealed class SyslogFileSource : ILogSource
    {
        private readonly string _name;

        public SyslogFileSource(string name)
        {
            _name = name;
        }

        public Task<LogReadBatch> ReadAsync(string? cursor, int maxEntries, CancellationToken ct)
        {
            if (!IsSafeName(_name))
            {
                throw new ArgumentException($"Invalid syslog file name '{_name}'.");
            }

            var options = new LogHelperOptions { AllowedRoots = new[] { "/var/log" } };
            return new FileLogSource(Path.Combine("/var/log", _name), options).ReadAsync(cursor, maxEntries, ct);
        }
    }

    private sealed class JournalSource : ILogSource
    {
        private readonly IProcessRunner _runner;
        private readonly Func<DateTimeOffset> _now;
        private readonly string _unit;

        public JournalSource(IProcessRunner runner, Func<DateTimeOffset> now, string unit)
        {
            _runner = runner;
            _now = now;
            _unit = unit;
        }

        public async Task<LogReadBatch> ReadAsync(string? cursor, int maxEntries, CancellationToken ct)
        {
            var args = new List<string> { "-o", "json", "--no-pager" };
            if (_unit != "all")
            {
                if (!IsSafeName(_unit))
                {
                    throw new ArgumentException($"Invalid journal unit name '{_unit}'.");
                }

                args.Add("-u");
                args.Add(_unit);
            }

            string? lastCursor = null;
            if (LogCursor.TryDecode(cursor, out string payload))
            {
                string? journalCursor = JsonSerializer.Deserialize<JournalCursor>(payload)?.Cursor;
                if (journalCursor is not null)
                {
                    args.Add("--after-cursor=" + journalCursor);
                    lastCursor = journalCursor;
                }
            }
            else
            {
                args.Add("-n");
                args.Add(maxEntries.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            string stdout = await _runner.RunAsync("journalctl", args, ct);

            var entries = new List<LogEntry>();
            foreach (string line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (entries.Count >= maxEntries)
                {
                    break;
                }

                entries.Add(SyslogParser.Parse(line, _now()));
                lastCursor = CursorOf(line) ?? lastCursor;
            }

            string nextCursor = lastCursor is null
                ? cursor ?? string.Empty
                : LogCursor.Encode(JsonSerializer.Serialize(new JournalCursor(lastCursor)));
            return new LogReadBatch(entries, nextCursor, Reset: false, Dropped: 0);
        }

        private static string? CursorOf(string line)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(line);
                return document.RootElement.TryGetProperty("__CURSOR", out JsonElement c) ? c.GetString() : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    private sealed record JournalCursor(string Cursor);
}
