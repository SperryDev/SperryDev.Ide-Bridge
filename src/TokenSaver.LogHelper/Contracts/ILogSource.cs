using TokenSaver.LogHelper.Models;

namespace TokenSaver.LogHelper.Contracts;

/// <summary>Reads entries from one channel. Cursors are opaque to callers.</summary>
public interface ILogSource
{
    Task<LogReadBatch> ReadAsync(string? cursor, int maxEntries, CancellationToken ct);

    /// <summary>
    /// Reads with a requested structured format. Sources that only produce their own entries ignore the format.
    /// </summary>
    Task<LogReadBatch> ReadAsync(string? cursor, int maxEntries, LogFormatSpec? format, CancellationToken ct)
        => ReadAsync(cursor, maxEntries, ct);
}

/// <summary>Lists and opens channels for one URI scheme.</summary>
public interface ILogSourceProvider
{
    string Scheme { get; }

    Task<IReadOnlyList<LogChannelInfo>> ListChannelsAsync(CancellationToken ct);

    ILogSource Open(ChannelId id);
}
