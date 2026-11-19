using SperryDev.Ide.Bridge;
using System.Text.Json;

namespace TokenSaver.MCP.Services;

/// <summary>One reply from an IDE bridge: did it work, what did it return, which instance answered.</summary>
internal sealed record BridgeReply(bool Ok, JsonElement? Result, string? Error, int? Pid, string? Ide);

/// <summary>The seam between the debugger control service and a running IDE (a Visual Studio or VS Code window).</summary>
internal interface IIdeBridge
{
    /// <summary>Runs <paramref name="action" /> in the IDE of the given kind ("VisualStudio" or "VsCode").</summary>
    Task<BridgeReply> CallAsync(string ide, string action, object args, CancellationToken ct);
}

/// <summary>Production bridge: <see cref="IdeInstanceRouter" /> over the IDE's named pipe.</summary>
internal sealed class IdeInstanceBridge : IIdeBridge
{
    public async Task<BridgeReply> CallAsync(string ide, string action, object args, CancellationToken ct)
    {
        string json = await IdeInstanceRouter.CallAsync(action, args, solutionPath: null, ct, ide);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        bool ok = root.TryGetProperty("ok", out JsonElement okProp) && okProp.ValueKind == JsonValueKind.True;
        JsonElement? result = ok && root.TryGetProperty("result", out JsonElement r) && r.ValueKind != JsonValueKind.Null ? r.Clone() : null;
        string? error = root.TryGetProperty("error", out JsonElement e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
        int? pid = root.TryGetProperty("instance_pid", out JsonElement p) && p.TryGetInt32(out int pidValue) ? pidValue : null;
        string? answeredBy = root.TryGetProperty("ide", out JsonElement i) && i.ValueKind == JsonValueKind.String ? i.GetString() : null;
        return new BridgeReply(ok, result, error, pid, answeredBy);
    }
}
