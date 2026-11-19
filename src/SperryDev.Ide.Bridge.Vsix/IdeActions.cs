using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using EnvDTE;
using EnvDTE80;

using SperryDev.Ide.Bridge;

namespace SperryDev.Ide.Bridge.Vsix;

/// <summary>
/// Implements the common IDE actions over the EnvDTE automation object. Must run on the Visual Studio UI thread.
/// Every action returns a result object or throws; <see cref="BridgeServer"/> turns exceptions into an error reply.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD010:Invoke main-thread members",
    Justification = "Every entry point runs on the UI thread: TokenSaverPackage switches to it before calling Invoke, and Invoke asserts it with ThreadHelper.ThrowIfNotOnUIThread.")]
internal sealed class IdeActions(DTE2 dte)
{
    // Watch expressions for this instance. Kept here (not in the Watch window) and evaluated on read.
    private readonly List<string> _watches = [];

    public object Invoke(string action, JsonElement args)
    {
        // Callers switch to the UI thread first (TokenSaverPackage); this asserts it at runtime.
        Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
        return InvokeOnUiThread(action, args);
    }

    private object InvokeOnUiThread(string action, JsonElement args) => action switch
    {
        "status" => new { solution_path = NullIfEmpty(dte.Solution?.FullName) },
        "setStartupProject" => SetStartupProject(RequireString(args, "project")),
        "startDebugging" => StartDebugging(),
        "stopDebugging" => StopDebugging(),
        "runTests" => RunTests(),
        "formatDocument" => FormatDocument(),
        "build" => Build(),
        "getBuildDiagnostics" => GetBuildDiagnostics(OptionalString(args, "severity") ?? "all"),
        "codeCleanup" => CodeCleanup(),
        "getOutput" => GetOutput(OptionalString(args, "pane") ?? "Build", OptionalInt(args, "maxLines") ?? 200, OptionalInt(args, "fromOffset"), OptionalInt(args, "maxChars") ?? 20000, OptionalString(args, "anchor")),
        "listOutputPanes" => ListOutputPanes(),
        "searchOutput" => SearchOutput(OptionalString(args, "pane") ?? "Build", RequireString(args, "pattern")),
        "clearOutput" => ClearOutput(OptionalString(args, "pane") ?? "Build"),
        "getLocals" => GetLocals(OptionalInt(args, "maxItems") ?? 100),
        "setBreakpoint" => SetBreakpoint(RequireString(args, "file"), RequireInt(args, "line")),
        "removeBreakpoint" => RemoveBreakpoint(RequireString(args, "file"), RequireInt(args, "line")),
        "listBreakpoints" => ListBreakpoints(),
        "step" => Step(RequireString(args, "mode")),
        "debugState" => DebugState(),
        "evaluate" => Evaluate(RequireString(args, "expression")),
        "addWatch" => AddWatch(RequireString(args, "expression")),
        "removeWatch" => RemoveWatch(RequireString(args, "expression")),
        "listWatches" => ListWatches(),
        "listConfigurations" => ListConfigurations(),
        "setConfiguration" => SetConfiguration(RequireString(args, "configuration")),
        "clean" => Clean(),
        "publish" => Publish(),
        "prepareKill" => PrepareKill(),
        "killProcess" => KillProcess(RequireInt(args, "pid")),
        _ => throw new NotSupportedException($"Unknown action '{action}'."),
    };

    // Clean and publish run through the IDE's build, not a child process this bridge starts, so no process is
    // tracked here. Every pid is refused; only processes the bridge started may be killed.
    private object KillProcess(int pid) =>
        throw new InvalidOperationException($"Process {pid} was not started by this bridge; it will not be killed.");

    private object SetStartupProject(string projectName)
    {
        var project = FindProject(projectName)
            ?? throw new InvalidOperationException($"No project named '{projectName}' in the open solution.");

        dte.Solution.SolutionBuild.StartupProjects = new object[] { project.UniqueName };
        return new { startup_project = project.UniqueName };
    }

    private object StartDebugging()
    {
        dte.Debugger.Go(false);
        return new { debugger_mode = dte.Debugger.CurrentMode.ToString() };
    }

