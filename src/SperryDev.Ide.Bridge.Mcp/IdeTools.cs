using SperryDev.Ide.Bridge;
using System.ComponentModel;
using System.Text.Json;

using ModelContextProtocol.Server;

using Microsoft.Extensions.Options;
using TokenSaver.MCP.Services;

namespace TokenSaver.MCP.Tools;

/// <summary>
///     Common IDE actions for any running IDE instance. Every result names the IDE and instance that answered
///     (<c>ide</c>, <c>instance_pid</c>, <c>solution_path</c>), so the caller always knows what it acted on.
/// </summary>
[McpServerToolType]
internal sealed class IdeTools(IOptions<IdeToolsOptions> options)
{
    // Resolves the solution a test-result call refers to: the given path, or the only open instance's solution.
    private static string? ResolveSolution(string? solutionPath)
    {
        if (!string.IsNullOrWhiteSpace(solutionPath))
        {
            return solutionPath;
        }

        var instances = VsInstanceRegistry.ListRunning();
        return instances.Count == 1 ? instances[0].SolutionPath : null;
    }

    [McpServerTool]
    [Description("List running IDE instances (currently Visual Studio) with their process id and the solution each has open.")]
    public string IdeListInstances() => IdeInstanceRouter.ListInstances();

    [McpServerTool]
    [Description("Set the startup project of the solution in an IDE instance. " +
                 "Pass solutionPath to choose the instance when several are open.")]
    public Task<string> IdeSetStartupProject(
        [Description("Project name to make the startup project")] string project,
        [Description("Optional: full path of the solution to target")] string? solutionPath = null,
        CancellationToken ct = default) =>
        IdeInstanceRouter.CallAsync("setStartupProject", new { project }, solutionPath, ct);

    [McpServerTool]
    [Description("Start debugging the solution in an IDE instance. Pass solutionPath when several instances are open.")]
    public Task<string> IdeStartDebugging(
        [Description("Optional: full path of the solution to target")] string? solutionPath = null,
        CancellationToken ct = default) =>
        IdeInstanceRouter.CallAsync("startDebugging", new { }, solutionPath, ct);

    [McpServerTool]
    [Description("Stop debugging in an IDE instance. Pass solutionPath when several instances are open.")]
    public Task<string> IdeStopDebugging(
        [Description("Optional: full path of the solution to target")] string? solutionPath = null,
        CancellationToken ct = default) =>
        IdeInstanceRouter.CallAsync("stopDebugging", new { }, solutionPath, ct);

    [McpServerTool]
    [Description("Run all tests in the solution. The server setting IdeTestRunMode picks the method: 'TestExplorer' " +
                 "(default) triggers Test Explorer in the IDE; 'DotnetTest' runs dotnet test with a TRX file. " +
                 "Pass solutionPath when several instances are open. Use IdeGetTestResults for the outcome.")]
    public Task<string> IdeRunTests(
        [Description("Optional: full path of the solution to target")] string? solutionPath = null,
        CancellationToken ct = default)
    {
        if (IdeTestRunService.NormalizeMode(options.Value.IdeTestRunMode) == IdeTestRunService.DotnetTestMode)
        {
            var solution = ResolveSolution(solutionPath);
            return solution is null
                ? Task.FromResult(JsonSerializer.Serialize(new { ok = false, error = "Pass solutionPath: no single open instance to infer it from." }))
                : IdeTestRunService.RunDotnetTestAsync(solution, ct);
        }

        return IdeInstanceRouter.CallAsync("runTests", new { }, solutionPath, ct);
    }

    [McpServerTool]
    [Description("Get the outcome of the newest test run for the solution: counts and failure messages from its TRX file. " +
                 "Covers runs from IdeRunTests in either mode, and TRX files written by other test runs in the solution's TestResults folder.")]
    public string IdeGetTestResults(
        [Description("Optional: full path of the solution to read results for")] string? solutionPath = null)
    {
        var solution = ResolveSolution(solutionPath);
        return solution is null
            ? JsonSerializer.Serialize(new { ok = false, error = "Pass solutionPath: no single open instance to infer it from." })
            : IdeTestRunService.GetLatestResult(solution);
    }

    [McpServerTool]
    [Description("Search the lines of an IDE output pane for a regex pattern. Returns the matching lines.")]
    public Task<string> IdeSearchOutput(
        [Description("Regex pattern to match against each output line")] string pattern,
        [Description("Output pane name, e.g. 'Build' or 'Debug'")] string pane = "Build",
        [Description("Optional: full path of the solution to target")] string? solutionPath = null,
        CancellationToken ct = default) =>
        IdeInstanceRouter.CallAsync("searchOutput", new { pattern, pane }, solutionPath, ct);

    [McpServerTool]
    [Description("Clear an output pane in an IDE instance.")]
    public Task<string> IdeClearOutput(
        [Description("Output pane name, e.g. 'Build' or 'Debug'")] string pane = "Build",
        [Description("Optional: full path of the solution to target")] string? solutionPath = null,
        CancellationToken ct = default) =>
        IdeInstanceRouter.CallAsync("clearOutput", new { pane }, solutionPath, ct);

