using TokenSaver.LogHelper.Models;

namespace TokenSaver.LogHelper.Contracts;

/// <summary>
/// Transport-agnostic facade. Every host (in-process MCP, a future remote host, a CLI) calls this.
/// Takes and returns plain DTOs only.
/// </summary>
public interface ILogService
{
    Task<IReadOnlyList<LogChannelInfo>> ListChannelsAsync(string? nameFilter, LogRequestContext context, CancellationToken ct);

    Task<LogQueryResult> QueryAsync(LogQueryRequest request, LogRequestContext context, CancellationToken ct);

    Task<LogQueryResult> MarkRunAsync(ChannelId channel, LogRequestContext context, CancellationToken ct);

    Task<LogQueryResult> SinkCompareAsync(ChannelId appChannel, ChannelId sinkChannel, int maxTokens, LogRequestContext context, CancellationToken ct);
}
