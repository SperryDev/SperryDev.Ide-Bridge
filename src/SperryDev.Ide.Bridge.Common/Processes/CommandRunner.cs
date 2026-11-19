using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace TokenSaver.Common.Core.Processes;

/// <summary>What to run and how long to let it run.</summary>
public sealed record CommandRequest
{
    /// <summary>Executable name or path. Never a shell command line.</summary>
    public required string FileName { get; init; }

    /// <summary>Arguments, one entry each; they are passed through <see cref="ProcessStartInfo.ArgumentList" /> so no shell or parser sees them.</summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];

    public string? WorkingDirectory { get; init; }

    /// <summary>The process tree is killed when this elapses. Default 10 minutes.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Per stream; output beyond this is drained and dropped, never blocks the child. Default 4 million characters.</summary>
    public int MaxOutputChars { get; init; } = 4_000_000;

    /// <summary>Environment overrides; a null value removes the variable.</summary>
    public IReadOnlyDictionary<string, string?>? Environment { get; init; }

    /// <summary>Called once, right after the process starts (for example to record it in a process registry).</summary>
    public Action<Process>? OnStarted { get; init; }
}

/// <summary>Outcome of a <see cref="CommandRunner.RunAsync" /> call. Ordinary failures are data, not exceptions.</summary>
public sealed record CommandResult
{
    /// <summary>False when the executable could not be started at all (missing, not executable).</summary>
    public bool Launched { get; init; } = true;

    /// <summary>Why it did not start, when <see cref="Launched" /> is false.</summary>
    public string? LaunchError { get; init; }

    public int ExitCode { get; init; }

    public string StdOut { get; init; } = "";

    public string StdErr { get; init; } = "";

    /// <summary>True when the timeout elapsed and the process tree was killed.</summary>
    public bool TimedOut { get; init; }

    public bool StdOutTruncated { get; init; }

    public bool StdErrTruncated { get; init; }

    public bool Success => Launched && !TimedOut && ExitCode == 0;
}

/// <summary>
///     The one place that starts a child process, runs it to completion and captures its output. Reads both
///     streams concurrently (a child that fills one pipe while the other is open never blocks), applies a timeout,
///     kills the whole process tree on timeout or cancellation, caps captured output, and disposes the process.
///     Long-lived or interactive launches (IDEs, terminals, services) are not run-to-completion and live elsewhere.
/// </summary>
public static class CommandRunner
{
    public static async Task<CommandResult> RunAsync(CommandRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        ProcessStartInfo info = new(request.FileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            info.WorkingDirectory = request.WorkingDirectory;
        }

        foreach (string argument in request.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        if (request.Environment is not null)
        {
            foreach ((string key, string? value) in request.Environment)
            {
                if (value is null)
                {
                    _ = info.Environment.Remove(key);
                }
                else
                {
                    info.Environment[key] = value;
                }
            }
        }

        using Process process = new() { StartInfo = info };
        try
        {
            if (!process.Start())
            {
                return new CommandResult { Launched = false, LaunchError = $"{request.FileName} did not start." };
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or DirectoryNotFoundException)
        {
            return new CommandResult { Launched = false, LaunchError = ex.Message };
        }

        try
        {
            request.OnStarted?.Invoke(process);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Tracking is best effort: a process that already exited cannot be recorded.
        }

        Capture stdout = new(request.MaxOutputChars);
        Capture stderr = new(request.MaxOutputChars);
        Task readOut = stdout.ReadAsync(process.StandardOutput);
        Task readErr = stderr.ReadAsync(process.StandardError);

        using CancellationTokenSource timeoutSource = new(request.Timeout);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutSource.Token);
        bool timedOut = false;
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            KillTree(process);
            if (ct.IsCancellationRequested)
            {
                throw;
            }

            timedOut = true;
        }

        // Killing the tree closes the pipes, so the readers end; the guard keeps a grandchild that
        // inherited a pipe from holding the call open.
        _ = await Task.WhenAny(Task.WhenAll(readOut, readErr), Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None)).ConfigureAwait(false);

        return new CommandResult
        {
            ExitCode = timedOut || !process.HasExited ? -1 : process.ExitCode,
            StdOut = stdout.Text,
            StdErr = stderr.Text,
            TimedOut = timedOut,
            StdOutTruncated = stdout.Truncated,
            StdErrTruncated = stderr.Truncated,
        };
    }

    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // Already gone.
        }
    }

    private sealed class Capture(int max)
    {
        private readonly StringBuilder _text = new();
        private readonly object _gate = new();

        public bool Truncated { get; private set; }

        public string Text
        {
            get
            {
                lock (_gate)
                {
                    return _text.ToString();
                }
            }
        }

        public async Task ReadAsync(StreamReader reader)
        {
            char[] buffer = new char[8192];
            int read;
            try
            {
                while ((read = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
                {
                    lock (_gate)
                    {
                        int room = max - _text.Length;
                        if (room >= read)
                        {
                            _ = _text.Append(buffer, 0, read);
                        }
                        else
                        {
                            if (room > 0)
                            {
                                _ = _text.Append(buffer, 0, room);
                            }

                            Truncated = true; // keep reading and drop: the child must never block on a full pipe
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // The pipe closed under us (process killed).
            }
        }
    }
}
