using System.Text;
using TokenSaver.LogHelper.Contracts;
using TokenSaver.LogHelper.Models;
using TokenSaver.LogHelper.Services;
using TokenSaver.LogHelper.Sources;
using Xunit;

namespace TokenSaver.LogHelper.Tests.Services;

public sealed class LogWatcherTests : IDisposable
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private readonly string _root;
    private readonly LogHelperOptions _options;
    private readonly string _path;
    private readonly ChannelId _channel;

    public LogWatcherTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "tsl-watcher-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _options = new LogHelperOptions { AllowedRoots = new[] { _root } };
        _path = Path.Combine(_root, "app.log");
        _channel = ChannelId.Parse("file:" + _path);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    // A short poll interval keeps the tests fast and also covers a missed file event.
    private LogWatcher Watcher() =>
        new(new ILogSourceProvider[] { new FileLogSourceProvider(_options) }, _options, TimeSpan.FromMilliseconds(50));

    private void Append(string text) => File.AppendAllText(_path, text, Utf8);

    // Collects messages until at least `count` have arrived, or fails after the timeout.
    private static async Task<List<string>> TakeAsync(IAsyncEnumerator<LogReadBatch> batches, int count, CancellationToken ct)
    {
        var messages = new List<string>();
        while (messages.Count < count && await batches.MoveNextAsync())
        {
            messages.AddRange(batches.Current.Entries.Select(e => e.Message));
        }

        ct.ThrowIfCancellationRequested();
        return messages;
    }

    [Fact]
    public async Task Yields_ExistingLines_ThenOnlyNewOnes()
    {
        File.WriteAllText(_path, "[INF] one\n[INF] two\n", Utf8);
        using var cts = new CancellationTokenSource(Wait);
        await using IAsyncEnumerator<LogReadBatch> batches = Watcher().WatchAsync(_channel, null, null, cts.Token).GetAsyncEnumerator(cts.Token);

        Assert.Equal(new[] { "one", "two" }, await TakeAsync(batches, 2, cts.Token));

        Append("[INF] three\n");
        Assert.Equal(new[] { "three" }, await TakeAsync(batches, 1, cts.Token));

        Append("[INF] four\n[INF] five\n");
        Assert.Equal(new[] { "four", "five" }, await TakeAsync(batches, 2, cts.Token));
    }

    [Fact]
    public async Task ResumesFromACursor_WithoutRepeats()
    {
        File.WriteAllText(_path, "[INF] one\n", Utf8);
        var first = await new FileLogSource(_path, _options).ReadAsync(null, 100, CancellationToken.None);
        Append("[INF] two\n");

        using var cts = new CancellationTokenSource(Wait);
        await using IAsyncEnumerator<LogReadBatch> batches = Watcher().WatchAsync(_channel, first.NextCursor, null, cts.Token).GetAsyncEnumerator(cts.Token);

        Assert.Equal(new[] { "two" }, await TakeAsync(batches, 1, cts.Token));
    }

    [Fact]
    public async Task FollowsARollingLog_WithoutLossOrRepeats()
    {
        File.WriteAllText(_path, "[INF] one\n", Utf8);
        using var cts = new CancellationTokenSource(Wait);
        await using IAsyncEnumerator<LogReadBatch> batches = Watcher().WatchAsync(_channel, null, null, cts.Token).GetAsyncEnumerator(cts.Token);
        Assert.Equal(new[] { "one" }, await TakeAsync(batches, 1, cts.Token));

        // Written, then rolled before the watcher has necessarily read it.
        Append("[INF] two\n");
        File.Move(_path, Path.Combine(_root, "app.log.1"));
        File.WriteAllText(_path, "[INF] three\n", Utf8);
        Assert.Equal(new[] { "two", "three" }, await TakeAsync(batches, 2, cts.Token));

        // The new file keeps being followed, and a second roll works too.
        Append("[INF] four\n");
        File.Move(_path, Path.Combine(_root, "app.log.2"));
        File.WriteAllText(_path, "[INF] five\n", Utf8);
        Assert.Equal(new[] { "four", "five" }, await TakeAsync(batches, 2, cts.Token));
    }

    [Fact]
    public async Task FileCreatedAfterTheWatchStarts_IsRead()
    {
        using var cts = new CancellationTokenSource(Wait);
        await using IAsyncEnumerator<LogReadBatch> batches = Watcher().WatchAsync(_channel, null, null, cts.Token).GetAsyncEnumerator(cts.Token);

        Task<List<string>> pending = TakeAsync(batches, 1, cts.Token);
        File.WriteAllText(_path, "[INF] hello\n", Utf8);

        Assert.Equal(new[] { "hello" }, await pending);
    }

    [Fact]
    public async Task Cancel_EndsTheWatch()
    {
        File.WriteAllText(_path, "[INF] one\n", Utf8);
        using var cts = new CancellationTokenSource();
        var seen = new List<string>();

        Task watch = Task.Run(async () =>
        {
            await foreach (LogReadBatch batch in Watcher().WatchAsync(_channel, null, null, cts.Token))
            {
                seen.AddRange(batch.Entries.Select(e => e.Message));
            }
        });

        await Task.Delay(300);
        await cts.CancelAsync();

        await watch.WaitAsync(Wait);
        Assert.Equal(new[] { "one" }, seen);
    }

    [Fact]
    public async Task PathOutsideAllowedRoots_Throws()
    {
        string outside = Path.Combine(Path.GetTempPath(), "tsl-outside-" + Guid.NewGuid().ToString("N") + ".log");
        ChannelId channel = ChannelId.Parse("file:" + outside);
        using var cts = new CancellationTokenSource(Wait);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
        {
            await foreach (LogReadBatch unused in Watcher().WatchAsync(channel, null, null, cts.Token))
            {
            }
        });
    }

    [Fact]
    public async Task UnknownScheme_Throws()
    {
        ChannelId channel = ChannelId.Parse("eventlog:Application");
        using var cts = new CancellationTokenSource(Wait);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (LogReadBatch unused in Watcher().WatchAsync(channel, null, null, cts.Token))
            {
            }
        });
    }
}
