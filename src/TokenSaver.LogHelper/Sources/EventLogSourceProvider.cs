using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TokenSaver.LogHelper.Contracts;
using TokenSaver.LogHelper.Models;

namespace TokenSaver.LogHelper.Sources;

/// <summary>
/// Serves <c>eventlog:&lt;log&gt;</c> channels on Windows, through <c>wevtutil</c>. Only logs in
/// <see cref="LogHelperOptions.EventLogAllowList"/> may be read; Security is not in the default list. Reads
/// resume from the last record id. Access-denied errors say which group grants access. The host registers this
/// provider only on Windows.
/// </summary>
public sealed class EventLogSourceProvider : ILogSourceProvider
{
    private static readonly Regex EventElement = new(@"<Event xmlns=.*?</Event>", RegexOptions.Compiled | RegexOptions.Singleline);

    private readonly IProcessRunner _runner;
    private readonly LogHelperOptions _options;

    public EventLogSourceProvider(IProcessRunner runner, LogHelperOptions options)
    {
        _runner = runner;
        _options = options;
    }

    public string Scheme => "eventlog";

    public Task<IReadOnlyList<LogChannelInfo>> ListChannelsAsync(CancellationToken ct)
    {
        IReadOnlyList<LogChannelInfo> channels = _options.EventLogAllowList
            .Select(name => new LogChannelInfo(ChannelId.Parse("eventlog:" + name), "Windows event log", null))
            .ToList();
        return Task.FromResult(channels);
    }

    public ILogSource Open(ChannelId id) => new EventLogSource(_runner, _options, id.Name);

    private sealed class EventLogSource : ILogSource
    {
        private readonly IProcessRunner _runner;
        private readonly LogHelperOptions _options;
        private readonly string _log;

        public EventLogSource(IProcessRunner runner, LogHelperOptions options, string log)
        {
            _runner = runner;
            _options = options;
            _log = log;
        }

        public async Task<LogReadBatch> ReadAsync(string? cursor, int maxEntries, CancellationToken ct)
        {
            string? allowed = _options.EventLogAllowList.FirstOrDefault(n => string.Equals(n, _log, StringComparison.OrdinalIgnoreCase));
            if (allowed is null)
            {
                throw new UnauthorizedAccessException(
                    $"Event log '{_log}' is not in the allow-list. Add it to LogHelperOptions.EventLogAllowList to read it.");
            }

            long? afterRecord = ReadRecordId(cursor);
            string count = maxEntries.ToString(CultureInfo.InvariantCulture);
            var args = afterRecord is long after
                ? new List<string> { "qe", allowed, "/c:" + count, "/rd:false", "/f:xml", "/q:*[System[EventRecordID>" + after.ToString(CultureInfo.InvariantCulture) + "]]" }
                : new List<string> { "qe", allowed, "/c:" + count, "/rd:true", "/f:xml" };

            string stdout;
            try
            {
                stdout = await _runner.RunAsync("wevtutil", args, ct);
            }
            catch (IOException ex) when (ex.Message.Contains("Access is denied", StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(
                    $"Event Log access denied for '{allowed}'. Add this user to the Event Log Readers group, or run elevated.", ex);
            }

            var entries = new List<(long Record, LogEntry Entry)>();
            foreach (Match match in EventElement.Matches(stdout))
            {
                entries.Add(ParseEvent(match.Value, allowed));
            }

            entries.Sort((a, b) => a.Record.CompareTo(b.Record));
            long newest = afterRecord ?? 0;
            foreach ((long record, _) in entries)
            {
                newest = Math.Max(newest, record);
            }

            string nextCursor = LogCursor.Encode(JsonSerializer.Serialize(new { record = newest, log = allowed }));
            return new LogReadBatch(entries.Select(e => e.Entry).ToList(), nextCursor, Reset: false, Dropped: 0);
        }

        private static long? ReadRecordId(string? cursor)
        {
            if (!LogCursor.TryDecode(cursor, out string payload))
            {
                return null;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(payload);
                return document.RootElement.TryGetProperty("record", out JsonElement r) ? r.GetInt64() : null;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
            {
                return null;
            }
        }

        private static (long Record, LogEntry Entry) ParseEvent(string xml, string log)
        {
            XElement root = XElement.Parse(xml);
            XNamespace ns = root.Name.Namespace;
            XElement system = root.Element(ns + "System")!;

            long record = long.TryParse(system.Element(ns + "EventRecordID")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long r) ? r : 0;
            string provider = system.Element(ns + "Provider")?.Attribute("Name")?.Value ?? string.Empty;
            string eventId = system.Element(ns + "EventID")?.Value ?? string.Empty;
            DateTimeOffset? time = DateTimeOffset.TryParse(
                system.Element(ns + "TimeCreated")?.Attribute("SystemTime")?.Value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out DateTimeOffset parsed)
                ? parsed
                : null;

            string message = root.Element(ns + "RenderingInfo")?.Element(ns + "Message")?.Value
                ?? string.Join(" ", (root.Element(ns + "EventData")?.Elements() ?? Enumerable.Empty<XElement>()).Select(d => d.Value));

            string raw = $"[{log} {eventId}] {provider}: {message}";
            var entry = new LogEntry(time, LevelOf(system.Element(ns + "Level")?.Value), provider, message.Trim(), raw);
            return (record, entry);
        }

        private static LogSeverity LevelOf(string? level) => level switch
        {
            "1" => LogSeverity.Critical,
            "2" => LogSeverity.Error,
            "3" => LogSeverity.Warning,
            "4" or "0" => LogSeverity.Information,
            "5" => LogSeverity.Debug,
            _ => LogSeverity.Unknown,
        };
    }
}
