using System.Text;
using TokenSaver.LogHelper.Models;
using TokenSaver.LogHelper.Sources;
using Xunit;

namespace TokenSaver.LogHelper.Tests.Sources;

public sealed class FileRotationTests : IDisposable
{
    private readonly string _root;
    private readonly LogHelperOptions _options;

    public FileRotationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "tsl-rot-" + Guid.NewGuid().ToString("N"));
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

    private string PathFor(string name) => Path.Combine(_root, name);

    private FileLogSource Open(string path) => new(path, _options);

    private static readonly UTF8Encoding Utf8 = new(false);

    private static string[] Messages(LogReadBatch batch) => batch.Entries.Select(e => e.Message).ToArray();

    [Fact]
    public async Task RenameRotation_FullyReadFile_NewFileStartsFromZero()
    {
        string path = PathFor("app.log");
        File.WriteAllText(path, "[INF] before rotation\n", Utf8);
        var first = await Open(path).ReadAsync(null, 100, CancellationToken.None);

        File.Move(path, PathFor("app.1.log"));
        File.WriteAllText(path, "[INF] after rotation\n", Utf8);

        var second = await Open(path).ReadAsync(first.NextCursor, 100, CancellationToken.None);

        Assert.True(second.Rotated);
        Assert.False(second.Reset);
        Assert.Equal(new[] { "after rotation" }, Messages(second));
    }

    [Fact]
    public async Task RenameRotation_UnreadTailOfRolledFile_IsReadBeforeTheNewFile()
    {
        string path = PathFor("app.log");
        File.WriteAllText(path, "[INF] one\n[INF] two\n", Utf8);
        var first = await Open(path).ReadAsync(null, 100, CancellationToken.None);

        // Written after the last read and before the roll: must not be lost.
        File.AppendAllText(path, "[INF] three\n[INF] four\n", Utf8);
        File.Move(path, PathFor("app.log.1"));
        File.WriteAllText(path, "[INF] five\n", Utf8);

        var second = await Open(path).ReadAsync(first.NextCursor, 100, CancellationToken.None);

        Assert.True(second.Rotated);
        Assert.False(second.Reset);
        Assert.Equal(new[] { "three", "four", "five" }, Messages(second));
    }

    [Fact]
    public async Task RenameRotation_ThenAppend_ContinuesWithoutRepeats()
    {
        string path = PathFor("app.log");
        File.WriteAllText(path, "[INF] one\n", Utf8);
        var first = await Open(path).ReadAsync(null, 100, CancellationToken.None);

        File.AppendAllText(path, "[INF] two\n", Utf8);
        File.Move(path, PathFor("app.log.1"));
        File.WriteAllText(path, "[INF] three\n", Utf8);
        var second = await Open(path).ReadAsync(first.NextCursor, 100, CancellationToken.None);

        File.AppendAllText(path, "[INF] four\n", Utf8);
        var third = await Open(path).ReadAsync(second.NextCursor, 100, CancellationToken.None);
        var fourth = await Open(path).ReadAsync(third.NextCursor, 100, CancellationToken.None);

        Assert.Equal(new[] { "two", "three" }, Messages(second));
        Assert.False(third.Rotated);
        Assert.Equal(new[] { "four" }, Messages(third));
        Assert.Empty(fourth.Entries);
    }

    [Fact]
    public async Task RenameRotation_FileMissingBetweenRollAndRecreate_TailReadThenNewFile()
    {
        string path = PathFor("app.log");
        File.WriteAllText(path, "[INF] one\n", Utf8);
        var first = await Open(path).ReadAsync(null, 100, CancellationToken.None);

        File.AppendAllText(path, "[INF] two\n", Utf8);
        File.Move(path, PathFor("app.log.1"));

        var during = await Open(path).ReadAsync(first.NextCursor, 100, CancellationToken.None);
        File.WriteAllText(path, "[INF] three\n", Utf8);
        var after = await Open(path).ReadAsync(during.NextCursor, 100, CancellationToken.None);

        Assert.True(during.Rotated);
        Assert.Equal(new[] { "two" }, Messages(during));
        Assert.False(after.Reset);
        Assert.Equal(new[] { "three" }, Messages(after));
    }

    [Fact]
    public async Task RolledFile_UnterminatedLastLine_IsStillRead()
    {
        string path = PathFor("app.log");
        File.WriteAllText(path, "[INF] one\n", Utf8);
        var first = await Open(path).ReadAsync(null, 100, CancellationToken.None);

        File.AppendAllText(path, "[INF] two\n[INF] no newline at the end", Utf8);
        File.Move(path, PathFor("app.log.1"));
        File.WriteAllText(path, "[INF] new\n", Utf8);

        var second = await Open(path).ReadAsync(first.NextCursor, 100, CancellationToken.None);

        Assert.Equal(new[] { "two", "no newline at the end", "new" }, Messages(second));
    }

    [Fact]
    public async Task CopyTruncate_WithCopyBesideIt_ReadsTheRestOfTheCopyThenTheNewContent()
    {
        string path = PathFor("copy.log");
        File.WriteAllText(path, "[INF] one\n", Utf8);
        var first = await Open(path).ReadAsync(null, 100, CancellationToken.None);

        File.AppendAllText(path, "[INF] two\n", Utf8);
        File.Copy(path, PathFor("copy.log.1"));
        File.WriteAllText(path, "[INF] three\n", Utf8);

        var second = await Open(path).ReadAsync(first.NextCursor, 100, CancellationToken.None);

        Assert.True(second.Rotated);
        Assert.False(second.Reset);
        Assert.Equal(new[] { "two", "three" }, Messages(second));
    }

    [Fact]
    public async Task Truncation_WithNoRolledFile_IsAReset()
    {
        string path = PathFor("trunc.log");
        File.WriteAllText(path, "[INF] one\n[INF] two\n", Utf8);
        var first = await Open(path).ReadAsync(null, 100, CancellationToken.None);

        File.WriteAllText(path, "[INF] new\n", Utf8);
        var second = await Open(path).ReadAsync(first.NextCursor, 100, CancellationToken.None);

        Assert.True(second.Reset);
        Assert.False(second.Rotated);
        Assert.Equal(new[] { "new" }, Messages(second));
    }

    [Fact]
    public async Task UnrelatedSiblingWithSameBytes_IsNotTreatedAsTheRolledFile()
    {
        string path = PathFor("app.log");
        File.WriteAllText(path, "[INF] one\n[INF] two\n", Utf8);
        var first = await Open(path).ReadAsync(null, 100, CancellationToken.None);

        // Same content, but a different stem: not a rolled copy of app.log.
        File.WriteAllText(PathFor("other.log"), "[INF] one\n[INF] two\n[INF] only here\n", Utf8);
        File.WriteAllText(path, "[INF] new\n", Utf8);
        var second = await Open(path).ReadAsync(first.NextCursor, 100, CancellationToken.None);

        Assert.True(second.Reset);
        Assert.Equal(new[] { "new" }, Messages(second));
    }

    [Fact]
    public async Task CompressedSibling_IsNotUsedAsTheRolledFile()
    {
        string path = PathFor("app.log");
        File.WriteAllText(path, "[INF] one\n", Utf8);
        var first = await Open(path).ReadAsync(null, 100, CancellationToken.None);

        File.Move(path, PathFor("app.log.1.gz"));
        File.WriteAllText(path, "[INF] new\n", Utf8);
        var second = await Open(path).ReadAsync(first.NextCursor, 100, CancellationToken.None);

        Assert.True(second.Reset);
        Assert.Equal(new[] { "new" }, Messages(second));
    }

    [Fact]
    public async Task RolledFile_StructuredFormat_KeepsTheFormatAcrossTheRoll()
    {
        string path = PathFor("app.log");
        File.WriteAllText(path, "{\"level\":\"info\",\"msg\":\"one\"}\n{\"level\":\"info\",\"msg\":\"two\"}\n", Utf8);
        var first = await Open(path).ReadAsync(null, 100, CancellationToken.None);

        File.AppendAllText(path, "{\"level\":\"error\",\"msg\":\"three\"}\n", Utf8);
        File.Move(path, PathFor("app.log.1"));
        File.WriteAllText(path, "{\"level\":\"warn\",\"msg\":\"four\"}\n", Utf8);
        var second = await Open(path).ReadAsync(first.NextCursor, 100, CancellationToken.None);

        Assert.Equal(new[] { "three", "four" }, Messages(second));
        Assert.All(second.Entries, e => Assert.NotNull(e.Fields));
    }

    [Fact]
    public async Task CopyTruncate_LongerRewrite_DetectedByAnchor()
    {
        string path = PathFor("copy.log");
        File.WriteAllText(path, "[INF] one line\n", new UTF8Encoding(false));
        var first = await Open(path).ReadAsync(null, 100, CancellationToken.None);

        File.WriteAllText(path, "[INF] a much longer replacement line that passes the old offset\n", new UTF8Encoding(false));
        var second = await Open(path).ReadAsync(first.NextCursor, 100, CancellationToken.None);

        Assert.True(second.Reset);
        Assert.Single(second.Entries);
    }

    [Fact]
    public async Task DeletedWhileOpen_NoThrow_ReturnsEmpty()
    {
        string path = PathFor("gone.log");
        File.WriteAllText(path, "[INF] here\n", new UTF8Encoding(false));
        var first = await Open(path).ReadAsync(null, 100, CancellationToken.None);

        File.Delete(path);
        var second = await Open(path).ReadAsync(first.NextCursor, 100, CancellationToken.None);

        Assert.Empty(second.Entries);
    }
}
