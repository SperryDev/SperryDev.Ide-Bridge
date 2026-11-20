using System;
using System.IO;
using System.Linq;
using System.Text.Json;

using TokenSaver.MCP.Services;

using Xunit;

namespace TokenSaver.MCP.Tests.Services;

public sealed class IdeTestRunServiceTests : IDisposable
{
    private const string TrxNamespace = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "tokensaver-trx-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Theory]
    [InlineData(null, "TestExplorer")]
    [InlineData("", "TestExplorer")]
    [InlineData("TestExplorer", "TestExplorer")]
    [InlineData("garbage", "TestExplorer")]
    [InlineData("DotnetTest", "DotnetTest")]
    [InlineData("dotnettest", "DotnetTest")]
    public void NormalizeMode_MapsKnownValuesAndDefaultsToTestExplorer(string? input, string expected)
    {
        Assert.Equal(expected, IdeTestRunService.NormalizeMode(input));
    }

    [Fact]
    public void GetLatestResult_WithoutTestResultsFolder_ReportsNotOk()
    {
        Directory.CreateDirectory(_root);

        using var json = JsonDocument.Parse(IdeTestRunService.GetLatestResult(_root));

        Assert.False(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.Contains("TestResults", json.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void GetLatestResult_WithNoTrxFiles_ReportsNotOk()
    {
        Directory.CreateDirectory(Path.Combine(_root, "TestResults"));

        using var json = JsonDocument.Parse(IdeTestRunService.GetLatestResult(_root));

        Assert.False(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("No TRX test results found.", json.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void GetLatestResult_ParsesCountersAndFailedTestsOnly()
    {
        WriteTrx("run.trx", total: 3, passed: 2, failed: 1, notExecuted: 0,
            results:
            [
                ("Passes_Fine", "Passed", null, null),
                ("Adds_Wrong", "Failed", "Assert.Equal() Failure", "   at Tests.Adds_Wrong() in Tests.cs:line 12"),
                ("Also_Passes", "Passed", null, null),
            ]);

        using var json = JsonDocument.Parse(IdeTestRunService.GetLatestResult(_root));
        var root = json.RootElement;
        var summary = root.GetProperty("summary");

        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(3, summary.GetProperty("total").GetInt32());
        Assert.Equal(2, summary.GetProperty("passed").GetInt32());
        Assert.Equal(1, summary.GetProperty("failed").GetInt32());
        Assert.Equal(0, summary.GetProperty("skipped").GetInt32());

        var failure = Assert.Single(summary.GetProperty("failures").EnumerateArray());
        Assert.Equal("Adds_Wrong", failure.GetProperty("test_name").GetString());
        Assert.Equal("Assert.Equal() Failure", failure.GetProperty("message").GetString());
        Assert.Contains("Adds_Wrong", failure.GetProperty("stack_trace").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void GetLatestResult_ReturnsTheNewestTrxFile()
    {
        var older = WriteTrx("older.trx", total: 1, passed: 1, failed: 0, notExecuted: 0, results: []);
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddMinutes(-10));
        var newer = WriteTrx("newer.trx", total: 5, passed: 4, failed: 1, notExecuted: 0, results: []);
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow);

        using var json = JsonDocument.Parse(IdeTestRunService.GetLatestResult(_root));

        Assert.Equal(newer, json.RootElement.GetProperty("trx").GetString());
        Assert.Equal(5, json.RootElement.GetProperty("summary").GetProperty("total").GetInt32());
    }

    private string WriteTrx(
        string fileName,
        int total,
        int passed,
        int failed,
        int notExecuted,
        (string Name, string Outcome, string? Message, string? StackTrace)[] results)
    {
        var resultsDir = Path.Combine(_root, "TestResults");
        Directory.CreateDirectory(resultsDir);
        var path = Path.Combine(resultsDir, fileName);

        var resultXml = string.Concat(results.Select(r =>
            $"""
              <UnitTestResult testName="{r.Name}" outcome="{r.Outcome}">
                {(r.Message is null ? "" : $"<Output><ErrorInfo><Message>{r.Message}</Message><StackTrace>{r.StackTrace}</StackTrace></ErrorInfo></Output>")}
              </UnitTestResult>
            """));

        File.WriteAllText(path,
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <TestRun id="00000000-0000-0000-0000-000000000000" xmlns="{TrxNamespace}">
              <Results>
            {resultXml}
              </Results>
              <ResultSummary outcome="Completed">
                <Counters total="{total}" passed="{passed}" failed="{failed}" notExecuted="{notExecuted}" />
              </ResultSummary>
            </TestRun>
            """);
        return path;
    }
}
