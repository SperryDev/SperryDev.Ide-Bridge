using System.Text.Json;

namespace TokenSaver.MCP.Services.LogSources;

/// <summary>Unwraps a bridge reply. A reply with ok=false becomes an IOException carrying the bridge's message.</summary>
internal static class IdeReply
{
    public static JsonElement ResultOrThrow(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("ok", out JsonElement ok) && ok.GetBoolean())
            {
                return root.TryGetProperty("result", out JsonElement result) ? result.Clone() : default;
            }

            string error = root.TryGetProperty("error", out JsonElement e) ? e.GetString() ?? "unknown error" : "unknown error";
            throw new IOException(error);
        }
        catch (JsonException ex)
        {
            throw new IOException("The IDE bridge returned a malformed reply: " + ex.Message, ex);
        }
    }
}
