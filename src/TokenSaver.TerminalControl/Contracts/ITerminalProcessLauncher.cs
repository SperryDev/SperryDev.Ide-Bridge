namespace TokenSaver.TerminalControl.Contracts;

using System.Diagnostics;

/// <summary>Starts terminal host processes. Injected so tests can avoid launching real windows.</summary>
public interface ITerminalProcessLauncher
{
    Process? Start(ProcessStartInfo startInfo);
}

public sealed class ProcessTerminalLauncher : ITerminalProcessLauncher
{
    public Process? Start(ProcessStartInfo startInfo) => Process.Start(startInfo);
}
