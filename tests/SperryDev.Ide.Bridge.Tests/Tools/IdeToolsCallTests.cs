using System.Text.Json;

using Microsoft.Extensions.Options;
using TokenSaver.MCP.Services;
using TokenSaver.MCP.Tools;

using Xunit;

namespace TokenSaver.MCP.Tests.Tools;

/// <summary>
///     Calls every IDE and process tool with a solution path that no instance has open, and no real launch or
///     kill. Each one must report an error, which proves the call reaches the router or the guard and fails safely.
///     Routing to a live instance is covered by the live checks, not here.
/// </summary>
public sealed class IdeToolsCallTests
{
    private const string MissingSolution = @"C:\__tokensaver_no_such_dir__\Missing.sln";

    private static readonly IdeTools Tools = new(Options.Create(new IdeToolsOptions()));

    [Fact]
    public void IdeListInstances_ReturnsJson()
    {
        using var doc = JsonDocument.Parse(Tools.IdeListInstances());
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }

    [Fact]
    public async Task IdeSetStartupProject_NoInstance_ReportsError()
    {
        AssertNotOk(await Tools.IdeSetStartupProject("Missing", MissingSolution));
    }

    [Fact]
    public async Task IdeStartDebugging_NoInstance_ReportsError()
    {
        AssertNotOk(await Tools.IdeStartDebugging(MissingSolution));
    }

    [Fact]
    public async Task IdeStopDebugging_NoInstance_ReportsError()
    {
        AssertNotOk(await Tools.IdeStopDebugging(MissingSolution));
    }

    [Fact]
    public async Task IdeRunTests_NoInstance_ReportsError()
    {
        AssertNotOk(await Tools.IdeRunTests(MissingSolution));
    }

    [Fact]
    public void IdeGetTestResults_MissingSolution_ReportsError()
    {
        AssertNotOk(Tools.IdeGetTestResults(MissingSolution));
    }

    [Fact]
    public async Task IdeSearchOutput_NoInstance_ReportsError()
    {
        AssertNotOk(await Tools.IdeSearchOutput("error", "Build", MissingSolution));
    }

    [Fact]
    public async Task IdeClearOutput_NoInstance_ReportsError()
    {
        AssertNotOk(await Tools.IdeClearOutput("Build", MissingSolution));
    }

    [Fact]
    public async Task IdeFormatDocument_NoInstance_ReportsError()
    {
        AssertNotOk(await Tools.IdeFormatDocument(MissingSolution));
    }

    [Fact]
    public async Task IdeBuild_NoInstance_ReportsError()
    {
        AssertNotOk(await Tools.IdeBuild(MissingSolution));
    }

    [Fact]
    public async Task IdeGetBuildDiagnostics_NoInstance_ReportsError()
    {
        AssertNotOk(await Tools.IdeGetBuildDiagnostics("all", MissingSolution));
    }

    [Fact]
    public async Task IdeCodeCleanup_NoInstance_ReportsError()
    {
        AssertNotOk(await Tools.IdeCodeCleanup(MissingSolution));
    }

    [Fact]
    public async Task IdeGetOutput_NoInstance_ReportsError()
    {
        AssertNotOk(await Tools.IdeGetOutput("Build", 200, MissingSolution));
    }

    [Fact]
    public async Task IdeGetLocals_NoInstance_ReportsError()
    {
        AssertNotOk(await Tools.IdeGetLocals(100, MissingSolution));
    }

    [Fact]
    public async Task IdeBridgeHealth_NoInstance_ReportsError()
    {
        AssertNotOk(await Tools.IdeBridgeHealth(MissingSolution));
    }

    [Fact]
    public async Task IdeListConfigurations_NoInstance_ReportsError()
    {
        AssertNotOk(await Tools.IdeListConfigurations(MissingSolution));
    }

    [Fact]
    public async Task IdeSetConfiguration_NoInstance_ReportsError()
    {
        AssertNotOk(await Tools.IdeSetConfiguration("Debug", MissingSolution));
    }

    [Fact]
    public async Task IdeClean_NoInstance_ReportsError()
    {
        AssertNotOk(await Tools.IdeClean(MissingSolution));
    }

    [Fact]
    public async Task IdePublish_NoInstance_ReportsError()
    {
        AssertNotOk(await Tools.IdePublish(MissingSolution));
    }

    [Fact]
    public async Task IdeKillProcess_UntrackedPid_ReportsError()
    {
        AssertNotOk(await Tools.IdeKillProcess(999, MissingSolution));
    }

    [Fact]
    public async Task IdeLaunchInstance_MissingSolutionFile_ReportsError()
    {
        // LaunchAsync returns before it looks for or starts any IDE when the .sln file does not exist.
        AssertNotOk(await Tools.IdeLaunchInstance(MissingSolution));
    }

    [Fact]
    public async Task IdeKillInstance_NoInstance_ReportsError()
    {
        // KillAsync asks the router first; with no matching instance nothing is killed.
        AssertNotOk(await Tools.IdeKillInstance(MissingSolution));
    }

    [Fact]
    public async Task IdeSetBreakpoint_NoInstance_ReportsError()
    {
        AssertNotOk(await Tools.IdeSetBreakpoint(@"C:\__tokensaver_no_such_dir__\File.cs", 1, MissingSolution));
    }

    [Fact]
    public async Task IdeRemoveBreakpoint_NoInstance_ReportsError()
    {
        AssertNotOk(await Tools.IdeRemoveBreakpoint(@"C:\__tokensaver_no_such_dir__\File.cs", 1, MissingSolution));
    }

    [Fact]
    public async Task IdeListBreakpoints_NoInstance_ReportsError()
    {
        AssertNotOk(await Tools.IdeListBreakpoints(MissingSolution));
    }

    [Fact]
    public async Task IdeStep_NoInstance_ReportsError()
    {
        AssertNotOk(await Tools.IdeStep("over", MissingSolution));
    }

    [Fact]
    public async Task IdeGetDebugState_NoInstance_ReportsError()
    {
        AssertNotOk(await Tools.IdeGetDebugState(MissingSolution));
    }

    [Fact]
    public async Task IdeEvaluate_NoInstance_ReportsError()
    {
        AssertNotOk(await Tools.IdeEvaluate("1 + 1", MissingSolution));
    }

    [Fact]
    public async Task IdeAddWatch_NoInstance_ReportsError()
    {
        AssertNotOk(await Tools.IdeAddWatch("x", MissingSolution));
    }

    [Fact]
    public async Task IdeRemoveWatch_NoInstance_ReportsError()
    {
        AssertNotOk(await Tools.IdeRemoveWatch("x", MissingSolution));
    }

    [Fact]
    public async Task IdeListWatches_NoInstance_ReportsError()
    {
        AssertNotOk(await Tools.IdeListWatches(MissingSolution));
    }

    private static void AssertNotOk(string json)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean(), json);
    }
}

