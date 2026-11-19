using TokenSaver.LogHelper.Contracts;
using TokenSaver.LogHelper.Models;
using TokenSaver.LogHelper.Sources;
using Xunit;

namespace TokenSaver.LogHelper.Tests.Sources;

public sealed class SyslogSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeRunner : IProcessRunner
    {
        public string Stdout { get; set; } = string.Empty;

        public Exception? Failure { get; set; }

        public string? LastFile { get; private set; }

        public IReadOnlyList<string>? LastArgs { get; private set; }

        public Task<string> RunAsync(string file, IReadOnlyList<string> args, CancellationToken ct)
        {
            LastFile = file;
            LastArgs = args;
            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.FromResult(Stdout);
        }
    }

    private static SyslogSourceProvider Provider(IProcessRunner runner, bool windows = false) =>
        new(runner, () => Now, isWindows: windows);

    [Fact]
    public async Task Journal_ArgsPassedAsArray_NoShellString()
    {
        var runner = new FakeRunner { Stdout = string.Empty };
        var source = Provider(runner).Open(ChannelId.Parse("syslog:journal/sshd"));

        await source.ReadAsync(null, 50, CancellationToken.None);

        Assert.Equal("journalctl", runner.LastFile);
        Assert.NotNull(runner.LastArgs);
        Assert.Contains("sshd", runner.LastArgs!);
        Assert.DoesNotContain(runner.LastArgs!, a => a.Contains(' ', StringComparison.Ordinal) && a.Contains("sshd", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Journal_UnitNameWithSemicolon_Rejected()
    {
        var runner = new FakeRunner();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Provider(runner).Open(ChannelId.Parse("syslog:journal/sshd;rm")).ReadAsync(null, 10, CancellationToken.None));
        Assert.Null(runner.LastFile);
    }

    [Fact]
    public async Task Journal_ResumesFromCursor()
    {
        var runner = new FakeRunner
        {
            Stdout = "{\"MESSAGE\":\"one\",\"PRIORITY\":\"6\",\"__CURSOR\":\"s=abc;i=1\",\"__REALTIME_TIMESTAMP\":\"1700000000000000\"}\n"
                + "{\"MESSAGE\":\"two\",\"PRIORITY\":\"6\",\"__CURSOR\":\"s=abc;i=2\",\"__REALTIME_TIMESTAMP\":\"1700000001000000\"}\n",
        };
        var source = Provider(runner).Open(ChannelId.Parse("syslog:journal/sshd"));

        var first = await source.ReadAsync(null, 50, CancellationToken.None);
        Assert.Equal(new[] { "one", "two" }, first.Entries.Select(e => e.Message));

        runner.Stdout = string.Empty;
        await source.ReadAsync(first.NextCursor, 50, CancellationToken.None);

        Assert.Contains("--after-cursor=s=abc;i=2", runner.LastArgs!);
    }

    [Fact]
    public async Task JournalctlMissing_ReturnsUnavailable()
    {
        var runner = new FakeRunner { Failure = new IOException("journalctl not found") };
        var source = Provider(runner).Open(ChannelId.Parse("syslog:journal/sshd"));

        var error = await Assert.ThrowsAsync<IOException>(() => source.ReadAsync(null, 10, CancellationToken.None));
        Assert.Contains("journalctl", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnWindows_JournalChannel_NotListed()
    {
        var provider = Provider(new FakeRunner(), windows: true);

        var channels = await provider.ListChannelsAsync(CancellationToken.None);

        Assert.DoesNotContain(channels, c => c.Id.ToString().Contains("journal", StringComparison.Ordinal));
    }
}
