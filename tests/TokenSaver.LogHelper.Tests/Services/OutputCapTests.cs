using System.Text;
using TokenSaver.LogHelper.Contracts;
using TokenSaver.LogHelper.Models;
using TokenSaver.LogHelper.Services;
using TokenSaver.LogHelper.Sources;
using Xunit;

namespace TokenSaver.LogHelper.Tests.Services;

/// <summary>Every view stays within maxTokens (about four characters per token) on a large input.</summary>
public sealed class OutputCapTests : IDisposable
{
    private static readonly LogRequestContext Context = new("test");

    private readonly string _root;
    private readonly LogHelperOptions _options;
    private readonly ChannelId _channel;

    public OutputCapTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "tsl-cap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _options = new LogHelperOptions { AllowedRoots = new[] { _root } };

        string path = Path.Combine(_root, "big.log");
        var sb = new StringBuilder();
        for (int i = 0; i < 50_000; i++)
        {
            string level = i % 500 == 0 ? "ERR" : "INF";
            sb.Append('[').Append(level).Append("] event number ").Append(i % 40).Append(" at step ").Append(i % 7).Append('\n');
            if (i % 1000 == 0)
            {
                sb.Append("   at Foo.Bar").Append(i % 9).Append("() in Foo.cs:line ").Append(i).Append('\n');
            }
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        _channel = ChannelId.Parse("file:" + path);
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

    [Theory]
    [InlineData("summary")]
    [InlineData("traces")]
    [InlineData("ignored")]
    public async Task EveryView_StaysWithinMaxTokens(string view)
    {
        const int maxTokens = 200;
        var service = new LogService(new ILogSourceProvider[] { new FileLogSourceProvider(_options) }, _options);

        var result = await service.QueryAsync(
            new LogQueryRequest(_channel, new LogFilterSpec { Match = "event", MinLevel = LogSeverity.Unknown }, View: view, MaxTokens: maxTokens),
            Context,
            CancellationToken.None);

        Assert.True(result.Ok, result.Error);
        Assert.True(result.Text.Length <= maxTokens * 4, $"{view} length {result.Text.Length}");
    }
}
