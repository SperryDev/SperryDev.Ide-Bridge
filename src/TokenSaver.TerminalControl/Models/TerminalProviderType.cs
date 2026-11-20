namespace TokenSaver.TerminalControl.Models;

public enum TerminalProviderType
{
    /// <summary>
    /// Integrated provider: PowerShell or Command Prompt (headless)
    /// </summary>
    Integrated,

    /// <summary>
    /// Windows Terminal provider (GUI)
    /// </summary>
    WindowsTerminal
}

public sealed record TerminalProviderAvailability(
    TerminalProviderType Provider,
    bool IsInstalled,
    string? Version = null,
    Uri? InstallUrl = null,
    string? ErrorMessage = null)
{
    public bool IsAvailable => IsInstalled && string.IsNullOrEmpty(ErrorMessage);
}

public sealed record TerminalProviderSelection(
    TerminalProviderType PreferredProvider,
    TerminalProviderType FallbackProvider = TerminalProviderType.Integrated,
    bool AllowProviderSwitch = true)
{
}
