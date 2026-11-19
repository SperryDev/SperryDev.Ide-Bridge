using System.Globalization;
using TokenSaver.LogHelper.Contracts;
using TokenSaver.LogHelper.Filtering;
using TokenSaver.LogHelper.Models;
using TokenSaver.LogHelper.Parsing;
using TokenSaver.LogHelper.State;

namespace TokenSaver.LogHelper.Services;

/// <summary>
/// The facade every host calls. Supports channel listing, summary queries (cursor or run diff), and run marking.
/// Sink comparison arrives in phase 6.
/// </summary>
public sealed class LogService : ILogService
{
    private const string LastRunToken = "lastRun";
    private const string RunIdPrefix = "run-";
    private const int MaxWatchSeconds = 25;
    private static readonly LogRequestContext Context = new("watch");

    private readonly IReadOnlyList<ILogSourceProvider> _providers;
    private readonly LogHelperOptions _options;
    private RunSnapshotStore? _runsCache;
    private string? _runsCacheDirectory;
    private readonly object _runsLock = new();
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public LogService(IEnumerable<ILogSourceProvider> providers, LogHelperOptions options)
        : this(providers, options, (d, ct) => Task.Delay(d, ct))
    {
    }

    /// <summary>The delay is injectable so watch tests run without waiting.</summary>
    public LogService(IEnumerable<ILogSourceProvider> providers, LogHelperOptions options, Func<TimeSpan, CancellationToken, Task> delay)
    {
        _providers = providers.ToList();
        _options = options;
        _delay = delay;
    }

    /// <summary>
    ///     The run-snapshot store for the current state directory, created on first use. The directory can
    ///     appear after startup (workspace root learned from the client), so it is re-read per call.
    /// </summary>
    private RunSnapshotStore? GetRuns()
    {
        string? directory = _options.StateDirectory;
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        lock (_runsLock)
        {
            if (_runsCache is null || !string.Equals(_runsCacheDirectory, directory, StringComparison.Ordinal))
            {
                _runsCache = new RunSnapshotStore(directory);
                _runsCacheDirectory = directory;
            }

            return _runsCache;
        }
    }

