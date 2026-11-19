using TokenSaver.LogHelper.Contracts;
using TokenSaver.LogHelper.Models;
using TokenSaver.LogHelper.Sources;
using Xunit;

namespace TokenSaver.LogHelper.Tests.Sources;

public sealed class EventLogSourceTests
{
    private sealed class FakeRunner : IProcessRunner
    {
        public string Stdout { get; set; } = string.Empty;

        public Exception? Failure { get; set; }

        public IReadOnlyList<string>? LastArgs { get; private set; }

        public Task<string> RunAsync(string file, IReadOnlyList<string> args, CancellationToken ct)
        {
            LastArgs = args;
            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.FromResult(Stdout);
        }
    }

    private const string ErrorEvent =
        "<Event xmlns=\"http://schemas.microsoft.com/win/2004/08/events/event\">" +
        "<System><Provider Name=\"Disk\"/><EventID>7</EventID><Level>2</Level>" +
        "<TimeCreated SystemTime=\"2026-11-15T10:20:30.000Z\"/><EventRecordID>42</EventRecordID></System>" +
        "<RenderingInfo><Message>The device has a bad block.</Message></RenderingInfo></Event>";

    private const string InfoEventNoRendering =
        "<Event xmlns=\"http://schemas.microsoft.com/win/2004/08/events/event\">" +
        "<System><Provider Name=\"Service Control\"/><EventID>7036</EventID><Level>4</Level>" +
        "<TimeCreated SystemTime=\"2026-11-15T10:21:00.000Z\"/><EventRecordID>43</EventRecordID></System>" +
        "<EventData><Data>Service entered the running state</Data></EventData></Event>";

    private static EventLogSourceProvider Provider(IProcessRunner runner) =>
        new(runner, new LogHelperOptions());

    [Fact]
    public async Task Evtx_Read_ReturnsEntriesWithLevelMapped()
    {
        var runner = new FakeRunner { Stdout = ErrorEvent + InfoEventNoRendering };
        var source = Provider(runner).Open(ChannelId.Parse("eventlog:System"));

        var batch = await source.ReadAsync(null, 100, CancellationToken.None);

        Assert.Equal(2, batch.Entries.Count);
        Assert.Equal(LogSeverity.Error, batch.Entries[0].Severity);
        Assert.Equal("The device has a bad block.", batch.Entries[0].Message);
        Assert.Equal("Disk", batch.Entries[0].Category);
        Assert.Equal(LogSeverity.Information, batch.Entries[1].Severity);
    }

    [Fact]
    public async Task Evtx_MissingRendering_FallsBackToEventData()
    {
        var runner = new FakeRunner { Stdout = InfoEventNoRendering };
        var source = Provider(runner).Open(ChannelId.Parse("eventlog:System"));

        var batch = await source.ReadAsync(null, 100, CancellationToken.None);

        Assert.Equal("Service entered the running state", Assert.Single(batch.Entries).Message);
    }

    [Fact]
    public async Task Bookmark_Resume_ReturnsOnlyNewer()
    {
        var runner = new FakeRunner { Stdout = ErrorEvent };
        var source = Provider(runner).Open(ChannelId.Parse("eventlog:System"));

        var first = await source.ReadAsync(null, 100, CancellationToken.None);
        Assert.NotNull(first.NextCursor);

        runner.Stdout = string.Empty;
        await source.ReadAsync(first.NextCursor, 100, CancellationToken.None);

        Assert.Contains(runner.LastArgs!, a => a.Contains("EventRecordID>42", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SecurityLog_DeniedByDefault()
    {
        var provider = Provider(new FakeRunner());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            provider.Open(ChannelId.Parse("eventlog:Security")).ReadAsync(null, 10, CancellationToken.None));
    }

    [Fact]
    public async Task LogNotInAllowList_Rejected()
    {
        var provider = Provider(new FakeRunner());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            provider.Open(ChannelId.Parse("eventlog:Microsoft-Windows-Other/Operational")).ReadAsync(null, 10, CancellationToken.None));
    }

    [Fact]
    public async Task AccessDenied_SaysWhatToGrant()
    {
        var runner = new FakeRunner { Failure = new IOException("wevtutil exited with code 5: Access is denied.") };
        var source = Provider(runner).Open(ChannelId.Parse("eventlog:System"));

        var error = await Assert.ThrowsAsync<IOException>(() => source.ReadAsync(null, 10, CancellationToken.None));
        Assert.Contains("Event Log Readers", error.Message, StringComparison.Ordinal);
    }
}