    [McpServerTool]
    [Description("Format the active document in an IDE instance. Pass solutionPath when several instances are open.")]
    public Task<string> IdeFormatDocument(
        [Description("Optional: full path of the solution to target")] string? solutionPath = null,
        CancellationToken ct = default) =>
        IdeInstanceRouter.CallAsync("formatDocument", new { }, solutionPath, ct);

    [McpServerTool]
    [Description("Build the solution in an IDE instance and report whether it succeeded. " +
                 "Pass solutionPath when several instances are open. Use IdeGetBuildDiagnostics for the errors.")]
    public Task<string> IdeBuild(
        [Description("Optional: full path of the solution to target")] string? solutionPath = null,
        CancellationToken ct = default) =>
        IdeInstanceRouter.CallAsync("build", new { }, solutionPath, ct);

    [McpServerTool]
    [Description("Read build errors and warnings from an IDE instance's Error List (the last build's results).")]
    public Task<string> IdeGetBuildDiagnostics(
        [Description("'error', 'warning', or 'all' (default)")] string severity = "all",
        [Description("Optional: full path of the solution to target")] string? solutionPath = null,
        CancellationToken ct = default) =>
        IdeInstanceRouter.CallAsync("getBuildDiagnostics", new { severity }, solutionPath, ct);

    [McpServerTool]
    [Description("Run Visual Studio's Code Cleanup on the active document of an IDE instance.")]
    public Task<string> IdeCodeCleanup(
        [Description("Optional: full path of the solution to target")] string? solutionPath = null,
        CancellationToken ct = default) =>
        IdeInstanceRouter.CallAsync("codeCleanup", new { }, solutionPath, ct);

    [McpServerTool]
    [Description("Read the last lines of an IDE output pane, e.g. 'Build' or 'Debug'. For a filtered, deduplicated summary, use LogQuery with a vsout: channel.")]
    public Task<string> IdeGetOutput(
        [Description("Output pane name, e.g. 'Build' or 'Debug'")] string pane = "Build",
        [Description("Maximum number of trailing lines to return")] int maxLines = 200,
        [Description("Optional: full path of the solution to target")] string? solutionPath = null,
        CancellationToken ct = default) =>
        IdeInstanceRouter.CallAsync("getOutput", new { pane, maxLines }, solutionPath, ct);

    [McpServerTool]
    [Description("Read local variables in the current stack frame of an IDE instance. The debugger must be stopped at a breakpoint.")]
    public Task<string> IdeGetLocals(
        [Description("Maximum number of variables to return")] int maxItems = 100,
        [Description("Optional: full path of the solution to target")] string? solutionPath = null,
        CancellationToken ct = default) =>
        IdeInstanceRouter.CallAsync("getLocals", new { maxItems }, solutionPath, ct);

    [McpServerTool]
    [Description("Check the bridge health of an IDE instance: whether its listener is running, when it started, " +
                 "when it last handled a request, how many requests it has handled, and its solution. " +
                 "Answered even while the IDE is busy.")]
    public Task<string> IdeBridgeHealth(
        [Description("Optional: full path of the solution to target")] string? solutionPath = null,
        CancellationToken ct = default) =>
        IdeInstanceRouter.CallAsync("health", new { }, solutionPath, ct);

    [McpServerTool]
    [Description("List the solution build configurations (e.g. Debug, Release) of an IDE instance and the active one.")]
    public Task<string> IdeListConfigurations(
        [Description("Optional: full path of the solution to target")] string? solutionPath = null,
        CancellationToken ct = default) =>
        IdeInstanceRouter.CallAsync("listConfigurations", new { }, solutionPath, ct);

    [McpServerTool]
    [Description("Set the active build configuration (e.g. Debug or Release) of an IDE instance's solution.")]
    public Task<string> IdeSetConfiguration(
        [Description("Configuration name, e.g. 'Debug' or 'Release'")] string configuration,
        [Description("Optional: full path of the solution to target")] string? solutionPath = null,
        CancellationToken ct = default) =>
        IdeInstanceRouter.CallAsync("setConfiguration", new { configuration }, solutionPath, ct);

    [McpServerTool]
    [Description("Clean the solution in an IDE instance using the active configuration.")]
    public Task<string> IdeClean(
        [Description("Optional: full path of the solution to target")] string? solutionPath = null,
        CancellationToken ct = default) =>
        IdeInstanceRouter.CallAsync("clean", new { }, solutionPath, ct);

    [McpServerTool]
    [Description("Publish the startup project of an IDE instance with the IDE's publish command, using the active configuration.")]
    public Task<string> IdePublish(
        [Description("Optional: full path of the solution to target")] string? solutionPath = null,
        CancellationToken ct = default) =>
        IdeInstanceRouter.CallAsync("publish", new { }, solutionPath, ct);

