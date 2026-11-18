using System;
using System.Text.Json;
using SperryDev.Ide.Bridge;
using Xunit;

namespace SperryDev.Ide.Bridge.Tests;

public sealed class VsInstanceRegistryTests
{
    [Fact]
    public void Manifest_WithAllFields_Parsed()
    {
        const string json = """
            {
              "pid": 4321,
              "pipeName": "TokenSaver.VSCode.4321",
              "solutionPath": "C:\\work\\app",
              "updatedUtc": "2026-11-15T12:00:00Z",
              "ide": "VsCode",
              "logRoots": ["C:\\Users\\me\\AppData\\Roaming\\Code\\logs\\20261115T1\\window1\\exthost"]
            }
            """;

        var instance = VsInstanceRegistry.Parse(json);

        Assert.NotNull(instance);
        Assert.Equal("VsCode", instance!.Ide);
        Assert.Equal(new[] { @"C:\Users\me\AppData\Roaming\Code\logs\20261115T1\window1\exthost" }, instance.LogRoots);
    }

    [Fact]
    public void Manifest_MissingIde_ReturnsNull()
    {
        const string json = """
            {
              "pid": 4321,
              "pipeName": "TokenSaver.VS.4321",
              "solutionPath": "C:\\work\\app.sln",
              "updatedUtc": "2026-11-15T12:00:00Z",
              "logRoots": []
            }
            """;

        Assert.Null(VsInstanceRegistry.Parse(json));
    }

    [Fact]
    public void Manifest_MissingLogRoots_ReturnsNull()
    {
        const string json = """
            {
              "pid": 4321,
              "pipeName": "TokenSaver.VS.4321",
              "solutionPath": "C:\\work\\app.sln",
              "updatedUtc": "2026-11-15T12:00:00Z",
              "ide": "VisualStudio"
            }
            """;

        Assert.Null(VsInstanceRegistry.Parse(json));
    }

    [Fact]
    public void Manifest_WrittenByTheBridgeShape_RoundTrips()
    {
        var written = new VsInstance(4321, "TokenSaver.VS.4321", @"C:\work\app.sln", DateTime.UtcNow, "VisualStudio", Array.Empty<string>());
        var json = JsonSerializer.Serialize(written);

        var read = VsInstanceRegistry.Parse(json);

        Assert.NotNull(read);
        Assert.Equal(written.Pid, read!.Pid);
        Assert.Equal(written.PipeName, read.PipeName);
        Assert.Equal("VisualStudio", read.Ide);
        Assert.Empty(read.LogRoots);
    }

    [Fact]
    public void Manifest_Malformed_ReturnsNull()
    {
        Assert.Null(VsInstanceRegistry.Parse("{ not json"));
    }
}
