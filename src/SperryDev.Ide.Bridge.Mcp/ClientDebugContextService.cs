using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using TokenSaver.Common.Core.Contracts;

namespace TokenSaver.MCP.Services;

/// <summary>
/// Tracks active clients and their associated debug sessions, processes, and output buffers.
/// Enables per-client filtering and monitoring in debugger endpoints.
/// </summary>
internal sealed class ClientDebugContextService : IClientDebugContextService, IAsyncDisposable
{
    private readonly ILogger<ClientDebugContextService> _logger;
    private readonly ConcurrentDictionary<string, ClientDebugContext> _clients;
    private readonly ConcurrentDictionary<string, SessionDebugInfo> _sessions;
    private readonly int _inactivityTimeoutMinutes;
    private readonly Timer? _cleanupTimer;

    public ClientDebugContextService(
        ILogger<ClientDebugContextService> logger,
        int inactivityTimeoutMinutes = 30)
    {
        _logger = logger;
        _clients = new();
        _sessions = new();
        _inactivityTimeoutMinutes = inactivityTimeoutMinutes;

        // Start cleanup timer to remove stale entries every 5 minutes
        _cleanupTimer = new Timer(
            _ => CleanupStaleEntries(),
            null,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5));
    }

    /// <summary>
    /// Register a new client connection.
    /// </summary>
    public string RegisterClient(
        string clientType,
        string? clientVersion = null,
        int? processId = null)
    {
        var clientId = Guid.NewGuid().ToString("N");
        var context = new ClientDebugContext(
            ClientId: clientId,
            ClientType: clientType,
            ClientVersion: clientVersion,
            ConnectedAt: DateTime.UtcNow,
            LastActivityAt: DateTime.UtcNow,
            ActiveSessionId: null,
            ProcessId: processId,
            OutputBufferId: clientId);

        if (_clients.TryAdd(clientId, context))
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Client registered: {ClientType} v{Version} (ID: {ClientId}, PID: {ProcessId})",
                    clientType,
                    clientVersion ?? "unknown",
                    clientId,
                    processId ?? 0);
            }
        }

        return clientId;
    }

    /// <summary>
    /// Record activity for a client to update LastActivityAt timestamp.
    /// </summary>
    public void RecordActivity(string clientId)
    {
        if (_clients.TryGetValue(clientId, out var context))
        {
            var updated = context with { LastActivityAt = DateTime.UtcNow };
            _clients[clientId] = updated;
        }
    }

    /// <summary>
    /// Start a debug session for a client.
    /// </summary>
    public string StartSession(string clientId, string? sessionName = null)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var sessionInfo = new SessionDebugInfo(
            SessionId: sessionId,
            ClientId: clientId,
            SessionName: sessionName ?? $"Session-{sessionId.Substring(0, 8)}",
            StartedAt: DateTime.UtcNow,
            EndedAt: null,
            IsActive: true,
            BreakpointCount: 0,
            EventCount: 0);

        if (_sessions.TryAdd(sessionId, sessionInfo))
        {
            if (_clients.TryGetValue(clientId, out var clientContext))
            {
                var updated = clientContext with { ActiveSessionId = sessionId };
                _clients[clientId] = updated;
            }

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Debug session started: {SessionName} (ID: {SessionId}) for client {ClientId}",
                    sessionInfo.SessionName,
                    sessionId,
                    clientId);
            }
        }

        return sessionId;
    }

    /// <summary>
    /// End an active debug session.
    /// </summary>
    public void EndSession(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var sessionInfo))
        {
            var updated = sessionInfo with { EndedAt = DateTime.UtcNow, IsActive = false };
            _sessions[sessionId] = updated;

            if (_clients.TryGetValue(sessionInfo.ClientId, out var clientContext))
            {
                var clientUpdated = clientContext with { ActiveSessionId = null };
                _clients[sessionInfo.ClientId] = clientUpdated;
            }

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Debug session ended: {SessionId} (duration: {DurationMs}ms)",
                    sessionId,
                    (updated.EndedAt - sessionInfo.StartedAt)?.TotalMilliseconds ?? 0);
            }
        }
    }

    /// <summary>
    /// Increment event count for a session.
    /// </summary>
    public void IncrementSessionEventCount(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var sessionInfo))
        {
            var updated = sessionInfo with { EventCount = sessionInfo.EventCount + 1 };
            _sessions[sessionId] = updated;
        }
    }

    /// <summary>
    /// Increment breakpoint count for a session.
    /// </summary>
    public void IncrementSessionBreakpointCount(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var sessionInfo))
        {
            var updated = sessionInfo with { BreakpointCount = sessionInfo.BreakpointCount + 1 };
            _sessions[sessionId] = updated;
        }
    }

    /// <summary>
    /// Unregister a client and clean up associated sessions.
    /// </summary>
    public void UnregisterClient(string clientId)
    {
        if (_clients.TryRemove(clientId, out var context))
        {
            if (context.ActiveSessionId != null)
            {
                EndSession(context.ActiveSessionId);
            }

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Client unregistered: {ClientId}", clientId);
            }
        }
    }

    /// <summary>
    /// Get a registered client by ID.
    /// </summary>
    public ClientDebugContext? GetClient(string clientId)
    {
        _clients.TryGetValue(clientId, out var context);
        return context;
    }

    /// <summary>
    /// Get a debug session by ID.
    /// </summary>
    public SessionDebugInfo? GetSession(string sessionId)
    {
        _sessions.TryGetValue(sessionId, out var session);
        return session;
    }

    /// <summary>
    /// Get all active clients.
    /// </summary>
    public IReadOnlyList<ClientDebugContext> GetActiveClients()
    {
        return _clients.Values.Where(c => c.LastActivityAt > DateTime.UtcNow.AddMinutes(-_inactivityTimeoutMinutes))
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Get all active sessions.
    /// </summary>
    public IReadOnlyList<SessionDebugInfo> GetActiveSessions()
    {
        return _sessions.Values.Where(s => s.IsActive).ToList().AsReadOnly();
    }

    /// <summary>
    /// Get all sessions for a specific client.
    /// </summary>
    public IReadOnlyList<SessionDebugInfo> GetClientSessions(string clientId)
    {
        return _sessions.Values.Where(s => s.ClientId == clientId).ToList().AsReadOnly();
    }

    /// <summary>
    /// Remove stale entries based on inactivity timeout.
    /// </summary>
    private void CleanupStaleEntries()
    {
        var now = DateTime.UtcNow;
        var staleTimeout = TimeSpan.FromMinutes(_inactivityTimeoutMinutes);

        var staleClientIds = _clients
            .Where(kvp => now - kvp.Value.LastActivityAt > staleTimeout)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var clientId in staleClientIds)
        {
            UnregisterClient(clientId);
        }

        var staleSessionIds = _sessions
            .Where(kvp => kvp.Value.EndedAt != null && now - kvp.Value.EndedAt > TimeSpan.FromHours(1))
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var sessionId in staleSessionIds)
        {
            _sessions.TryRemove(sessionId, out _);
        }

        if (staleClientIds.Count > 0 || staleSessionIds.Count > 0)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Cleanup: removed {ClientCount} stale clients and {SessionCount} old sessions",
                    staleClientIds.Count,
                    staleSessionIds.Count);
            }
        }
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        if (_cleanupTimer is not null)
        {
            await _cleanupTimer.DisposeAsync();
        }
    }
}

