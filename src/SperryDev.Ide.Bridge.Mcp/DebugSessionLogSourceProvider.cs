using System.Text.Json;
using TokenSaver.LogHelper.Contracts;
using TokenSaver.LogHelper.Models;
using TokenSaver.LogHelper.Parsing;

namespace TokenSaver.MCP.Services.LogSources;

/// <summary>
/// Serves <c>debug:&lt;session&gt;</c> channels: the program output a VS Code debug session produced, read a page at
/// a time from the bridge by sequence number. Dropped lines (evicted before the caller read them) are reported.
/// </summary>
internal sealed class DebugSessionLogSourceProvider : ILogSourceProvider
{
    private readonly IIdeLogBridge _bridge;

    public DebugSessionLogSourceProvider(IIdeLogBridge bridge)
    {
        _bridge = bridge;
    }

    public string Scheme => "debug";

    public async Task<IReadOnlyList<LogChannelInfo>> ListChannelsAsync(CancellationToken ct)
    {
        string reply = await _bridge.CallAsync("listDebugSessions", new { }, null, ct);
        JsonElement result = IdeReply.ResultOrThrow(reply);

        var channels = new List<LogChannelInfo>();
        if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("sessions", out JsonElement sessions))
        {
            foreach (JsonElement session in sessions.EnumerateArray())
            {
                string id = session.GetProperty("id").GetString() ?? string.Empty;
                bool ended = session.TryGetProperty("ended", out JsonElement e) && e.GetBoolean();
                channels.Add(new LogChannelInfo(
                    ChannelId.Parse("debug:" + id),
                    ended ? "debug session (ended)" : "debug session",
                    null));
            }
        }

        return channels;
    }

    public ILogSource Open(ChannelId id) => new DebugSessionLogSource(_bridge, id.Name);
}

internal sealed class DebugSessionLogSource : ILogSource
{
    private const int MaxPage = 2000;

    private readonly IIdeLogBridge _bridge;
    private readonly string _session;

    public DebugSessionLogSource(IIdeLogBridge bridge, string session)
    {
        _bridge = bridge;
        _session = session;
    }

    public async Task<LogReadBatch> ReadAsync(string? cursor, int maxEntries, CancellationToken ct)
    {
        long fromSeq = DecodeSeq(cursor);

        int maxLines = Math.Clamp(maxEntries, 1, MaxPage);
        string reply = await _bridge.CallAsync(
            "getDebugOutput",
            new { session = _session, fromSeq, maxLines },
            null,
            ct);
        JsonElement result = IdeReply.ResultOrThrow(reply);

        var lines = result.GetProperty("lines").EnumerateArray()
            .Select(l => l.GetString() ?? string.Empty)
            .ToList();
        long nextSeq = result.GetProperty("nextSeq").GetInt64();
        int dropped = result.TryGetProperty("dropped", out JsonElement d) ? d.GetInt32() : 0;

        var entries = new LogParser(new LogHelperOptions()).Parse(lines);
        string nextCursor = LogCursor.Encode(JsonSerializer.Serialize(new DebugCursor(nextSeq)));
        return new LogReadBatch(entries, nextCursor, Reset: false, Dropped: dropped);
    }

    // A cursor that does not decode is treated as no cursor, so the read starts from the first line.
    private static long DecodeSeq(string? cursor)
    {
        if (!LogCursor.TryDecode(cursor, out string payload))
        {
            return 1;
        }

        try
        {
            return JsonSerializer.Deserialize<DebugCursor>(payload)?.Seq ?? 1;
        }
        catch (JsonException)
        {
            return 1;
        }
    }

    private sealed record DebugCursor(long Seq);
}