    public async Task<IReadOnlyList<LogChannelInfo>> ListChannelsAsync(string? nameFilter, LogRequestContext context, CancellationToken ct)
    {
        var all = new List<LogChannelInfo>();
        foreach (ILogSourceProvider provider in _providers)
        {
            all.AddRange(await provider.ListChannelsAsync(ct));
        }

        if (string.IsNullOrEmpty(nameFilter))
        {
            return all;
        }

        return all
            .Where(c => c.Id.ToString().Contains(nameFilter, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public async Task<LogQueryResult> QueryAsync(LogQueryRequest request, LogRequestContext context, CancellationToken ct)
    {
        if (request.View is not ("summary" or "traces" or "ignored"))
        {
            return Fail($"Unknown view '{request.View}'. Use summary, traces or ignored.");
        }

        if (IsRunDiffRequest(request.Since))
        {
            return await QueryAgainstRunAsync(request, ct);
        }

        if (request.WaitSeconds > 0)
        {
            return await WatchAsync(request, ct);
        }

        string? formatError = StructuredParser.Validate(request.Format, _options);
        if (formatError is not null)
        {
            return Fail(formatError);
        }

        LogReadResult read = await ReadAllAsync(request.Channel, request.Since, ct, request.Format);
        if (read.Error is not null)
        {
            return Fail(read.Error);
        }

        LogReadBatch batch = read.Batch!;
        LogFilterResult filtered = LogFilter.Apply(batch.Entries, request.Filter ?? new LogFilterSpec(), _options);
        if (filtered.Error is not null)
        {
            return Fail(filtered.Error);
        }

        SummaryText body = request.View switch
        {
            "traces" => TracesFormatter.Format(TraceFlagger.Flag(filtered.Kept), request.MaxTokens),
            "ignored" => IgnoredBody(filtered.Dropped, request.MaxTokens),
            _ => SummaryFormatter.Format(TemplateAggregator.Aggregate(filtered.Kept, _options), request.MaxTokens),
        };

        string text = batch.Reset
            ? "(cursor reset: source changed)\n" + body.Text
            : batch.Rotated
                ? "(log rolled: includes the rest of the previous file)\n" + body.Text
                : body.Text;

        return Ok(text, RawCharsOf(batch.Entries), batch.NextCursor, body.Truncated);
    }

    public async Task<LogQueryResult> MarkRunAsync(ChannelId channel, LogRequestContext context, CancellationToken ct)
    {
        RunSnapshotStore? runs = GetRuns();
        if (runs is null)
        {
            return Fail("No state directory is configured, so runs cannot be marked.");
        }

        LogReadResult read = await ReadAllAsync(channel, null, ct);
        if (read.Error is not null)
        {
            return Fail(read.Error);
        }

        IReadOnlyList<LogEntry> entries = read.Batch!.Entries;
        IReadOnlyList<TemplateRow> rows = TemplateAggregator.Aggregate(entries, _options);
        string runId = NewRunId();
        var snapshot = RunSnapshot.FromRows(runId, DateTimeOffset.UtcNow, rows);

        await runs.AppendAsync(channel.ToString(), snapshot, ct);

        return Ok(
            string.Create(CultureInfo.InvariantCulture, $"{runId} marked: {rows.Count} templates from {entries.Count} entries"),
            RawCharsOf(entries),
            nextCursor: null,
            truncated: false);
    }

    public async Task<LogQueryResult> SinkCompareAsync(ChannelId appChannel, ChannelId sinkChannel, int maxTokens, LogRequestContext context, CancellationToken ct)
    {
        LogReadResult app = await ReadAllAsync(appChannel, null, ct);
        if (app.Error is not null)
        {
            return Fail(app.Error);
        }

        LogReadResult sink = await ReadAllAsync(sinkChannel, null, ct);
        if (sink.Error is not null)
        {
            return Fail(sink.Error);
        }

        IReadOnlyList<LogEntry> appEntries = app.Batch!.Entries;
        IReadOnlyList<LogEntry> sinkEntries = sink.Batch!.Entries;
        var rows = SinkComparer.Compare(
            TemplateAggregator.Aggregate(appEntries, _options),
            TemplateAggregator.Aggregate(sinkEntries, _options));

        SummaryText summary = rows.Count == 0
            ? new SummaryText("no messages in either channel to compare.", Truncated: false, Omitted: 0)
            : SinkComparer.Format(rows, maxTokens);

        return Ok(summary.Text, RawCharsOf(appEntries) + RawCharsOf(sinkEntries), null, summary.Truncated);
    }

    /// <summary>
    /// Bounded live watch: reads once, then polls once a second until something new appears or the wait ends.
    /// Returns the last result with its cursor, so the caller can keep polling without gaps or repeats.
    /// A cancelled wait returns the last result rather than throwing.
    /// </summary>
    private async Task<LogQueryResult> WatchAsync(LogQueryRequest request, CancellationToken ct)
    {
        int seconds = Math.Min(request.WaitSeconds, MaxWatchSeconds);
        var single = request with { WaitSeconds = 0 };

        LogQueryResult last = await QueryAsync(single, Context, ct);
        if (!last.Ok || last.Text.Length > 0)
        {
            return last;
        }

        for (int i = 0; i < seconds; i++)
        {
            try
            {
                await _delay(TimeSpan.FromSeconds(1), ct);
            }
            catch (OperationCanceledException)
            {
                return last;
            }

            last = await QueryAsync(single with { Since = last.NextCursor ?? request.Since }, Context, ct);
            if (!last.Ok || last.Text.Length > 0)
            {
                return last;
            }
        }

        return last;
    }

    // What the filter dropped, summarized the same way as kept entries, so a filter can be checked for hidden errors.
    private SummaryText IgnoredBody(IReadOnlyList<LogEntry> dropped, int maxTokens)
    {
        if (dropped.Count == 0)
        {
            return new SummaryText("nothing was dropped by the filter.", Truncated: false, Omitted: 0);
        }

        return SummaryFormatter.Format(TemplateAggregator.Aggregate(dropped, _options), maxTokens);
    }

    private static bool IsRunDiffRequest(string? since) =>
        since is not null
        && (string.Equals(since, LastRunToken, StringComparison.Ordinal)
            || since.StartsWith(RunIdPrefix, StringComparison.Ordinal));

    private async Task<LogQueryResult> QueryAgainstRunAsync(LogQueryRequest request, CancellationToken ct)
    {
        RunSnapshotStore? runs = GetRuns();
        if (runs is null)
        {
            return Fail("No state directory is configured, so runs cannot be compared.");
        }

        IReadOnlyList<RunSnapshot> stored = await runs.ListAsync(request.Channel.ToString(), ct);
        RunSnapshot? baseline;
        if (string.Equals(request.Since, LastRunToken, StringComparison.Ordinal))
        {
            baseline = stored.Count > 0 ? stored[^1] : null;
            if (baseline is null)
            {
                return Ok("no baseline for this channel yet; call LogMarkRun first.", 0, null, false);
            }
        }
        else
        {
            baseline = stored.FirstOrDefault(s => string.Equals(s.RunId, request.Since, StringComparison.Ordinal));
            if (baseline is null)
            {
                return Fail($"unknown run '{request.Since}' for this channel.");
            }
        }

        LogReadResult read = await ReadAllAsync(request.Channel, null, ct);
        if (read.Error is not null)
        {
            return Fail(read.Error);
        }

        IReadOnlyList<LogEntry> entries = read.Batch!.Entries;
        var current = RunSnapshot.FromRows("current", DateTimeOffset.UtcNow, TemplateAggregator.Aggregate(entries, _options));
        RunDiff diff = RunDiffer.Diff(baseline, current);

        if (diff.BaselineReset)
        {
            return Ok("baseline reset: masking rules changed since the last run; call LogMarkRun to set a new baseline.",
                RawCharsOf(entries), null, false);
        }

        var candidates = new List<(TemplateRow Row, LogEntry Entry)>();
        foreach (TemplateChange change in diff.New)
        {
            candidates.Add(ToCandidate(change.Template, change.Severity, change.CurrentCount, change.Sample, null));
        }

        foreach (TemplateChange change in diff.Changed)
        {
            candidates.Add(ToCandidate(change.Template, change.Severity, change.CurrentCount, change.Sample, change.PreviousCount));
        }

        LogFilterResult filtered = LogFilter.Apply(candidates.Select(c => c.Entry), request.Filter ?? new LogFilterSpec(), _options);
        if (filtered.Error is not null)
        {
            return Fail(filtered.Error);
        }

        var kept = new HashSet<LogEntry>(filtered.Kept, ReferenceEqualityComparer.Instance);
        List<TemplateRow> rows = candidates.Where(c => kept.Contains(c.Entry)).Select(c => c.Row).ToList();

        SummaryText summary = SummaryFormatter.Format(rows, request.MaxTokens);
        var text = new System.Text.StringBuilder();
        text.Append(rows.Count == 0 ? "no new or changed messages since " + baseline.RunId : summary.Text);
        if (diff.Gone.Count > 0)
        {
            text.Append('\n').Append(string.Create(CultureInfo.InvariantCulture, $"{diff.Gone.Count} template(s) no longer present."));
        }

        return Ok(text.ToString(), RawCharsOf(entries), null, summary.Truncated);
    }

    private static (TemplateRow Row, LogEntry Entry) ToCandidate(string template, LogSeverity severity, int count, string sample, int? previousCount)
    {
        string label = previousCount is int prev
            ? string.Create(CultureInfo.InvariantCulture, $"{template} (was {prev})")
            : template;
        var row = new TemplateRow(string.Empty, label, severity, count, null, null, sample);
        var entry = new LogEntry(null, severity, null, template, sample);
        return (row, entry);
    }

    private async Task<LogReadResult> ReadAllAsync(ChannelId channel, string? cursor, CancellationToken ct, LogFormatSpec? format = null)
    {
        ILogSourceProvider? provider = _providers.FirstOrDefault(p =>
            string.Equals(p.Scheme, channel.Scheme, StringComparison.OrdinalIgnoreCase));
        if (provider is null)
        {
            return LogReadResult.Failed($"No log provider is registered for scheme '{channel.Scheme}'.");
        }

        try
        {
            LogReadBatch batch = await provider.Open(channel).ReadAsync(cursor, int.MaxValue, format, ct);
            return LogReadResult.Ok(batch);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or NotSupportedException)
        {
            return LogReadResult.Failed(ex.Message);
        }
    }

    private static string NewRunId() =>
        RunIdPrefix + Guid.NewGuid().ToString("N")[..12];

    private static int RawCharsOf(IEnumerable<LogEntry> entries) =>
        entries.Sum(e => e.Raw.Length + 1);

    private static LogQueryResult Ok(string text, int rawChars, string? nextCursor, bool truncated) =>
        new(Ok: true, Error: null, Text: text, RawChars: rawChars, ReturnedChars: text.Length, NextCursor: nextCursor, Truncated: truncated);

    private static LogQueryResult Fail(string error) =>
        new(Ok: false, Error: error, Text: string.Empty, RawChars: 0, ReturnedChars: 0, NextCursor: null, Truncated: false);

    private sealed record LogReadResult(LogReadBatch? Batch, string? Error)
    {
        public static LogReadResult Ok(LogReadBatch batch) => new(batch, null);

        public static LogReadResult Failed(string error) => new(null, error);
    }
}
