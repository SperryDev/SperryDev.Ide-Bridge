using System;

namespace SperryDev.Ide.Bridge;

/// <summary>
/// Start/stop control and status of this instance's IDE bridge, shown on the Services tab. A stop by the user is
/// remembered for this session, so the package watchdog does not restart the listener. Stopping also removes this
/// instance's registry entry, so MCP stops routing to it; starting writes the entry again.
/// </summary>
public static class BridgeControl
{
    private static BridgeServer? _bridge;
    private static string _pipeName = string.Empty;
    private static int _pid;
    private static Func<string?> _solutionPath = () => null;
    private static volatile bool _userStopped;

    /// <summary>True when the user stopped the bridge; the watchdog must not restart it while this is set.</summary>
    public static bool UserStopped => _userStopped;

    /// <summary>Called once by the package after the bridge is created.</summary>
    public static void Attach(BridgeServer bridge, int pid, string pipeName, Func<string?> solutionPath)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _pid = pid;
        _pipeName = pipeName ?? throw new ArgumentNullException(nameof(pipeName));
        _solutionPath = solutionPath ?? throw new ArgumentNullException(nameof(solutionPath));
    }

    public static string Describe()
    {
        if (_bridge is null)
        {
            return "Not attached";
        }

        if (_bridge.IsRunning)
        {
            return $"Running, pipe {_pipeName}";
        }

        return _userStopped ? "Stopped" : "Stopped unexpectedly; the watchdog will restart it";
    }

    public static string Start()
    {
        if (_bridge is null)
        {
            return "IDE bridge is not attached.";
        }

        _userStopped = false;
        _bridge.Start();
        InstanceRegistry.Write(_pid, _solutionPath());
        return "IDE bridge started.";
    }

    public static string Stop()
    {
        if (_bridge is null)
        {
            return "IDE bridge is not attached.";
        }

        _userStopped = true;
        _bridge.Stop();
        InstanceRegistry.Remove(_pid);
        return "IDE bridge stopped. MCP cannot reach this Visual Studio instance until the bridge is started.";
    }
}
