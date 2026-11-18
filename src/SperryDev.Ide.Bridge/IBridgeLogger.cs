using System;

namespace SperryDev.Ide.Bridge;

/// <summary>
/// Logging seam for the bridge. The host adapts its own logger (for TokenSaver, SperryDev.Logger) to this interface.
/// </summary>
public interface IBridgeLogger
{
    void LogDebug(string message);

    void LogInfo(string message);

    void LogError(string message);

    void LogException(Exception exception);
}
