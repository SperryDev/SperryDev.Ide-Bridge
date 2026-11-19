namespace TokenSaver.TerminalControl.Models;

public sealed record TerminalSessionConfig(
    bool IsElevated = false,
    string? ProfileGuid = null,
    string? ProfileName = null,
    string? InitialDirectory = null,
    string? PlaceholderTabName = "__placeholder",
    int OutputBufferSize = 10000,
    TimeSpan? GracefulCloseTimeout = null,
    IReadOnlyDictionary<string, string>? EnvironmentVariables = null,
    TerminalLaunchMode LaunchMode = TerminalLaunchMode.InteractiveUser)
{
    public TimeSpan ActualGracefulCloseTimeout =>
        GracefulCloseTimeout ?? TimeSpan.FromSeconds(5);
}
