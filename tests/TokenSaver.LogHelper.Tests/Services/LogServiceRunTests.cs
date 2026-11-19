using TokenSaver.LogHelper.Contracts;
using TokenSaver.LogHelper.Models;
using TokenSaver.LogHelper.Services;
using TokenSaver.LogHelper.Sources;
using Xunit;

namespace TokenSaver.LogHelper.Tests.Services;

public sealed class LogServiceRunTests : IDisposable
{
    private static readonly LogRequestContext Context = new("test");

    private readonly string _root;
    private readonly string _state;
    private readonly LogHelperOptions _options;
    private readonly string _path;
    private readonly ChannelId _channel;

    public LogServiceRunTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "tsl-runs-" + Guid.NewGuid().ToString("N"));
        _state = Path.Combine(_root, ".tokensaver", "logs");
        Directory.CreateDirectory(_root);
        _path = Path.Combine(_root, "app.log");
        _options = new LogHelperOptions { AllowedRoots = new[] { _root }, StateDirectory = _state };
        _channel = ChannelId.Parse("file:" + _path);
        File.WriteAllText(_path, "[INF] startup complete\n[ERR] db timeout\n", new System.Text.UTF8Encoding(false));
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

    private LogService Service() =>
        new(new ILogSourceProvider[] { new FileLogSourceProvider(_options) }, _options);

    private void Append(string text) =>
        File.AppendAllText(_path, text, new System.Text.UTF8Encoding(false));

    [Fact]
    public async Task MarkRun_ThenQuerySinceLastRun_ShowsOnlyNewAndChanged()
    {
        var service = Service();
        var mark = await service.MarkRunAsync(_channel, Context, CancellationToken.None);
        Assert.True(mark.Ok, mark.Error);

        Append("[ERR] disk full\n[ERR] db timeout\n");

        var result = await service.QueryAsync(
            new LogQueryRequest(_channel, Since: "lastRun"), Context, CancellationToken.None);

        Assert.True(result.Ok, result.Error);
        Assert.Contains("disk full", result.Text, StringComparison.Ordinal);
        Assert.Contains("db timeout", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("startup complete", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SinceUnknownRunId_ReturnsError()
    {
        var result = await Service().QueryAsync(
            new LogQueryRequest(_channel, Since: "run-deadbeef0000"), Context, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Contains("unknown run", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SinceLastRun_NoPriorRun_SaysNoBaseline()
    {
        var result = await Service().QueryAsync(
            new LogQueryRequest(_channel, Since: "lastRun"), Context, CancellationToken.None);

        Assert.True(result.Ok, result.Error);
        Assert.Contains("no baseline", result.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SinceRunId_DiffsAgainstThatRunOnly()
    {
        var service = Service();
        var first = await service.MarkRunAsync(_channel, Context, CancellationToken.None);
        string runId = first.Text.Split(' ')[0];

        Append("[ERR] cache miss\n");
        await service.MarkRunAsync(_channel, Context, CancellationToken.None);
        Append("[ERR] queue stalled\n");

        var result = await service.QueryAsync(
            new LogQueryRequest(_channel, Since: runId), Context, CancellationToken.None);

        Assert.True(result.Ok, result.Error);
        Assert.Contains("cache miss", result.Text, StringComparison.Ordinal);
        Assert.Contains("queue stalled", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MarkRun_ReturnsRunIdAsFirstToken()
    {
        var result = await Service().MarkRunAsync(_channel, Context, CancellationToken.None);

        Assert.True(result.Ok, result.Error);
        Assert.StartsWith("run-", result.Text.Split(' ')[0], StringComparison.Ordinal);
    }
}
