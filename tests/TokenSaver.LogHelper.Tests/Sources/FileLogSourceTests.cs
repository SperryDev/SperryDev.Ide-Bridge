using System.Text;
using TokenSaver.LogHelper.Contracts;
using TokenSaver.LogHelper.Models;
using TokenSaver.LogHelper.Sources;
using Xunit;

namespace TokenSaver.LogHelper.Tests.Sources;

public sealed class FileLogSourceTests : IDisposable
{
    private readonly string _root;
    private readonly LogHelperOptions _options;
    private readonly FileLogSourceProvider _provider;

    public FileLogSourceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "tsl-filesrc-" + Guid.NewGuid().ToString("N"));
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

    private string PathFor(string name) => Path.Combine(_root, name);

    private ILogSource Open(string path) => _provider.Open(ChannelId.Parse("file:" + path));

    [Fact]
    public async Task Read_WholeFile_ReturnsAllEntries()
    {
        string path = PathFor("whole.log");
        File.WriteAllText(path, "[INF] one\n[WRN] two\n[ERR] three\n", new UTF8Encoding(false));

        var batch = await Open(path).ReadAsync(null, 100, CancellationToken.None);

        Assert.Equal(new[] { "one", "two", "three" }, batch.Entries.Select(e => e.Message));
        Assert.False(batch.Reset);
        Assert.False(string.IsNullOrEmpty(batch.NextCursor));
    }

    [Fact]
    public async Task Read_PartialTrailingLine_Skipped_ThenReturnedAfterNewline()
    {
        string path = PathFor("partial.log");
        File.WriteAllText(path, "[INF] a\n[INF] b", new UTF8Encoding(false));
        var source = Open(path);

        var first = await source.ReadAsync(null, 100, CancellationToken.None);
        Assert.Equal(new[] { "a" }, first.Entries.Select(e => e.Message));

        File.AppendAllText(path, "\n", new UTF8Encoding(false));
        var second = await source.ReadAsync(first.NextCursor, 100, CancellationToken.None);
        Assert.Equal(new[] { "b" }, second.Entries.Select(e => e.Message));
    }

    [Fact]
    public async Task Resume_FromCursor_ReturnsOnlyNewEntries()
    {
        string path = PathFor("resume.log");
        File.WriteAllText(path, "[INF] old\n", new UTF8Encoding(false));
        var source = Open(path);
        var first = await source.ReadAsync(null, 100, CancellationToken.None);

        File.AppendAllText(path, "[INF] new\n", new UTF8Encoding(false));
        var second = await source.ReadAsync(first.NextCursor, 100, CancellationToken.None);

        Assert.Equal(new[] { "new" }, second.Entries.Select(e => e.Message));
        Assert.False(second.Reset);
    }

    [Fact]
    public async Task Truncated_File_DetectedByAnchorHash_ResetsFlagged()
    {
        string path = PathFor("truncated.log");
        File.WriteAllText(path, string.Concat(Enumerable.Repeat("[INF] original line padding\n", 20)), new UTF8Encoding(false));
        var source = Open(path);
        var first = await source.ReadAsync(null, 100, CancellationToken.None);

        File.WriteAllText(path, "[INF] replacement\n", new UTF8Encoding(false));
        var second = await source.ReadAsync(first.NextCursor, 100, CancellationToken.None);

        Assert.True(second.Reset);
        Assert.Equal(new[] { "replacement" }, second.Entries.Select(e => e.Message));
    }

    [Fact]
    public async Task Utf8Bom_And_Utf16_Decoded()
    {
        string utf8 = PathFor("bom8.log");
        File.WriteAllText(utf8, "[INF] bom utf8\n", new UTF8Encoding(true));

        string utf16 = PathFor("bom16.log");
        File.WriteAllText(utf16, "[INF] bom utf16\n", Encoding.Unicode);

        var a = await Open(utf8).ReadAsync(null, 100, CancellationToken.None);
        var b = await Open(utf16).ReadAsync(null, 100, CancellationToken.None);

        Assert.Equal("bom utf8", Assert.Single(a.Entries).Message);
        Assert.Equal("bom utf16", Assert.Single(b.Entries).Message);
    }

    [Fact]
    public async Task ConcurrentWriter_AppendWhileReading_NoLostOrDuplicateEntries()
    {
        const int total = 200;
        string path = PathFor("concurrent.log");
        File.WriteAllText(path, string.Empty, new UTF8Encoding(false));
        var source = Open(path);

        var writer = Task.Run(() =>
        {
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            using var sw = new StreamWriter(stream, new UTF8Encoding(false));
            for (int i = 0; i < total; i++)
            {
                sw.Write($"[INF] line {i}\n");
                sw.Flush();
            }
        });

        var seen = new List<string>();
        string? cursor = null;
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (seen.Count < total && DateTime.UtcNow < deadline)
        {
            var batch = await source.ReadAsync(cursor, 1000, CancellationToken.None);
            seen.AddRange(batch.Entries.Select(e => e.Message));
            cursor = batch.NextCursor;
        }

        await writer;
        Assert.Equal(total, seen.Count);
        Assert.Equal(total, seen.Distinct().Count());
    }

    [Fact]
    public async Task OutsideAllowedRoot_Throws()
    {
        string outside = Path.Combine(Path.GetTempPath(), "tsl-outside-" + Guid.NewGuid().ToString("N") + ".log");
        File.WriteAllText(outside, "[INF] x\n", new UTF8Encoding(false));
        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                Open(outside).ReadAsync(null, 10, CancellationToken.None));
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public async Task ChunkSplit_AnySplitPoint_EqualsOneShotRead()
    {
        var lines = Enumerable.Range(0, 30).Select(i => $"[INF] item {i} payload").ToArray();

        string oneShotPath = PathFor("oneshot.log");
        File.WriteAllText(oneShotPath, string.Join('\n', lines) + "\n", new UTF8Encoding(false));
        var oneShot = (await Open(oneShotPath).ReadAsync(null, 1000, CancellationToken.None))
            .Entries.Select(e => e.Message).ToList();

        for (int split = 0; split <= lines.Length; split++)
        {
            string path = PathFor($"split-{split}.log");
            File.WriteAllText(path, string.Join('\n', lines.Take(split)) + (split > 0 ? "\n" : string.Empty), new UTF8Encoding(false));
            var source = Open(path);
            var first = await source.ReadAsync(null, 1000, CancellationToken.None);

            File.AppendAllText(path, string.Join('\n', lines.Skip(split)) + "\n", new UTF8Encoding(false));
            var second = await source.ReadAsync(first.NextCursor, 1000, CancellationToken.None);

            var combined = first.Entries.Select(e => e.Message).Concat(second.Entries.Select(e => e.Message)).ToList();
            Assert.Equal(oneShot, combined);
        }
    }
}
