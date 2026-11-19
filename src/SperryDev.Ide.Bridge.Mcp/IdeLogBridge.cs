using SperryDev.Ide.Bridge;

namespace TokenSaver.MCP.Services.LogSources;

/// <summary>Routes bridge actions to the running IDE instance through <see cref="IdeInstanceRouter"/>.</summary>
internal sealed class IdeLogBridge : IIdeLogBridge
{
    public Task<string> CallAsync(string action, object args, string? solutionPath, CancellationToken ct) =>
        IdeInstanceRouter.CallAsync(action, args, solutionPath, ct);
}