    [McpServerTool]
    [Description("Kill a process that an IDE instance started for clean or publish. Refuses any other process id.")]
    public Task<string> IdeKillProcess(
        [Description("Process id returned by IdeClean or IdePublish")] int pid,
        [Description("Optional: full path of the solution to target")] string? solutionPath = null,
        CancellationToken ct = default) =>
        IdeInstanceRouter.CallAsync("killProcess", new { pid }, solutionPath, ct);

    [McpServerTool]
    [Description("Launch a Visual Studio instance with a solution open. Reuses an instance that already has it open. " +
                 "Waits for the instance's bridge to come up.")]
    public Task<string> IdeLaunchInstance(
        [Description("Full path of the .sln file to open")] string solutionPath,
        CancellationToken ct = default) =>
        IdeInstanceLifecycle.LaunchAsync(solutionPath, ct);

    [McpServerTool]
    [Description("Save all documents and then kill a Visual Studio instance. Refuses, killing nothing, if any document is still unsaved.")]
    public Task<string> IdeKillInstance(
        [Description("Optional: full path of the solution to target")] string? solutionPath = null,
        CancellationToken ct = default) =>
        IdeInstanceLifecycle.KillAsync(solutionPath, ct);

    [McpServerTool]
    [Description("Set a breakpoint at a file and line in an IDE instance.")]
    public Task<string> IdeSetBreakpoint(
        [Description("Full path of the source file")] string file,
        [Description("1-based line number")] int line,
        [Description("Optional: full path of the solution to target")] string? solutionPath = null,
        CancellationToken ct = default) =>
        IdeInstanceRouter.CallAsync("setBreakpoint", new { file, line }, solutionPath, ct);

    [McpServerTool]
    [Description("Remove breakpoints at a file and line in an IDE instance.")]
    public Task<string> IdeRemoveBreakpoint(
        [Description("Full path of the source file")] string file,
        [Description("1-based line number")] int line,
        [Description("Optional: full path of the solution to target")] string? solutionPath = null,
        CancellationToken ct = default) =>
        IdeInstanceRouter.CallAsync("removeBreakpoint", new { file, line }, solutionPath, ct);

    [McpServerTool]
    [Description("List breakpoints set in an IDE instance.")]
    public Task<string> IdeListBreakpoints(
        [Description("Optional: full path of the solution to target")] string? solutionPath = null,
        CancellationToken ct = default) =>
        IdeInstanceRouter.CallAsync("listBreakpoints", new { }, solutionPath, ct);

    [McpServerTool]
    [Description("Step the debugger in an IDE instance: 'over', 'into', or 'out'. The debugger must be stopped at a breakpoint.")]
    public Task<string> IdeStep(
        [Description("'over', 'into', or 'out'")] string mode,
        [Description("Optional: full path of the solution to target")] string? solutionPath = null,
        CancellationToken ct = default) =>
        IdeInstanceRouter.CallAsync("step", new { mode }, solutionPath, ct);

    [McpServerTool]
    [Description("Get the debugger mode (design, run, or break) and the current function in an IDE instance.")]
    public Task<string> IdeGetDebugState(
        [Description("Optional: full path of the solution to target")] string? solutionPath = null,
        CancellationToken ct = default) =>
        IdeInstanceRouter.CallAsync("debugState", new { }, solutionPath, ct);

    [McpServerTool]
    [Description("Evaluate an expression in the current stack frame of an IDE instance. The debugger must be stopped at a breakpoint.")]
    public Task<string> IdeEvaluate(
        [Description("C# expression to evaluate, e.g. 'items.Count'")] string expression,
        [Description("Optional: full path of the solution to target")] string? solutionPath = null,
        CancellationToken ct = default) =>
        IdeInstanceRouter.CallAsync("evaluate", new { expression }, solutionPath, ct);

    [McpServerTool]
    [Description("Add a watch expression to an IDE instance's watch list. Watches are kept by the extension, not the Watch window.")]
    public Task<string> IdeAddWatch(
        [Description("Expression to watch")] string expression,
        [Description("Optional: full path of the solution to target")] string? solutionPath = null,
        CancellationToken ct = default) =>
        IdeInstanceRouter.CallAsync("addWatch", new { expression }, solutionPath, ct);

    [McpServerTool]
    [Description("Remove a watch expression from an IDE instance's watch list.")]
    public Task<string> IdeRemoveWatch(
        [Description("Expression to remove")] string expression,
        [Description("Optional: full path of the solution to target")] string? solutionPath = null,
        CancellationToken ct = default) =>
        IdeInstanceRouter.CallAsync("removeWatch", new { expression }, solutionPath, ct);

    [McpServerTool]
    [Description("List an IDE instance's watch expressions with their current values (when stopped at a breakpoint).")]
    public Task<string> IdeListWatches(
        [Description("Optional: full path of the solution to target")] string? solutionPath = null,
        CancellationToken ct = default) =>
        IdeInstanceRouter.CallAsync("listWatches", new { }, solutionPath, ct);
}

