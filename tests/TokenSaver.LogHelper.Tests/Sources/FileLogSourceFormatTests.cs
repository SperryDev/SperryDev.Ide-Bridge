using TokenSaver.LogHelper.Contracts;
using TokenSaver.LogHelper.Models;
using TokenSaver.LogHelper.Sources;
using Xunit;

namespace TokenSaver.LogHelper.Tests.Sources;

public sealed class FileLogSourceFormatTests : IDisposable
{
    private readonly string _root;
    private readonly LogHelperOptions _options;
    private readonly FileLogSourceProvider _provider;

    public FileLogSourceFormatTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "tsl-fmt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _options = new LogHelperOptions { AllowedRoots = new[] { _root } };
        _provider = new FileLogSourceProvider(_options);
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

    private ILogSource Open(string path) => _provider.Open(ChannelId.Parse("file:" + path));

    [Fact]
    public async Task JsonFile_IsSniffedOnce_AndCursorKeepsTheFormat()
    {
        string path = Path.Combine(_root, "app.log");
        await File.WriteAllTextAsync(path, "{\"level\":\"Info\",\"msg\":\"a\",\"req\":\"1\"}\n");

        var first = await Open(path).ReadAsync(null, 100, null, CancellationToken.None);
        Assert.Equal("1", first.Entries[0].Fields!["req"]);

        await File.AppendAllTextAsync(path, "{\"level\":\"Info\",\"msg\":\"b\",\"req\":\"2\"}\n");
        var second = await Open(path).ReadAsync(first.NextCursor, 100, null, CancellationToken.None);

        // The cursor carried the json format, so the new record went through the structured parser.
        Assert.Single(second.Entries);
        Assert.Equal("2", second.Entries[0].Fields!["req"]);
    }

    [Fact]
    public async Task ExplicitRegexFormat_ExtractsFields()
    {
        string path = Path.Combine(_root, "svc.log");
        await File.WriteAllTextAsync(path, "api ERROR timeout\n");

        var format = new LogFormatSpec("regex", @"^(?<service>\w+) (?<level>\w+) (?<message>.*)$");
        var batch = await Open(path).ReadAsync(null, 100, format, CancellationToken.None);

        Assert.Equal("api", batch.Entries[0].Fields!["service"]);
        Assert.Equal(LogSeverity.Error, batch.Entries[0].Severity);
        Assert.Equal("timeout", batch.Entries[0].Message);
    }
}
