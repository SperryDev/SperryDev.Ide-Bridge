using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SperryDev.Ide.Bridge;

/// <summary>
/// Lists running Visual Studio instances from the manifests each instance writes to
/// %ProgramData%\TokenSaver\vs-instances\&lt;pid&gt;.json (see <see cref="InstanceRegistry"/>).
/// Manifests for processes that have exited are removed.
/// </summary>
public static class VsInstanceRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static string Directory =>
        Path.Combine(Environment.GetEnvironmentVariable("ProgramData") ?? @"C:\ProgramData", "TokenSaver", "vs-instances");

    public static IReadOnlyList<VsInstance> ListRunning()
    {
        var instances = new List<VsInstance>();
        if (!System.IO.Directory.Exists(Directory))
        {
            return instances;
        }

        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*.json"))
        {
            var instance = TryRead(file);
            if (instance is null)
            {
                continue;
            }

            if (IsRunning(instance.Pid))
            {
                instances.Add(instance);
            }
            else
            {
                TryDelete(file);
            }
        }

        return instances;
    }

    private static VsInstance? TryRead(string file)
    {
        try
        {
            return Parse(File.ReadAllText(file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Parses one manifest. Returns null when the JSON is malformed or a required field is missing.</summary>
    public static VsInstance? Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<VsInstance>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsRunning(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (IOException)
        {
            // Locked by another reader; it will be removed on a later listing.
        }
    }
}

/// <summary>
/// One running IDE window, and the manifest it writes. Every field is required in the JSON.
/// <c>Ide</c> is "VisualStudio" or "VsCode"; SolutionPath is the .sln or, for VS Code, the workspace folder.
/// <c>LogRoots</c> lists the exthost folders a VS Code window reports; empty for Visual Studio.
/// </summary>
public sealed record VsInstance(
    [property: JsonRequired] int Pid,
    [property: JsonRequired] string PipeName,
    [property: JsonRequired] string? SolutionPath,
    [property: JsonRequired] DateTime UpdatedUtc,
    [property: JsonRequired] string Ide,
    [property: JsonRequired] IReadOnlyList<string> LogRoots);
