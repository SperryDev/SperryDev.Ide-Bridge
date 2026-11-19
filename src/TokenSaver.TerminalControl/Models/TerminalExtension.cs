namespace TokenSaver.TerminalControl.Models;

public sealed record TerminalExtension(
    string Name,
    string? Version = null,
    string? Author = null,
    string? Description = null,
    string? Publisher = null,
    bool IsEnabled = true,
    string? InstallPath = null)
{
}
