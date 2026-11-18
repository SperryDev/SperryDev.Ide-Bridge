using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SperryDev.Ide.Bridge;
using Xunit;

namespace SperryDev.Ide.Bridge.Tests;

/// <summary>Tests that change ProgramData run one at a time, so they do not see each other's manifests.</summary>
[Collection("ProgramData")]
public sealed class IdeInstanceRouterTests : IDisposable
{
    private readonly string _root;
    private readonly string? _previousProgramData;

    public IdeInstanceRouterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sperry-ide-bridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _previousProgramData = Environment.GetEnvironmentVariable("ProgramData");
        Environment.SetEnvironmentVariable("ProgramData", _root);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("ProgramData", _previousProgramData);
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Left in the temp folder; nothing else reads it.
        }
    }

    [Fact]
    public void ListInstances_NoManifests_ReportsZero()
    {
        using var json = JsonDocument.Parse(IdeInstanceRouter.ListInstances());

        Assert.Equal(0, json.RootElement.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task CallAsync_NoRunningInstance_ReturnsNotOk()
    {
        using var json = JsonDocument.Parse(await IdeInstanceRouter.CallAsync("health", new { }, null, CancellationToken.None));

        Assert.False(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.Contains("No Visual Studio instance", json.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task CallAsync_InstanceWithNoPipe_ReportsUnreachableWithItsPid()
    {
        // This test process is a running pid, but no bridge listens on its pipe.
        var pid = Environment.ProcessId;
        var manifest = new VsInstance(pid, InstanceRegistry.PipeNameFor(pid), @"C:\work\app.sln", DateTime.UtcNow, "VisualStudio", Array.Empty<string>());
        Directory.CreateDirectory(VsInstanceRegistry.Directory);
        File.WriteAllText(Path.Combine(VsInstanceRegistry.Directory, $"{pid}.json"), JsonSerializer.Serialize(manifest));

        using var json = JsonDocument.Parse(await IdeInstanceRouter.CallAsync("health", new { }, null, CancellationToken.None));

        Assert.False(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(pid, json.RootElement.GetProperty("instance_pid").GetInt32());
        Assert.Equal("VisualStudio", json.RootElement.GetProperty("ide").GetString());
    }
}
