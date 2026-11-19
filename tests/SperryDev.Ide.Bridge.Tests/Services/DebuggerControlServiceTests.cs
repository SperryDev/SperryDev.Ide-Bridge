using System.Text.Json;

using TokenSaver.Common.Core.Contracts;
using TokenSaver.MCP.Services;

using Xunit;

namespace TokenSaver.MCP.Tests;

/// <summary>The debugger tools act on the IDE bridge and report what it says; they never invent success.</summary>
public sealed class DebuggerControlServiceTests
{
    private sealed class FakeBridge(Func<string, string, BridgeReply> respond) : IIdeBridge
    {
        public List<(string Ide, string Action, string Args)> Calls { get; } = [];

        public Task<BridgeReply> CallAsync(string ide, string action, object args, CancellationToken ct)
        {
            Calls.Add((ide, action, JsonSerializer.Serialize(args)));
            return Task.FromResult(respond(ide, action));
        }
    }

    private static BridgeReply Ok(string json = "{}", int pid = 4242) =>
        new(true, JsonElement.Parse(json), null, pid, "VisualStudio");

    private static BridgeReply Fail(string error) => new(false, null, error, null, null);

    [Fact]
    public async Task NoIdeRunning_StartReportsFailureWithTheBridgesReason()
    {
        FakeBridge bridge = new((_, _) => Fail("No VsCode instance is running."));
        DebuggerControlService service = new(bridge);

        DebugSessionResult result = await service.StartDebugSessionAsync("VsCode", "launch");

        Assert.False(result.Success);
        Assert.Contains("No VsCode instance", result.Message, StringComparison.Ordinal);
        Assert.Equal(("VsCode", "startDebugging"), (bridge.Calls[0].Ide, bridge.Calls[0].Action));
        Assert.Contains("\"configuration\":\"launch\"", bridge.Calls[0].Args, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Start_SuccessCarriesTheInstanceInTheSessionId()
    {
        DebuggerControlService service = new(new FakeBridge((_, _) => Ok("{\"debugger_mode\":\"dbgRunMode\"}", pid: 77)));

        DebugSessionResult result = await service.StartDebugSessionAsync("VisualStudio2026", "x");

        Assert.True(result.Success);
        Assert.Equal("VisualStudio-77", result.SessionId);
    }

    [Theory]
    [InlineData("VsCode", "VsCode")]
    [InlineData("vscode", "VsCode")]
    [InlineData("VisualStudio2026", "VisualStudio")]
    [InlineData("VisualStudio", "VisualStudio")]
    public void IdeTypesMapToTheBridgesIdeKind(string ideType, string expected)
    {
        Assert.Equal(expected, DebuggerControlService.BridgeIde(ideType));
    }

    [Fact]
    public void AnUnknownIdeType_IsRejected()
    {
        _ = Assert.Throws<ArgumentException>(() => DebuggerControlService.BridgeIde("Eclipse"));
    }

    [Fact]
    public async Task SetBreakpoint_SendsFileAndLineAndReportsTheIdesAnswer()
    {
        FakeBridge bridge = new((_, _) => Ok());
        DebuggerControlService service = new(bridge);

        BreakpointResult result = await service.SetBreakpointAsync("VisualStudio2026", @"C:\r\a.cs", 12);

        Assert.True(result.Verified);
        Assert.Equal(@"C:\r\a.cs:12", result.BreakpointId);
        Assert.Contains("\"line\":12", bridge.Calls[0].Args, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetBreakpoint_ConditionsAreRefusedNotIgnored()
    {
        FakeBridge bridge = new((_, _) => Ok());
        DebuggerControlService service = new(bridge);

        BreakpointResult result = await service.SetBreakpointAsync("VsCode", @"C:\r\a.cs", 3, condition: "x > 5");

        Assert.False(result.Verified);
        Assert.Empty(bridge.Calls);
    }

    [Fact]
    public async Task RemoveBreakpoint_ParsesTheIdAndFailsLoudlyWhenTheIdeRefuses()
    {
        FakeBridge ok = new((_, _) => Ok());
        await new DebuggerControlService(ok).RemoveBreakpointAsync("VsCode", @"C:\r\a.cs:12");
        Assert.Contains("\"line\":12", ok.Calls[0].Args, StringComparison.Ordinal);

        DebuggerControlService failing = new(new FakeBridge((_, _) => Fail("not connected")));
        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => failing.RemoveBreakpointAsync("VsCode", @"C:\r\a.cs:12"));
        Assert.Contains("not connected", ex.Message, StringComparison.Ordinal);

        _ = await Assert.ThrowsAsync<ArgumentException>(() => new DebuggerControlService(ok).RemoveBreakpointAsync("VsCode", "no-line"));
    }

    [Fact]
    public async Task ListBreakpoints_MapsTheBridgeReply()
    {
        DebuggerControlService service = new(new FakeBridge((_, _) => Ok(
            "{\"breakpoints\":[{\"file\":\"C:\\\\r\\\\a.cs\",\"line\":5,\"enabled\":true,\"condition\":\"x>1\"},{\"file\":\"C:\\\\r\\\\b.cs\",\"line\":9,\"enabled\":false,\"condition\":null}]}")));

        IReadOnlyList<BreakpointInfo> list = await service.ListBreakpointsAsync("VisualStudio2026");

        Assert.Equal(2, list.Count);
        Assert.Equal(@"C:\r\a.cs:5", list[0].BreakpointId);
        Assert.Equal("x>1", list[0].Condition);
        Assert.False(list[1].Verified);
        Assert.Null(list[1].Condition);
    }

    [Theory]
    [InlineData(DebugCommand.StepIn, "into")]
    [InlineData(DebugCommand.StepOver, "over")]
    [InlineData(DebugCommand.StepOut, "out")]
    public async Task Steps_SendBothModeAndKindSoEitherIdeUnderstandsThem(DebugCommand command, string expected)
    {
        FakeBridge bridge = new((_, _) => Ok());

        ExecutionResult result = await new DebuggerControlService(bridge).ExecuteStepAsync("VsCode", command);

        Assert.True(result.Success);
        Assert.Contains($"\"mode\":\"{expected}\"", bridge.Calls[0].Args, StringComparison.Ordinal);
        Assert.Contains($"\"kind\":\"{expected}\"", bridge.Calls[0].Args, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Continue_UsesStartDebuggingInVisualStudioAndAStepInVsCode()
    {
        FakeBridge vs = new((_, _) => Ok());
        _ = await new DebuggerControlService(vs).ExecuteStepAsync("VisualStudio2026", DebugCommand.Continue);
        Assert.Equal("startDebugging", vs.Calls[0].Action);

        FakeBridge code = new((_, _) => Ok());
        _ = await new DebuggerControlService(code).ExecuteStepAsync("VsCode", DebugCommand.Continue);
        Assert.Equal("step", code.Calls[0].Action);
        Assert.Contains("continue", code.Calls[0].Args, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DebugCommand.Pause)]
    [InlineData(DebugCommand.Restart)]
    public async Task UnsupportedCommands_AreRefusedWithoutCallingTheIde(DebugCommand command)
    {
        FakeBridge bridge = new((_, _) => Ok());

        ExecutionResult result = await new DebuggerControlService(bridge).ExecuteStepAsync("VsCode", command);

        Assert.False(result.Success);
        Assert.Empty(bridge.Calls);
    }

    [Theory]
    [InlineData("{\"debugger_mode\":\"dbgRunMode\",\"function\":null}", true, false)]
    [InlineData("{\"debugger_mode\":\"dbgBreakMode\",\"function\":\"Main\"}", false, true)]
    [InlineData("{\"mode\":\"debugging\"}", true, false)]
    [InlineData("{\"mode\":\"none\"}", false, false)]
    public async Task DebugState_ReadsBothIdesShapes(string json, bool running, bool hasFrame)
    {
        DebuggerControlService service = new(new FakeBridge((_, _) => Ok(json)));

        DebugStateResult state = await service.GetDebugStateAsync("VisualStudio2026");

        Assert.Equal(running, state.IsRunning);
        Assert.Equal(hasFrame, state.CallStack.Count == 1);
    }

    [Fact]
    public async Task DebugState_WhenTheIdeFails_Throws()
    {
        DebuggerControlService service = new(new FakeBridge((_, _) => Fail("No Visual Studio instance is running with a solution open.")));

        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetDebugStateAsync("VisualStudio2026"));
    }
}
