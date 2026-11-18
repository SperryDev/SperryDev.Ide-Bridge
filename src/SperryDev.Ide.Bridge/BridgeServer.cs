using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;



namespace SperryDev.Ide.Bridge;

/// <summary>
/// Named-pipe server for one Visual Studio instance. Each request is one JSON line:
/// <c>{"action":"...","args":{...}}</c>. Each reply is one JSON line: <c>{"ok":true,"result":{...}}</c> or
/// <c>{"ok":false,"error":"..."}</c>. The pipe name embeds the process id, so several instances run side by side.
/// The listen loop survives faults: an unexpected error is logged and the listener is recreated after a backoff.
/// <see cref="IsRunning"/> lets a watchdog restart it if the loop has stopped. The <c>health</c> action is answered
/// here, not on the UI thread, so it responds even while Visual Studio is busy.
/// </summary>
public sealed class BridgeServer : IDisposable
{
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    private readonly string _pipeName;
    private readonly Func<string, JsonElement, Task<object>> _invoke;
    private readonly Func<string?> _solutionPath;
    private readonly IBridgeLogger _logger;
    private CancellationTokenSource _cts = new();
    private Task? _loop;
    private DateTime? _startedUtc;
    private DateTime? _lastRequestUtc;
    private long _requestsHandled;

    public BridgeServer(
        string pipeName,
        Func<string, JsonElement, Task<object>> invoke,
        Func<string?> solutionPath,
        IBridgeLogger logger)
    {
        _pipeName = pipeName ?? throw new ArgumentNullException(nameof(pipeName));
        _invoke = invoke ?? throw new ArgumentNullException(nameof(invoke));
        _solutionPath = solutionPath ?? throw new ArgumentNullException(nameof(solutionPath));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>True while the listen loop is running (it is not faulted, cancelled, or never started).</summary>
    public bool IsRunning => _loop is { IsCompleted: false } && !_cts.IsCancellationRequested;

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _startedUtc = DateTime.UtcNow;
        var ct = _cts.Token;
        _loop = Task.Run(() => RunAsync(ct));
    }

    /// <summary>Cancels the listen loop. <see cref="Start"/> can run it again.</summary>
    public void Stop() => _cts.Cancel();

    private async Task RunAsync(CancellationToken ct)
    {
        var backoff = TimeSpan.FromMilliseconds(250);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    _pipeName, PipeDirection.InOut, maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                backoff = TimeSpan.FromMilliseconds(250);
                await HandleAsync(server, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogException(ex);
                try
                {
                    await Task.Delay(backoff, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                backoff = Min(TimeSpan.FromTicks(backoff.Ticks * 2), MaxBackoff);
            }
        }
    }

    private async Task HandleAsync(Stream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true) { AutoFlush = true };

        ct.ThrowIfCancellationRequested();
        var line = await reader.ReadLineAsync().ConfigureAwait(false);
        var reply = await ReplyAsync(line).ConfigureAwait(false);
        await writer.WriteLineAsync(reply).ConfigureAwait(false);
    }

    private async Task<string> ReplyAsync(string? line)
    {
        Interlocked.Increment(ref _requestsHandled);
        _lastRequestUtc = DateTime.UtcNow;

        try
        {
            using var doc = JsonDocument.Parse(line ?? string.Empty);
            var root = doc.RootElement;
            var action = root.GetProperty("action").GetString() ?? string.Empty;

            if (action == "health")
            {
                return JsonSerializer.Serialize(new { ok = true, pid = System.Diagnostics.Process.GetCurrentProcess().Id, result = Health() });
            }

            var args = root.TryGetProperty("args", out var a) ? a.Clone() : default;
            var result = await _invoke(action, args).ConfigureAwait(false);
            return JsonSerializer.Serialize(new { ok = true, pid = System.Diagnostics.Process.GetCurrentProcess().Id, result });
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            return JsonSerializer.Serialize(new { ok = false, pid = System.Diagnostics.Process.GetCurrentProcess().Id, error = ex.Message });
        }
    }

    // Answered without touching Visual Studio, so it reports even when the UI thread is busy.
    private object Health() => new
    {
        listener_running = IsRunning,
        started_utc = _startedUtc,
        last_request_utc = _lastRequestUtc,
        requests_handled = Interlocked.Read(ref _requestsHandled),
        solution_path = _solutionPath(),
        pipe_name = _pipeName,
    };

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a <= b ? a : b;

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
