using System.Text;
using TokenSaver.LogHelper.Contracts;
using TokenSaver.LogHelper.Models;
using TokenSaver.LogHelper.Services;
using TokenSaver.LogHelper.Sources;
using Xunit;

namespace TokenSaver.LogHelper.Tests.Services;

public sealed class LogServiceWatchTests : IDisposable
{
    private static readonly LogRequestContext Context = new("test");

    private readonly string _root;
    private readonly LogHelperOptions _options;
    private readonly string _path;
    private readonly ChannelId _channel;
    private readonly List<TimeSpan> _delays = new();

    public LogServiceWatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "tsl-watch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _options = new LogHelperOptions { AllowedRoots = new[] { _root } };
        _path = Path.Combine(_root, "live.log");
        File.WriteAllText(_path, "[INF] startup\n", new UTF8Encoding(false));
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

    private LogService Service(Func<TimeSpan, CancellationToken, Task> delay) =>
        new(new ILogSourceProvider[] { new FileLogSourceProvider(_options) }, _options, delay);

    private Task RecordingDelay(TimeSpan d, CancellationToken ct)
    {
        _delays.Add(d);
        return Task.CompletedTask;
    }

    private void Append(string text) => File.AppendAllText(_path, text, new UTF8Encoding(false));

    private async Task<string> StartCursorAsync(LogService service)
    {
        var first = await service.QueryAsync(new LogQueryRequest(_channel), Context, CancellationToken.None);
        return first.NextCursor!;
    }

    [Fact]
    public async Task Wait_NewEntryArrives_ReturnsEarlyWithNextCursor()
    {
        var service = Service(async (d, ct) =>
        {
            _delays.Add(d);
            Append("[ERR] payment gateway timeout\n");
            await Task.CompletedTask;
        });
        string cursor = await StartCursorAsync(service);

        var result = await service.QueryAsync(
            new LogQueryRequest(_channel, Since: cursor, WaitSeconds: 10), Context, CancellationToken.None);

        Assert.True(result.Ok, result.Error);
        Assert.Contains("payment gateway timeout", result.Text, StringComparison.Ordinal);
        Assert.NotNull(result.NextCursor);
        Assert.Single(_delays);
    }

    [Fact]
    public async Task Wait_NothingNew_ReturnsAtTimeout_SameCursor()
    {
        var service = Service(RecordingDelay);
        string cursor = await StartCursorAsync(service);

        var result = await service.QueryAsync(
            new LogQueryRequest(_channel, Since: cursor, WaitSeconds: 3), Context, CancellationToken.None);

        Assert.True(result.Ok, result.Error);
        Assert.Empty(result.Text);
        Assert.Equal(3, _delays.Count);
        Assert.Equal(cursor, result.NextCursor);
    }

    [Fact]
    public async Task Wait_Over25_ClampedTo25()
    {
        var service = Service(RecordingDelay);
        string cursor = await StartCursorAsync(service);

        await service.QueryAsync(
            new LogQueryRequest(_channel, Since: cursor, WaitSeconds: 999), Context, CancellationToken.None);

        Assert.Equal(25, _delays.Count);
    }

    [Fact]
    public async Task Wait_Cancelled_ReturnsPartial()
    {
        using var cts = new CancellationTokenSource();
        var service = Service((d, ct) =>
        {
            cts.Cancel();
            throw new OperationCanceledException(ct);
        });
        string cursor = await StartCursorAsync(service);

        var result = await service.QueryAsync(
            new LogQueryRequest(_channel, Since: cursor, WaitSeconds: 10), Context, cts.Token);

        Assert.True(result.Ok, result.Error);
        Assert.Empty(result.Text);
    }

    [Fact]
    public async Task Wait_DuplicateTemplates_ReportedOnce()
    {
        var service = Service(async (d, ct) =>
        {
            Append("[ERR] dup\n[ERR] dup\n");
            await Task.CompletedTask;
        });
        string cursor = await StartCursorAsync(service);

        var result = await service.QueryAsync(
            new LogQueryRequest(_channel, Since: cursor, WaitSeconds: 5), Context, CancellationToken.None);

        var lines = result.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines, l => l.Contains("dup", StringComparison.Ordinal));
        Assert.Contains("x2", result.Text, StringComparison.Ordinal);
    }
}