/// <summary>Contract for client debug context tracking.</summary>
internal interface IClientDebugContextService
{
    string RegisterClient(string clientType, string? clientVersion = null, int? processId = null);
    void RecordActivity(string clientId);
    string StartSession(string clientId, string? sessionName = null);
    void EndSession(string sessionId);
    void IncrementSessionEventCount(string sessionId);
    void IncrementSessionBreakpointCount(string sessionId);
    void UnregisterClient(string clientId);
    ClientDebugContext? GetClient(string clientId);
    SessionDebugInfo? GetSession(string sessionId);
    IReadOnlyList<ClientDebugContext> GetActiveClients();
    IReadOnlyList<SessionDebugInfo> GetActiveSessions();
    IReadOnlyList<SessionDebugInfo> GetClientSessions(string clientId);
}

/// <summary>Represents an active client connection.</summary>
internal sealed record ClientDebugContext(
    string ClientId,
    string ClientType,
    string? ClientVersion,
    DateTime ConnectedAt,
    DateTime LastActivityAt,
    string? ActiveSessionId,
    int? ProcessId,
    string OutputBufferId);

/// <summary>Represents an active debug session.</summary>
internal sealed record SessionDebugInfo(
    string SessionId,
    string ClientId,
    string SessionName,
    DateTime StartedAt,
    DateTime? EndedAt,
    bool IsActive,
    int BreakpointCount,
    int EventCount);
