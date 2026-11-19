using System.Text;
using TokenSaver.LogHelper.Contracts;
using TokenSaver.LogHelper.Models;
using TokenSaver.LogHelper.Services;
using TokenSaver.LogHelper.Sources;
using Xunit;

namespace TokenSaver.LogHelper.Tests.Services;

public sealed class LogServiceViewTests : IDisposable
{
    private static readonly LogRequestContext Context = new("test");

    private readonly string _root;
    private readonly LogHelperOptions _options;

    public LogServiceViewTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "tsl-views-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _options = new LogHelperOptions { AllowedRoots = new[] { _root } };
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

    private ChannelId WriteLog(string name, string content)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return ChannelId.Parse("file:" + path);
    }

    private LogService Service() =>
        new(new ILogSourceProvider[] { new FileLogSourceProvider(_options) }, _options);

    [Fact]
    public async Task TracesView_ListsFlaggedTraceWithConfidence()
    {
        var channel = WriteLog("traces.log", "[INF] request failed\n   at Foo.Bar() in Foo.cs:line 10\n[INF] ok\n");

        var result = await Service().QueryAsync(
            new LogQueryRequest(channel, View: "traces"), Context, CancellationToken.None);

        Assert.True(result.Ok, result.Error);
        Assert.Contains("MEDIUM", result.Text, StringComparison.Ordinal);
        Assert.Contains("request failed", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TracesView_RespectsMaxTokens()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 300; i++)
        {
            // Letters, not digits, so each trace has a distinct top frame after normalization.
            string name = "Ns" + (char)('a' + i % 26) + (char)('a' + i / 26 % 26) + (char)('a' + i / 676 % 26);
            sb.Append("[ERR] trace ").Append(i).Append('\n').Append("   at ").Append(name).Append(".Run()\n");
        }

        var channel = WriteLog("many.log", sb.ToString());

        var result = await Service().QueryAsync(
            new LogQueryRequest(channel, View: "traces", MaxTokens: 50), Context, CancellationToken.None);

        Assert.True(result.Ok, result.Error);
        Assert.True(result.Text.Length <= 50 * 4, $"length {result.Text.Length}");
    }

    [Fact]
    public async Task IgnoredView_ShowsDroppedMessages()
    {
        var channel = WriteLog("ignored.log", "[INF] noise from heartbeat\n[ERR] keep this failure\n");

        var result = await Service().QueryAsync(
            new LogQueryRequest(channel, new LogFilterSpec { MinLevel = LogSeverity.Error }, View: "ignored"),
            Context, CancellationToken.None);

        Assert.True(result.Ok, result.Error);
        Assert.Contains("noise from heartbeat", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("keep this failure", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IgnoredView_NoFilter_SaysNothingDropped()
    {
        var channel = WriteLog("none.log", "[ERR] only line\n");

        var result = await Service().QueryAsync(
            new LogQueryRequest(channel, View: "ignored"), Context, CancellationToken.None);

        Assert.True(result.Ok, result.Error);
        Assert.Contains("nothing was dropped", result.Text, StringComparison.OrdinalIgnoreCase);
    }
}
