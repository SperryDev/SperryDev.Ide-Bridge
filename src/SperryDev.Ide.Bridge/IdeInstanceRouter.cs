using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SperryDev.Ide.Bridge;

/// <summary>
/// Routes a common IDE action to the right running IDE instance and reports which instance answered.
/// Visual Studio instances are reached over their per-process named pipe (TokenSaver.VS.&lt;pid&gt;).
/// </summary>
public static class IdeInstanceRouter
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Runs <paramref name="action"/> on the instance whose solution matches <paramref name="solutionPath"/>.
    /// With no solution given, the single running instance is used; several instances is an error.
    /// Returns a JSON object that always includes <c>ide</c>, <c>instance_pid</c>, and <c>solution_path</c>.
    /// </summary>
    public static async Task<string> CallAsync(string action, object args, string? solutionPath, CancellationToken ct, string? ide = null)
    {
        var instances = VsInstanceRegistry.ListRunning();
        if (!string.IsNullOrWhiteSpace(ide))
        {
            // Only instances of the requested IDE ("VisualStudio" or "VsCode") are candidates.
            instances = instances.Where(i => string.Equals(i.Ide, ide, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        VsInstance? target;
        if (!string.IsNullOrWhiteSpace(solutionPath))
        {
            var matches = instances.Where(i => SamePath(i.SolutionPath, solutionPath)).ToList();
            if (matches.Count == 0)
            {
                return Error($"No open Visual Studio instance has '{solutionPath}' open.", instances);
            }

            target = matches[0];
        }
        else if (instances.Count == 0)
        {
            return Error(string.IsNullOrWhiteSpace(ide) ? "No Visual Studio instance is running with a solution open." : $"No {ide} instance is running.", instances);
        }
        else if (instances.Count > 1)
        {
            return Error("Several Visual Studio instances are open; pass solutionPath to choose one.", instances);
        }
        else
        {
            target = instances[0];
        }

        return await SendAsync(target, action, args, ct);
    }

    /// <summary>JSON for <c>IdeListInstances</c>: every running instance with its solution.</summary>
    public static string ListInstances()
    {
        var instances = VsInstanceRegistry.ListRunning();
        return JsonSerializer.Serialize(new
        {
            count = instances.Count,
            instances = instances.Select(Describe),
        });
    }

    private static async Task<string> SendAsync(
        VsInstance target,
        string action,
        object args,
        CancellationToken ct)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", target.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(ConnectTimeout);
            await pipe.ConnectAsync(connectCts.Token);

            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);

            await writer.WriteLineAsync(JsonSerializer.Serialize(new { action, args }));
            var line = await reader.ReadLineAsync();

            using var reply = JsonDocument.Parse(line ?? "{}");
            var ok = reply.RootElement.TryGetProperty("ok", out var okProp) && okProp.GetBoolean();

            return JsonSerializer.Serialize(new
            {
                ide = target.Ide,
                instance_pid = target.Pid,
                solution_path = target.SolutionPath,
                ok,
                result = ok && reply.RootElement.TryGetProperty("result", out var r) ? r.Clone() : (JsonElement?)null,
                error = !ok && reply.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null,
            });
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or JsonException or OperationCanceledException)
        {
            return JsonSerializer.Serialize(new
            {
                ide = target.Ide,
                instance_pid = target.Pid,
                solution_path = target.SolutionPath,
                ok = false,
                error = $"Could not reach Visual Studio instance {target.Pid}: {ex.Message}",
            });
        }
    }

    private static string Error(string message, IReadOnlyList<VsInstance> instances) =>
        JsonSerializer.Serialize(new
        {
            ok = false,
            error = message,
            instances = instances.Select(Describe),
        });

    private static object Describe(VsInstance i) => new
    {
        ide = i.Ide,
        pid = i.Pid,
        solution_path = i.SolutionPath,
        updated_utc = i.UpdatedUtc,
    };

    private static bool SamePath(string? a, string b) =>
        !string.IsNullOrEmpty(a) && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}
