using System.Text;
using TokenSaver.LogHelper.Contracts;
using TokenSaver.LogHelper.Models;
using TokenSaver.LogHelper.Services;
using TokenSaver.LogHelper.Sources;
using Xunit;

namespace TokenSaver.LogHelper.Tests.Services;

public sealed class SinkCompareServiceTests : IDisposable
{
    private static readonly LogRequestContext Context = new("test");

    private readonly string _root;
    private readonly LogHelperOptions _options;

    public SinkCompareServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "tsl-sink-" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public async Task SinkCompare_FlagsPruneAndElevateCandidates()
    {
        var app = WriteLog("app.log", "[ERR] payment gateway timeout\n[INF] heartbeat ok\n");
        var sink = WriteLog("sink.log", "[INF] heartbeat ok\n[INF] heartbeat ok\n");
        var service = new LogService(new ILogSourceProvider[] { new FileLogSourceProvider(_options) }, _options);

        var result = await service.SinkCompareAsync(app, sink, 1500, Context, CancellationToken.None);

        Assert.True(result.Ok, result.Error);
        Assert.Contains("ELEVATE", result.Text, StringComparison.Ordinal);
        Assert.Contains("payment gateway timeout", result.Text, StringComparison.Ordinal);
        Assert.Contains("PRUNE", result.Text, StringComparison.Ordinal);
    }
}
