using System.Text.Json;
using TokenSaver.LogHelper.Contracts;
using TokenSaver.LogHelper.Models;
using TokenSaver.LogHelper.Parsing;

namespace TokenSaver.MCP.Services.LogSources;

/// <summary>
/// Serves <c>vsout:&lt;pane&gt;</c> channels: Visual Studio output panes, read from the bridge by character offset with an
/// anchor. A bridge older than the ranged read returns only the tail; that is read once and not repeated.
/// </summary>
internal sealed class VsOutputLogSourceProvider : ILogSourceProvider
{
    private readonly IIdeLogBridge _bridge;

    public VsOutputLogSourceProvider(IIdeLogBridge bridge)
    {
        _bridge = bridge;
    }

    public string Scheme => "vsout";

    public async Task<IReadOnlyList<LogChannelInfo>> ListChannelsAsync(CancellationToken ct)
    {
        string reply = await _bridge.CallAsync("listOutputPanes", new { }, null, ct);
        JsonElement result = IdeReply.ResultOrThrow(reply);

        var channels = new List<LogChannelInfo>();
        if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("panes", out JsonElement panes))
        {
            foreach (JsonElement pane in panes.EnumerateArray())
            {
                string name = pane.GetString() ?? string.Empty;
                channels.Add(new LogChannelInfo(ChannelId.Parse("vsout:" + name), name, null));
            }
        }

        return channels;
    }

    public ILogSource Open(ChannelId id) => new VsOutputLogSource(_bridge, id.Name);
}

internal sealed record VsOutputCursor(string Pane, int Offset, string? Anchor, bool Legacy);

internal sealed class VsOutputLogSource : ILogSource
{
    private const int MaxChars = 20000;

    private readonly IIdeLogBridge _bridge;
    private readonly string _pane;

    public VsOutputLogSource(IIdeLogBridge bridge, string pane)
    {
        _bridge = bridge;
        _pane = pane;
    }

    public async Task<LogReadBatch> ReadAsync(string? cursor, int maxEntries, CancellationToken ct)
    {
        VsOutputCursor? state = DecodeOrNull(cursor);

        if (state is { Legacy: true })
        {
            return Empty(state);
        }

        int maxLines = Math.Clamp(maxEntries, 1, 2000);
        string reply = await _bridge.CallAsync(
            "getOutput",
            new
            {
                pane = _pane,
                fromOffset = state?.Offset ?? 0,
                anchor = state?.Anchor,
                maxChars = MaxChars,
                maxLines,
            },
            null,
            ct);
        JsonElement result = IdeReply.ResultOrThrow(reply);

        if (result.TryGetProperty("text", out JsonElement textElement))
        {
            string text = textElement.GetString() ?? string.Empty;
            int nextOffset = result.TryGetProperty("nextOffset", out JsonElement n) ? n.GetInt32() : 0;
            string? anchor = result.TryGetProperty("anchor", out JsonElement a) ? a.GetString() : null;
            bool reset = result.TryGetProperty("reset", out JsonElement r) && r.GetBoolean();

            var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
            if (lines.Count > 0 && lines[^1].Length == 0)
            {
                lines.RemoveAt(lines.Count - 1);
            }

            var entries = new LogParser(new LogHelperOptions()).Parse(lines);
            var next = new VsOutputCursor(_pane, nextOffset, anchor, Legacy: false);
            return new LogReadBatch(entries, EncodeCursor(next), reset, Dropped: 0);
        }

        // Older bridge: the reply has only the tail lines and no cursor. Read it on the first call only.
        var legacyLines = result.TryGetProperty("lines", out JsonElement l)
            ? l.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToList()
            : new List<string>();
        var legacyEntries = new LogParser(new LogHelperOptions()).Parse(legacyLines);
        return new LogReadBatch(legacyEntries, EncodeCursor(new VsOutputCursor(_pane, 0, string.Empty, Legacy: true)), Reset: false, Dropped: 0);
    }

    // A cursor that does not decode is treated as no cursor, so the read starts from the beginning.
    private static VsOutputCursor? DecodeOrNull(string? cursor)
    {
        if (!LogCursor.TryDecode(cursor, out string payload))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<VsOutputCursor>(payload);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private LogReadBatch Empty(VsOutputCursor state) =>
        new(Array.Empty<LogEntry>(), EncodeCursor(state), Reset: false, Dropped: 0);

    private static string EncodeCursor(VsOutputCursor cursor) =>
        LogCursor.Encode(JsonSerializer.Serialize(cursor));
}
