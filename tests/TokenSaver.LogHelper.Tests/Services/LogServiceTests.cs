using System.Text;
using TokenSaver.LogHelper.Contracts;
using TokenSaver.LogHelper.Models;
using TokenSaver.LogHelper.Services;
using TokenSaver.LogHelper.Sources;
using Xunit;

namespace TokenSaver.LogHelper.Tests.Services;

public sealed class LogServiceTests : IDisposable
{
    private static readonly LogRequestContext Context = new("test");

    private readonly string _root;
    private readonly LogHelperOptions _options;

    public LogServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "tsl-service-" + Guid.NewGuid().ToString("N"));
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

    private LogService CreateService(params ILogSourceProvider[] extra) =>
        new(new ILogSourceProvider[] { new FileLogSourceProvider(_options) }.Concat(extra), _options);

    [Fact]
    public async Task Golden_OneUniqueErrorIn10kNoisyLines_SurvivesAndReduces()
    {
        string path = Path.Combine(_root, "noisy.log");
        var sb = new StringBuilder();
        for (int i = 0; i < 10_000; i++)
        {
            if (i == 4_321)
            {
                sb.Append("[ERR] payment gateway timeout after 30s\n");
            }

            sb.Append("[INF] heartbeat tick ").Append(i).Append('\n');
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        string raw = sb.ToString();

        var request = new LogQueryRequest(ChannelId.Parse("file:" + path), MaxTokens: 1500);
        var result = await CreateService().QueryAsync(request, Context, CancellationToken.None);

        Assert.True(result.Ok, result.Error);
        Assert.Contains("payment gateway timeout", result.Text, StringComparison.Ordinal);
        Assert.True(result.RawChars == raw.Length, $"raw {result.RawChars} vs {raw.Length}");
        Assert.True(result.ReturnedChars * 20 < result.RawChars,
            $"returned {result.ReturnedChars} chars of {result.RawChars} raw");
    }

    [Fact]
    public async Task Query_UnknownScheme_ReturnsErrorResultNotThrow()
    {
        var request = new LogQueryRequest(ChannelId.Parse("vscode:Git"));

        var result = await CreateService().QueryAsync(request, Context, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Contains("vscode", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Channels_FilterSubstring_Applies()
    {
        var fake = new FakeProvider(
            "vscode",
            new LogChannelInfo(ChannelId.Parse("vscode:Git"), "Git output", null),
            new LogChannelInfo(ChannelId.Parse("vscode:Extension Host"), "Host output", null));

        var channels = await CreateService(fake).ListChannelsAsync("git", Context, CancellationToken.None);

        var only = Assert.Single(channels);
        Assert.Equal("vscode:Git", only.Id.ToString());
    }

    private sealed class FakeProvider : ILogSourceProvider
    {
        private readonly IReadOnlyList<LogChannelInfo> _channels;

        public FakeProvider(string scheme, params LogChannelInfo[] channels)
        {
            Scheme = scheme;
            _channels = channels;
        }

        public string Scheme { get; }

        public Task<IReadOnlyList<LogChannelInfo>> ListChannelsAsync(CancellationToken ct) => Task.FromResult(_channels);

        public ILogSource Open(ChannelId id) => throw new NotSupportedException();
    }
}
