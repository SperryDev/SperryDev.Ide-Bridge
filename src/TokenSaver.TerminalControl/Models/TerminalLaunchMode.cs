namespace TokenSaver.TerminalControl.Models;

/// <summary>Where a standard session's terminal runs.</summary>
public enum TerminalLaunchMode
{
    /// <summary>Show the terminal in the logged-on user's desktop session. When TokenSaver runs as
    /// LocalSystem, the window is started in that session and the pipes are readable by that user only.</summary>
    InteractiveUser,

    /// <summary>No window. The wrapper runs headless under TokenSaver's own account (the service account)
    /// and is used only through its pipes.</summary>
    ServiceAccount,
}
