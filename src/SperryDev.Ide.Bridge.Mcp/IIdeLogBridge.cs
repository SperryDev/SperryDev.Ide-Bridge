namespace TokenSaver.MCP.Services.LogSources;

/// <summary>Sends one IDE bridge action and returns the raw reply: <c>{ "ok", "pid", "result" | "error" }</c>.</summary>
internal interface IIdeLogBridge
{
    Task<string> CallAsync(string action, object args, string? solutionPath, CancellationToken ct);
}
