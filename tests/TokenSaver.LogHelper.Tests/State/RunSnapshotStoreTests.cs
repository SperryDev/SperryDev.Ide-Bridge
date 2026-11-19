using System.Text;
using TokenSaver.LogHelper.Models;
using TokenSaver.LogHelper.Normalization;
using TokenSaver.LogHelper.Services;
using TokenSaver.LogHelper.State;
using Xunit;

namespace TokenSaver.LogHelper.Tests.State;

public sealed class RunSnapshotStoreTests : IDisposable
{
    private static readonly DateTimeOffset Started = new(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);

    private readonly string _dir;

    public RunSnapshotStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tsl-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static RunSnapshot Snapshot(string runId, int count = 1)
    {
        string template = "db timeout";
        var row = new TemplateRow(MessageNormalizer.TemplateHash(template), template, LogSeverity.Error, count, Started, Started, "sample");
        return RunSnapshot.FromRows(runId, Started, new[] { row });
    }

    [Fact]
    public async Task FirstWrite_NoExistingFile_Succeeds()
    {
        var store = new RunSnapshotStore(_dir);

        await store.AppendAsync("file:a", Snapshot("r1"), CancellationToken.None);

        var runs = await store.ListAsync("file:a", CancellationToken.None);
        Assert.Single(runs);
        Assert.Equal("r1", runs[0].RunId);
    }

    [Fact]
    public async Task Retention_KeepsLastFive()
    {
        var store = new RunSnapshotStore(_dir, retention: 5);

        for (int i = 1; i <= 7; i++)
        {
            await store.AppendAsync("file:a", Snapshot("r" + i), CancellationToken.None);
        }

        var runs = await store.ListAsync("file:a", CancellationToken.None);
        Assert.Equal(new[] { "r3", "r4", "r5", "r6", "r7" }, runs.Select(r => r.RunId));
    }

    [Fact]
    public async Task CorruptFile_Tolerated_ReturnsEmpty_AndIsQuarantined()
    {
        var store = new RunSnapshotStore(_dir);
        await store.AppendAsync("file:a", Snapshot("r1"), CancellationToken.None);
        string file = Directory.GetFiles(_dir, "*.json").Single();
        File.WriteAllText(file, "{ not json");

        var runs = await store.ListAsync("file:a", CancellationToken.None);

        Assert.Empty(runs);
        Assert.Single(Directory.GetFiles(_dir, "*.corrupt"));
    }

    [Fact]
    public async Task ParallelWrites_SameKey_NoTornFile()
    {
        var store = new RunSnapshotStore(_dir, retention: 5);

        var writes = Enumerable.Range(0, 32)
            .Select(i => store.AppendAsync("file:a", Snapshot("r" + i), CancellationToken.None));
        await Task.WhenAll(writes);

        var runs = await store.ListAsync("file:a", CancellationToken.None);
        Assert.Equal(5, runs.Count);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public async Task ChannelIdWithPathChars_SanitizedFileName_StaysInStateDir()
    {
        var store = new RunSnapshotStore(_dir);

        await store.AppendAsync(@"file:C:\logs\..\..\a.log/../x", Snapshot("r1"), CancellationToken.None);

        string file = Assert.Single(Directory.GetFiles(_dir, "*.json"));
        Assert.Equal(Path.GetFullPath(_dir), Path.GetDirectoryName(Path.GetFullPath(file)));
    }

    [Fact]
    public async Task State_ContainsNoRawLines()
    {
        var store = new RunSnapshotStore(_dir);
        var entries = new[]
        {
            new LogEntry(Started, LogSeverity.Error, null, "login failed password=hunter2", "login failed password=hunter2"),
        };
        var options = new LogHelperOptions();
        var rows = TemplateAggregator.Aggregate(entries, options);
        var snapshot = RunSnapshot.FromRows("r1", Started, rows);

        await store.AppendAsync("file:a", snapshot, CancellationToken.None);

        string text = File.ReadAllText(Directory.GetFiles(_dir, "*.json").Single(), Encoding.UTF8);
        Assert.DoesNotContain("hunter2", text, StringComparison.Ordinal);
    }
}
