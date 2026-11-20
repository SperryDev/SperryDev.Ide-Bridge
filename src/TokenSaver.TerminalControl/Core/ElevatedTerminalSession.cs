namespace TokenSaver.TerminalControl.Core;

using Microsoft.Extensions.Logging;
using TokenSaver.TerminalControl.Contracts;
using TokenSaver.TerminalControl.Output;

/// <summary>
/// Standard session whose window is opened elevated (one UAC prompt at initialize).
/// Tab handling is inherited unchanged from <see cref="StandardTerminalSession"/>.
/// </summary>
public sealed class ElevatedTerminalSession : StandardTerminalSession
{
    public ElevatedTerminalSession(
        ILogger logger,
        NamedPipeManager? pipeManager = null,
        ITerminalProcessLauncher? launcher = null)
        : base(logger, pipeManager, launcher, elevated: true)
    {
    }
}