    private object StopDebugging()
    {
        dte.Debugger.Stop(false);
        return new { debugger_mode = dte.Debugger.CurrentMode.ToString() };
    }

    private object RunTests()
    {
        // Runs every test in the solution through Test Explorer. Filtering is not supported yet.
        dte.ExecuteCommand("TestExplorer.RunAllTests");
        return new { command = "TestExplorer.RunAllTests" };
    }

    private object FormatDocument()
    {
        if (dte.ActiveDocument is null)
        {
            throw new InvalidOperationException("No active document to format.");
        }

        dte.ExecuteCommand("Edit.FormatDocument");
        return new { document = dte.ActiveDocument.FullName };
    }

    private object Build()
    {
        dte.Solution.SolutionBuild.Build(WaitForBuildToFinish: true);
        var failed = dte.Solution.SolutionBuild.LastBuildInfo;
        return new { succeeded = failed == 0, failed_projects = failed };
    }

    // Reads the Error List, which holds the last build's errors and warnings.
    // vsBuildErrorLevel: 1 = error, 2 = warning (per EnvDTE); other levels are excluded.
    private object GetBuildDiagnostics(string severity)
    {
        var wanted = severity;
        var items = new List<object>();
        var errors = dte.ToolWindows.ErrorList.ErrorItems;
        for (var i = 1; i <= errors.Count; i++)
        {
            ErrorItem item = errors.Item(i);
            var kind = item.ErrorLevel switch
            {
                vsBuildErrorLevel.vsBuildErrorLevelHigh => "error",
                vsBuildErrorLevel.vsBuildErrorLevelMedium => "warning",
                _ => null,
            };

            if (kind is null || (!string.Equals(wanted, "all", StringComparison.OrdinalIgnoreCase) && !string.Equals(wanted, kind + "s", StringComparison.OrdinalIgnoreCase) && !string.Equals(wanted, kind, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            items.Add(new { severity = kind, file = item.FileName, line = item.Line, column = item.Column, message = item.Description });
        }

        return new { count = items.Count, diagnostics = items };
    }

    // Runs Visual Studio's Code Cleanup on the active document. The command name is not verified against a live IDE.
    private object CodeCleanup()
    {
        if (dte.ActiveDocument is null)
        {
            throw new InvalidOperationException("No active document to clean up.");
        }

        dte.ExecuteCommand("Edit.RunCodeCleanup");
        return new { document = dte.ActiveDocument.FullName };
    }

    private object GetOutput(string paneName, int maxLines, int? fromOffset = null, int maxChars = 20000, string? anchor = null)
    {
        foreach (OutputWindowPane pane in dte.ToolWindows.OutputWindow.OutputWindowPanes)
        {
            if (!string.Equals(pane.Name, paneName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var doc = pane.TextDocument;
            var text = doc.StartPoint.CreateEditPoint().GetText(doc.EndPoint);
            if (fromOffset is int offset)
            {
                var slice = OutputPaneSlice.Slice(text, offset, maxChars, anchor);
                return new { pane = pane.Name, text = slice.Text, nextOffset = slice.NextOffset, anchor = slice.Anchor, reset = slice.Reset };
            }

            var lines = text.Split('\n');
            var tail = lines.Skip(Math.Max(0, lines.Length - maxLines));
            return new { pane = pane.Name, lines = tail.Select(l => l.TrimEnd('\r')).ToList() };
        }

        throw new InvalidOperationException($"No output pane named '{paneName}'.");
    }

    private object ListOutputPanes()
    {
        var names = new List<string>();
        foreach (OutputWindowPane pane in dte.ToolWindows.OutputWindow.OutputWindowPanes)
        {
            names.Add(pane.Name);
        }

        return new { count = names.Count, panes = names };
    }

    private object SearchOutput(string paneName, string pattern)
    {
        var pane = FindOutputPane(paneName);
        var doc = pane.TextDocument;
        var text = doc.StartPoint.CreateEditPoint().GetText(doc.EndPoint);
        var regex = new System.Text.RegularExpressions.Regex(pattern);
        var matches = text.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => regex.IsMatch(l))
            .ToList();
        return new { pane = pane.Name, pattern, match_count = matches.Count, lines = matches };
    }

    private object ClearOutput(string paneName)
    {
        FindOutputPane(paneName).Clear();
        return new { pane = paneName, cleared = true };
    }

    private OutputWindowPane FindOutputPane(string paneName)
    {
        foreach (OutputWindowPane pane in dte.ToolWindows.OutputWindow.OutputWindowPanes)
        {
            if (string.Equals(pane.Name, paneName, StringComparison.OrdinalIgnoreCase))
            {
                return pane;
            }
        }

        throw new InvalidOperationException($"No output pane named '{paneName}'.");
    }

    private object GetLocals(int maxItems)
    {
        if (dte.Debugger.CurrentMode != dbgDebugMode.dbgBreakMode)
        {
            throw new InvalidOperationException("Not stopped at a breakpoint; locals are only available in break mode.");
        }

        var frame = dte.Debugger.CurrentStackFrame
            ?? throw new InvalidOperationException("No current stack frame.");

        var locals = new List<object>();
        foreach (Expression expression in frame.Locals)
        {
            if (locals.Count >= maxItems)
            {
                break;
            }

            locals.Add(new { name = expression.Name, value = expression.Value, type = expression.Type });
        }

        return new { function = frame.FunctionName, count = locals.Count, locals };
    }

    private object SetBreakpoint(string file, int line)
    {
        var breakpoints = dte.Debugger.Breakpoints.Add(File: file, Line: line);
        return new { file, line, added = breakpoints.Count };
    }

    private object RemoveBreakpoint(string file, int line)
    {
        var breakpoints = dte.Debugger.Breakpoints;
        var removed = 0;
        for (var i = breakpoints.Count; i >= 1; i--)
        {
            var bp = breakpoints.Item(i);
            if (SameFile(bp.File, file) && bp.FileLine == line)
            {
                bp.Delete();
                removed++;
            }
        }

        return new { file, line, removed };
    }

    private object ListBreakpoints()
    {
        var breakpoints = dte.Debugger.Breakpoints;
        var list = new List<object>();
        for (var i = 1; i <= breakpoints.Count; i++)
        {
            var bp = breakpoints.Item(i);
            list.Add(new { file = bp.File, line = bp.FileLine, enabled = bp.Enabled, condition = bp.Condition, function = bp.FunctionName });
        }

        return new { count = list.Count, breakpoints = list };
    }

    private object Step(string mode)
    {
        switch (mode.ToUpperInvariant())
        {
            case "OVER":
                dte.Debugger.StepOver(false);
                break;
            case "INTO":
                dte.Debugger.StepInto(false);
                break;
            case "OUT":
                dte.Debugger.StepOut(false);
                break;
            default:
                throw new ArgumentException("Step mode must be 'over', 'into', or 'out'.");
        }

        return new { mode, debugger_mode = dte.Debugger.CurrentMode.ToString() };
    }

    private object DebugState()
    {
        var breakMode = dte.Debugger.CurrentMode == dbgDebugMode.dbgBreakMode;
        return new
        {
            debugger_mode = dte.Debugger.CurrentMode.ToString(),
            function = breakMode ? dte.Debugger.CurrentStackFrame?.FunctionName : null,
        };
    }

    private object Evaluate(string expression)
    {
        RequireBreakMode();
        var result = dte.Debugger.GetExpression(expression, false, 5000);
        return new
        {
            expression,
            valid = result.IsValidValue,
            value = result.Value,
            type = result.Type,
        };
    }

    private object AddWatch(string expression)
    {
        if (!_watches.Contains(expression))
        {
            _watches.Add(expression);
        }

        return new { count = _watches.Count, watches = _watches.ToList() };
    }

    private object RemoveWatch(string expression)
    {
        var removed = _watches.Remove(expression);
        return new { removed, count = _watches.Count, watches = _watches.ToList() };
    }

    // Values are evaluated now. Outside break mode they are reported as unavailable, not as errors.
    private object ListWatches()
    {
        var breakMode = dte.Debugger.CurrentMode == dbgDebugMode.dbgBreakMode;
        var items = _watches.Select(expression =>
        {
            if (!breakMode)
            {
                return new { expression, valid = false, value = (string?)null, type = (string?)null, reason = (string?)"not stopped at a breakpoint" };
            }

            var result = dte.Debugger.GetExpression(expression, false, 5000);
            return new { expression, valid = result.IsValidValue, value = (string?)result.Value, type = (string?)result.Type, reason = (string?)null };
        }).ToList();

        return new { count = items.Count, watches = items };
    }

    private object ListConfigurations()
    {
        var configurations = dte.Solution.SolutionBuild.SolutionConfigurations;
        var names = new List<string>();
        for (var i = 1; i <= configurations.Count; i++)
        {
            names.Add(configurations.Item(i).Name);
        }

        return new { active = dte.Solution.SolutionBuild.ActiveConfiguration?.Name, configurations = names };
    }

    private object SetConfiguration(string name)
    {
        var configurations = dte.Solution.SolutionBuild.SolutionConfigurations;
        for (var i = 1; i <= configurations.Count; i++)
        {
            var configuration = configurations.Item(i);
            if (string.Equals(configuration.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                configuration.Activate();
                return new { active = dte.Solution.SolutionBuild.ActiveConfiguration?.Name };
            }
        }

        throw new ArgumentException($"No solution configuration named '{name}'.");
    }

    private object Clean()
    {
        dte.Solution.SolutionBuild.Clean(true);
        var failed = dte.Solution.SolutionBuild.LastBuildInfo;
        return new { succeeded = failed == 0, configuration = dte.Solution.SolutionBuild.ActiveConfiguration?.Name };
    }

    // Publishes the startup project through the IDE's publish command. The command name is not verified against a live IDE.
    private object Publish()
    {
        dte.ExecuteCommand("Build.PublishSelection");
        return new { command = "Build.PublishSelection", configuration = dte.Solution.SolutionBuild.ActiveConfiguration?.Name };
    }

    // Saves every document, then reports any that are still unsaved. The MCP kills the instance only when safe is true.
    private object PrepareKill()
    {
        dte.ExecuteCommand("File.SaveAll");

        var documents = dte.Documents;
        var unsaved = new List<string>();
        for (var i = 1; i <= documents.Count; i++)
        {
            var doc = documents.Item(i);
            if (!doc.Saved)
            {
                unsaved.Add(doc.FullName);
            }
        }

        return new { safe = unsaved.Count == 0, unsaved };
    }

    private void RequireBreakMode()
    {
        if (dte.Debugger.CurrentMode != dbgDebugMode.dbgBreakMode)
        {
            throw new InvalidOperationException("Not stopped at a breakpoint; expressions can only be evaluated in break mode.");
        }
    }

    private static bool SameFile(string? a, string b) =>
        !string.IsNullOrEmpty(a) && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private Project? FindProject(string name)
    {
        foreach (Project project in dte.Solution.Projects)
        {
            var found = FindInProject(project, name);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    // Walks solution folders recursively so projects nested in folders can be targeted.
    private static Project? FindInProject(Project project, string name)
    {
        if (string.Equals(project.Name, name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(project.UniqueName, name, StringComparison.OrdinalIgnoreCase))
        {
            return project;
        }

        foreach (Project child in Subprojects(project))
        {
            var found = FindInProject(child, name);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static IEnumerable<Project> Subprojects(Project folder)
    {
        foreach (ProjectItem item in folder.ProjectItems)
        {
            if (item.SubProject is Project sub)
            {
                yield return sub;
            }
        }
    }

    private static string? OptionalString(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? OptionalInt(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;

    private static int RequireInt(JsonElement args, string name)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number)
        {
            throw new ArgumentException($"Missing integer argument '{name}'.");
        }

        return value.GetInt32();
    }

    private static string RequireString(JsonElement args, string name)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new ArgumentException($"Missing string argument '{name}'.");
        }

        return value.GetString()!;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}

