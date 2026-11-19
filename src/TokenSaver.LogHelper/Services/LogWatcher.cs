using System.Runtime.CompilerServices;
using TokenSaver.LogHelper.Contracts;
using TokenSaver.LogHelper.Models;
using TokenSaver.LogHelper.Security;
using TokenSaver.LogHelper.Sources;

namespace TokenSaver.LogHelper.Services;

/// <summary>
/// Follows a channel and yields only what is new since a cursor, as it arrives. It reads through the channel's own
/// source, so everything a source guarantees holds here too: complete lines only, and a rolling file followed across
/// a roll without loss or repeats (<see cref="LogReadBatch.Rotated"/>). A watched file wakes the loop through file
/// system events on its folder; a poll interval backs that up, because events can be missed (network drives,
/// buffer overflows) and other channels have none.
/// </summary>
public sealed class LogWatcher
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(1);

    private readonly IReadOnlyList<ILogSourceProvider> _providers;
    private readonly LogHelperOptions _options;
    private readonly TimeSpan _pollInterval;

    public LogWatcher(IEnumerable<ILogSourceProvider> providers, LogHelperOptions options, TimeSpan? pollInterval = null)
    {
        _providers = providers.ToList();
        _options = options;
        _pollInterval = pollInterval is { } p && p > TimeSpan.Zero ? p : DefaultPollInterval;
    }

    /// <summary>
    /// Yields a batch each time new entries (or a rotation or reset) are found, until <paramref name="ct"/> is cancelled.
    /// Pass the previous batch's <see cref="LogReadBatch.NextCursor"/> to resume without gaps or repeats; null starts
    /// at the beginning of the channel. An unreadable channel throws on the first read (for example a path outside
    /// the allowed roots); a read that fails later with an I/O error is retried at the next poll.
    /// </summary>
    public async IAsyncEnumerable<LogReadBatch> WatchAsync(
        ChannelId channel,
        string? cursor,
        LogFormatSpec? format,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ILogSourceProvider provider = _providers.FirstOrDefault(p =>
                string.Equals(p.Scheme, channel.Scheme, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"No log provider is registered for scheme '{channel.Scheme}'.");

        ILogSource source = provider.Open(channel);
        using var signal = new ChangeSignal(WatchedFile(channel));

        string? current = cursor;
        bool first = true;
        while (!ct.IsCancellationRequested)
        {
            LogReadBatch? batch = null;
            bool cancelled = false;
            try
            {
                batch = await source.ReadAsync(current, int.MaxValue, format, ct);
            }
            catch (IOException) when (!first)
            {
                // Locked or moved between the check and the read: the next poll retries from the same cursor.
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                cancelled = true;
            }

            if (cancelled)
            {
                // Cancelling ends the watch quietly, the same as cancelling while it waits.
                yield break;
            }

            first = false;
            if (batch is not null)
            {
                current = batch.NextCursor;
                if (batch.Entries.Count > 0 || batch.Reset || batch.Rotated)
                {
                    yield return batch;
                }

                if (batch.Entries.Count > 0)
                {
                    // A large file is read in chunks: keep going until a read comes back empty.
                    continue;
                }
            }

            await signal.WaitAsync(_pollInterval, ct);
        }
    }

    // The file for a file channel when it is readable under an allowed root, else null (polling only).
    private string? WatchedFile(ChannelId channel) =>
        string.Equals(channel.Scheme, "file", StringComparison.OrdinalIgnoreCase)
            && PathGuard.TryResolve(channel.Name, _options.AllowedRoots, out string? canonical, out _)
            ? canonical
            : null;

    /// <summary>Wakes the watch loop when something in the log's folder changes. With no file it only waits out the interval.</summary>
    private sealed class ChangeSignal : IDisposable
    {
        private readonly SemaphoreSlim _changed = new(0, 1);
        private readonly FileSystemWatcher? _watcher;
        private readonly string _stem = string.Empty;

        public ChangeSignal(string? file)
        {
            string? directory = file is null ? null : Path.GetDirectoryName(file);
            if (file is null || directory is null || !Directory.Exists(directory))
            {
                return;
            }

            _stem = RolledFiles.StemOf(Path.GetFileName(file));
            FileSystemWatcher? watcher = null;
            try
            {
                watcher = new FileSystemWatcher(directory)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                    InternalBufferSize = 64 * 1024,
                };
                watcher.Changed += OnEvent;
                watcher.Created += OnEvent;
                watcher.Deleted += OnEvent;
                watcher.Renamed += (_, e) => OnEvent(null, e);
                watcher.Error += (_, _) => Signal();
                watcher.EnableRaisingEvents = true;
                _watcher = watcher;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or PlatformNotSupportedException)
            {
                // No events here: polling still works. The watcher is null when its constructor was what threw.
#pragma warning disable CA1508 // The analyzer does not see that the constructor can throw before the assignment.
                watcher?.Dispose();
#pragma warning restore CA1508
            }
        }

        public async Task WaitAsync(TimeSpan timeout, CancellationToken ct)
        {
            try
            {
                await _changed.WaitAsync(timeout, ct);
            }
            catch (OperationCanceledException)
            {
                // Cancellation ends the loop through its own check.
            }
        }

        public void Dispose()
        {
            _watcher?.Dispose();
            _changed.Dispose();
        }

        private void OnEvent(object? sender, FileSystemEventArgs e)
        {
            // Only the log and its rolled siblings matter; other files in the folder are noise.
            string name = e.Name ?? string.Empty;
            if (name.StartsWith(_stem, StringComparison.OrdinalIgnoreCase)
                || (e is RenamedEventArgs renamed && (renamed.OldName ?? string.Empty).StartsWith(_stem, StringComparison.OrdinalIgnoreCase)))
            {
                Signal();
            }
        }

        private void Signal()
        {
            try
            {
                if (_changed.CurrentCount == 0)
                {
                    _changed.Release();
                }
            }
            catch (Exception ex) when (ex is SemaphoreFullException or ObjectDisposedException)
            {
                // Already signalled, or the watch ended.
            }
        }
    }
}
