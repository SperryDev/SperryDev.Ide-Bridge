namespace TokenSaver.TerminalControl.Models;

public sealed record TerminalTabInfo(
    string TabId,
    string TabName,
    string SessionId,
    DateTime CreatedAt,
    bool IsActive,
    string? CurrentCommand = null,
    bool IsPlaceholder = false,
    NamedPipeInfo? StdoutPipe = null,
    NamedPipeInfo? StderrPipe = null)
{
    public IReadOnlyList<NamedPipeInfo> GetActivePipes()
    {
        var pipes = new List<NamedPipeInfo>();
        if (StdoutPipe?.IsValid == true) pipes.Add(StdoutPipe);
        if (StderrPipe?.IsValid == true) pipes.Add(StderrPipe);
        return pipes;
    }
}

public sealed record TerminalOutputLine(
    int LineNumber,
    DateTime Timestamp,
    TerminalStreamType Stream,
    string Content)
{
}

public enum TerminalStreamType
{
    Stdout,
    Stderr
}

public sealed record TerminalOutput(
    string TabId,
    DateTime CapturedAt,
    IReadOnlyList<TerminalOutputLine> Lines)
{
    public IReadOnlyList<TerminalOutputLine> GetErrorLines() =>
        Lines.Where(l => l.Stream == TerminalStreamType.Stderr).ToList();

    public IReadOnlyList<TerminalOutputLine> GetStdoutLines() =>
        Lines.Where(l => l.Stream == TerminalStreamType.Stdout).ToList();
}

public sealed record TerminalExecutionResult(
    bool Success,
    int ExitCode = 0,
    string? Stdout = null,
    string? Stderr = null,
    long ExecutionTimeMs = 0,
    IReadOnlyList<string>? Errors = null)
{
}
