using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace TokenSaver.MCP.Services;

/// <summary>
/// Tracks the processes this MCP server starts (test runs, dev commands, IDE launches), so they can be listed and
/// killed when they hang. Entries store pid and start time, not the Process object, because callers dispose it.
/// Only tracked processes can be killed, so unrelated processes are never touched.
/// </summary>
internal static class ManagedProcessRegistry
{
    private static readonly ConcurrentDictionary<int, Entry> Entries = new();

    private sealed record Entry(int Pid, DateTime ProcessStartUtc, string Label, string FileName, DateTime StartedUtc);

    /// <summary>Records a process this server started. Safe to call with <c>null</c> (a failed start).</summary>
    public static void Track(Process? process, string label)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            Entries[process.Id] = new Entry(
                process.Id,
                process.StartTime.ToUniversalTime(),
                label,
                process.StartInfo.FileName,
                DateTime.UtcNow);
        }
        catch (InvalidOperationException)
        {
            // The process already exited before it could be recorded.
        }
    }

    /// <summary>Records a process by pid, for an instance found after a launch handed off to a running IDE.</summary>
    public static void TrackPid(int pid, string label)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            Track(process, label);
        }
        catch (ArgumentException)
        {
            // Exited before it could be recorded.
        }
    }

    /// <summary>Tracked processes that are still running, with age and responsiveness. Exited entries are dropped.</summary>
    public static IReadOnlyList<object> List()
    {
        var result = new List<object>();
        foreach (var entry in Entries.Values.ToList())
        {
            var process = FindMatch(entry);
            if (process is null)
            {
                Entries.TryRemove(entry.Pid, out _);
                continue;
            }

            using (process)
            {
                result.Add(new
                {
                    pid = entry.Pid,
                    label = entry.Label,
                    file = entry.FileName,
                    started_utc = entry.StartedUtc,
                    age_seconds = (int)(DateTime.UtcNow - entry.StartedUtc).TotalSeconds,
                    responding = SafeResponding(process),
                });
            }
        }

        return result;
    }

    /// <summary>
    /// Kills a tracked process and its child processes. Refuses pids this server did not start, and pids whose
    /// start time no longer matches (the pid was reused by another process).
    /// </summary>
    public static (bool ok, string message) Kill(int pid)
    {
        if (!Entries.TryGetValue(pid, out var entry))
        {
            return (false, $"Process {pid} was not started by the MCP server; refusing to kill it.");
        }

        var process = FindMatch(entry);
        if (process is null)
        {
            Entries.TryRemove(pid, out _);
            return (false, $"Process {pid} has already exited.");
        }

        using (process)
        {
            process.Kill(entireProcessTree: true);
        }

        Entries.TryRemove(pid, out _);
        return (true, $"Killed process {pid} ({entry.Label}) and its child processes.");
    }

    // Returns the live process for this entry, or null if the pid is gone or was reused by a different process.
    private static Process? FindMatch(Entry entry)
    {
        try
        {
            var process = Process.GetProcessById(entry.Pid);
            if (process.HasExited || process.StartTime.ToUniversalTime() != entry.ProcessStartUtc)
            {
                process.Dispose();
                return null;
            }

            return process;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool? SafeResponding(Process process)
    {
        try
        {
            return process.Responding;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
