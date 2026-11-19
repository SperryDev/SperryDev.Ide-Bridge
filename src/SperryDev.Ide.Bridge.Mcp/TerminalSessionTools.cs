using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using TokenSaver.TerminalControl.Contracts;
using TokenSaver.TerminalControl.Models;

namespace TokenSaver.MCP.Tools;

/// <summary>
///     MCP surface for terminal sessions. A session's launchMode decides where its terminal runs: InteractiveUser
///     shows it on the logged-on user's desktop, ServiceAccount runs it headless under TokenSaver's own account.
/// </summary>
[McpServerToolType]
internal sealed class TerminalSessionTools
{
    private readonly ITerminalSessionManager _manager;
    private readonly object _initGate = new();
    private Task? _initTask;

    public TerminalSessionTools(ITerminalSessionManager manager)
    {
        _manager = manager;
    }

    [McpServerTool]
    [Description(
        "Run commands in a terminal session. action: open (create a session with launchMode InteractiveUser " +
        "(visible on the user's desktop, default) or ServiceAccount (headless, under the service account) and a first tab; " +
        "tabName optional), tab (new tab in sessionId; tabName optional), exec (run command in sessionId + tabId; " +
        "timeoutSeconds optional, default 60), list (open sessions), close (end sessionId).")]
    public async Task<string> TerminalSession(
        string action = "list",
        string? launchMode = null,
        string? sessionId = null,
        string? tabId = null,
        string? tabName = null,
        string? command = null,
        int timeoutSeconds = 60,
        CancellationToken ct = default)
    {
        switch (action.ToUpperInvariant())
        {
            case "OPEN":
                if (!TryParseMode(launchMode, out var mode))
                {
                    return $"Error: launchMode must be InteractiveUser or ServiceAccount, got '{launchMode}'.";
                }

                return await RunAsync(async () =>
                {
                    await EnsureInitializedAsync(ct);
                    var session = await _manager.CreateSessionAsync(new TerminalSessionConfig(LaunchMode: mode), ct);
                    var tab = await session.CreateTabAsync(tabName ?? "main", null, ct);
                    return $"Opened session {session.SessionId} (launchMode {mode}) with tab {tab.TabId}.";
                });

            case "TAB":
                if (sessionId is null)
                {
                    return "Error: tab needs sessionId.";
                }

                return await RunAsync(async () =>
                {
                    var session = _manager.GetSession(sessionId)
                        ?? throw new InvalidOperationException($"no session '{sessionId}'. Use action=list.");
                    var tab = await session.CreateTabAsync(tabName ?? "main", null, ct);
                    return $"Created tab {tab.TabId} in {sessionId}.";
                });

            case "EXEC":
                if (sessionId is null || tabId is null || command is null)
                {
                    return "Error: exec needs sessionId, tabId and command.";
                }

                return await RunAsync(async () =>
                {
                    var session = _manager.GetSession(sessionId)
                        ?? throw new InvalidOperationException($"no session '{sessionId}'. Use action=list.");
                    var result = await session.ExecuteInTabAsync(
                        tabId, command, null, TimeSpan.FromSeconds(timeoutSeconds), ct);

                    var text = new StringBuilder();
                    text.Append(System.Globalization.CultureInfo.InvariantCulture, $"exit {result.ExitCode}").AppendLine();
                    text.Append(result.Stdout);
                    if (!string.IsNullOrEmpty(result.Stderr))
                    {
                        text.AppendLine().Append("stderr: ").Append(result.Stderr);
                    }

                    return text.ToString();
                });

            case "LIST":
                var sessions = _manager.GetActiveSessions();
                if (sessions.Count == 0)
                {
                    return "No open terminal sessions.";
                }

                return string.Join("\n", sessions.Select(s =>
                    $"{s.SessionId}  state={s.State}  elevated={s.IsElevated}"));

            case "CLOSE":
                if (sessionId is null)
                {
                    return "Error: close needs sessionId.";
                }

                return await RunAsync(async () =>
                {
                    await _manager.CloseSessionAsync(sessionId, graceful: true, ct);
                    return $"Closed {sessionId}.";
                });

            default:
                return "Error: action must be open, tab, exec, list or close.";
        }
    }

    /// <summary>Accepts InteractiveUser or ServiceAccount (case-insensitive); empty means InteractiveUser.</summary>
    internal static bool TryParseMode(string? value, out TerminalLaunchMode mode)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            mode = TerminalLaunchMode.InteractiveUser;
            return true;
        }

        return Enum.TryParse(value, ignoreCase: true, out mode) && Enum.IsDefined(mode);
    }

    /// <summary>
    /// Initializes the manager once. The init runs without the caller's token so a cancelled call cannot poison it;
    /// a failed init is cleared so the next call retries.
    /// </summary>
    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        Task init;
        lock (_initGate)
        {
            _initTask ??= _manager.InitializeAsync(CancellationToken.None);
            init = _initTask;
        }

        try
        {
            await init.WaitAsync(ct);
        }
        catch (Exception) when (init.IsFaulted)
        {
            lock (_initGate)
            {
                if (ReferenceEquals(_initTask, init))
                {
                    _initTask = null;
                }
            }

            throw;
        }
    }

    private static async Task<string> RunAsync(Func<Task<string>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return $"Error: {ex.Message}";
        }
    }
}
