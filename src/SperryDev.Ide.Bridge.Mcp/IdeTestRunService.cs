using System;
using System.Collections.Generic;
using TokenSaver.Common.Core.Processes;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace TokenSaver.MCP.Services;

/// <summary>
/// Runs tests for an IDE solution with <c>dotnet test</c> and a TRX logger, and reads the newest TRX file back.
/// TRX is a documented, IDE-independent format. Visual Studio's Test Explorer results aren't exposed to extensions,
/// so IdeGetTestResults reads TRX files instead.
/// </summary>
internal static class IdeTestRunService
{
    public const string DotnetTestMode = "DotnetTest";
    public const string TestExplorerMode = "TestExplorer";
    private const string ResultsFolder = "TestResults";
    private static readonly XNamespace Trx = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    /// <summary>Returns the configured mode. Anything other than "DotnetTest" is treated as Test Explorer.</summary>
    public static string NormalizeMode(string? mode) =>
        string.Equals(mode, DotnetTestMode, StringComparison.OrdinalIgnoreCase) ? DotnetTestMode : TestExplorerMode;

    // A .sln path gives its folder; a folder (a VS Code workspace) is used as is.
    private static string SolutionDirectory(string solutionPath)
    {
        var full = Path.GetFullPath(solutionPath);
        return Directory.Exists(full)
            ? full
            : Path.GetDirectoryName(full) ?? throw new InvalidOperationException("No solution directory.");
    }

    /// <summary>Runs <c>dotnet test</c> on the solution, writing a TRX file into the solution's TestResults folder.</summary>
    public static async Task<string> RunDotnetTestAsync(string solutionPath, CancellationToken ct)
    {
        var solutionDir = SolutionDirectory(solutionPath);
        var resultsDir = Path.Combine(solutionDir, ResultsFolder);
        var trxName = $"tokensaver-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.trx";

        CommandResult run = await CommandRunner.RunAsync(
            new CommandRequest
            {
                FileName = "dotnet",
                Arguments = ["test", solutionPath, "--logger", $"trx;LogFileName={trxName}", "--results-directory", resultsDir],
                WorkingDirectory = solutionDir,
                Timeout = TimeSpan.FromMinutes(30),
                OnStarted = process => ManagedProcessRegistry.Track(process, "test run"),
            },
            ct);
        if (!run.Launched)
        {
            throw new InvalidOperationException($"dotnet test did not start: {run.LaunchError}");
        }

        var summary = ReadSummary(Path.Combine(resultsDir, trxName));
        return JsonSerializer.Serialize(new
        {
            ok = run.Success,
            exit_code = run.ExitCode,
            mode = DotnetTestMode,
            trx = Path.Combine(resultsDir, trxName),
            summary,
            timed_out = run.TimedOut,
            output_tail = Tail(run.StdOut + run.StdErr, 4000),
        });
    }

    /// <summary>Reads the newest TRX file in the solution's TestResults folder.</summary>
    public static string GetLatestResult(string solutionPath)
    {
        var solutionDir = SolutionDirectory(solutionPath);
        var resultsDir = Path.Combine(solutionDir, ResultsFolder);
        if (!Directory.Exists(resultsDir))
        {
            return JsonSerializer.Serialize(new { ok = false, error = $"No {ResultsFolder} folder under {solutionDir}." });
        }

        var newest = new DirectoryInfo(resultsDir).EnumerateFiles("*.trx", SearchOption.AllDirectories)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();
        if (newest is null)
        {
            return JsonSerializer.Serialize(new { ok = false, error = "No TRX test results found." });
        }

        return JsonSerializer.Serialize(new
        {
            ok = true,
            trx = newest.FullName,
            recorded_utc = newest.LastWriteTimeUtc,
            summary = ReadSummary(newest.FullName),
        });
    }

    private static object ReadSummary(string trxPath)
    {
        if (!File.Exists(trxPath))
        {
            return new { error = "TRX file not found." };
        }

        var doc = XDocument.Load(trxPath);
        var counters = doc.Descendants(Trx + "Counters").FirstOrDefault();
        var failures = doc.Descendants(Trx + "UnitTestResult")
            .Where(r => string.Equals((string?)r.Attribute("outcome"), "Failed", StringComparison.OrdinalIgnoreCase))
            .Select(r => new
            {
                test_name = (string?)r.Attribute("testName"),
                message = (string?)r.Descendants(Trx + "Message").FirstOrDefault(),
                stack_trace = (string?)r.Descendants(Trx + "StackTrace").FirstOrDefault(),
            })
            .ToList();

        return new
        {
            total = (int?)counters?.Attribute("total") ?? 0,
            passed = (int?)counters?.Attribute("passed") ?? 0,
            failed = (int?)counters?.Attribute("failed") ?? 0,
            skipped = (int?)counters?.Attribute("notExecuted") ?? 0,
            failures,
        };
    }

    private static string Tail(string text, int maxChars) =>
        text.Length <= maxChars ? text : text[^maxChars..];
}
