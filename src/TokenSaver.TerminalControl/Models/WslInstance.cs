namespace TokenSaver.TerminalControl.Models;

public sealed record WslInstance(
    string Name,
    string State,
    string Version,
    string? DefaultUser = null,
    bool IsDefault = false)
{
    public bool IsRunning => State.Equals("Running", StringComparison.OrdinalIgnoreCase);
}
