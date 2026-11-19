namespace TokenSaver.TerminalControl.Core;

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TokenSaver.TerminalControl.Contracts;
using TokenSaver.TerminalControl.Models;

public sealed class SessionRegistry : ISessionRegistry
{
    private readonly ILogger<SessionRegistry> _logger;
    private readonly ConcurrentDictionary<string, SessionMetadata> _sessions;
    private readonly ConcurrentDictionary<string, List<SessionActivity>> _activityHistory;
    private readonly object _syncLock = new();

    public SessionRegistry(ILogger<SessionRegistry> logger)
    {
        _logger = logger;
        _sessions = new ConcurrentDictionary<string, SessionMetadata>();
        _activityHistory = new ConcurrentDictionary<string, List<SessionActivity>>();
    }

    public Task<SessionMetadata> RegisterSessionAsync(
        string sessionId,
        string name,
        string openedBy,
        bool isElevated,
        CancellationToken ct = default)
    {
        var metadata = new SessionMetadata(
            SessionId: sessionId,
            Name: name,
            OpenedBy: openedBy,
            IsElevated: isElevated,
            CreatedAt: DateTime.UtcNow,
            CurrentActivity: "Initialized",
            TabIds: new List<string>())
        {
            LastActivityAt = DateTime.UtcNow
        };

        if (!_sessions.TryAdd(sessionId, metadata))
        {
            _logger.LogWarning("Session {SessionId} already registered", sessionId);
            return Task.FromResult(_sessions[sessionId]);
        }

        _activityHistory.TryAdd(sessionId, new List<SessionActivity>());

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Session registered: {SessionId} ({Name}) opened by {OpenedBy}",
                sessionId,
                name,
                openedBy);
        }

        return Task.FromResult(metadata);
    }

    public Task<SessionMetadata?> GetSessionAsync(string sessionId, CancellationToken ct = default)
    {
        _sessions.TryGetValue(sessionId, out var metadata);
        return Task.FromResult(metadata);
    }

    public Task<IReadOnlyList<SessionMetadata>> GetActiveSessionsAsync(CancellationToken ct = default)
    {
        var activeSessions = _sessions.Values
            .Where(s => s.IsActive)
            .ToList();

        return Task.FromResult<IReadOnlyList<SessionMetadata>>(activeSessions);
    }

    public Task<IReadOnlyList<SessionMetadata>> GetAllSessionsAsync(CancellationToken ct = default)
    {
        var allSessions = _sessions.Values.ToList();
        return Task.FromResult<IReadOnlyList<SessionMetadata>>(allSessions);
    }

    public Task UpdateActivityAsync(
        string sessionId,
        string activity,
        string? tabId = null,
        string? details = null,
        CancellationToken ct = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var metadata))
        {
            _logger.LogWarning("Session {SessionId} not found", sessionId);
            return Task.CompletedTask;
        }

        // Update metadata
        var updated = metadata with { CurrentActivity = activity };
        updated.LastActivityAt = DateTime.UtcNow;
        _sessions[sessionId] = updated;

        // Record activity history
        if (_activityHistory.TryGetValue(sessionId, out var history))
        {
            lock (_syncLock)
            {
                history.Add(new SessionActivity(
                    SessionId: sessionId,
                    Activity: activity,
                    RecordedAt: DateTime.UtcNow,
                    TabId: tabId,
                    Details: details));
            }
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Session {SessionId} activity updated: {Activity}",
                sessionId,
                activity);
        }

        return Task.CompletedTask;
    }

    public Task AddTabToSessionAsync(
        string sessionId,
        string tabId,
        CancellationToken ct = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var metadata))
        {
            _logger.LogWarning("Session {SessionId} not found", sessionId);
            return Task.CompletedTask;
        }

        var tabList = (metadata.TabIds?.ToList() ?? new List<string>());
        if (!tabList.Contains(tabId))
        {
            tabList.Add(tabId);
            var updated = metadata with { TabIds = tabList };
            updated.LastActivityAt = DateTime.UtcNow;
            _sessions[sessionId] = updated;

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Tab {TabId} added to session {SessionId}", tabId, sessionId);
            }
        }

        return Task.CompletedTask;
    }

    public Task RemoveTabFromSessionAsync(
        string sessionId,
        string tabId,
        CancellationToken ct = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var metadata))
        {
            _logger.LogWarning("Session {SessionId} not found", sessionId);
            return Task.CompletedTask;
        }

        var tabList = (metadata.TabIds?.ToList() ?? new List<string>());
        if (tabList.Remove(tabId))
        {
            var updated = metadata with { TabIds = tabList };
            updated.LastActivityAt = DateTime.UtcNow;
            _sessions[sessionId] = updated;

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Tab {TabId} removed from session {SessionId}", tabId, sessionId);
            }
        }

        return Task.CompletedTask;
    }

    public Task CloseSessionAsync(string sessionId, CancellationToken ct = default)
    {
        if (_sessions.TryRemove(sessionId, out var metadata))
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Session closed: {SessionId} ({Name}) opened by {OpenedBy} (duration: {Duration})",
                    sessionId,
                    metadata.Name,
                    metadata.OpenedBy,
                    metadata.ElapsedTime);
            }
        }
        else
        {
            _logger.LogWarning("Session {SessionId} not found for closing", sessionId);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SessionActivity>> GetActivityHistoryAsync(
        string sessionId,
        int? maxEntries = 100,
        CancellationToken ct = default)
    {
        if (!_activityHistory.TryGetValue(sessionId, out var history))
        {
            return Task.FromResult<IReadOnlyList<SessionActivity>>(new List<SessionActivity>());
        }

        lock (_syncLock)
        {
            var entries = history
                .TakeLast(maxEntries ?? 100)
                .ToList();

            return Task.FromResult<IReadOnlyList<SessionActivity>>(entries);
        }
    }

    public Task ClearActivityHistoryAsync(string sessionId, CancellationToken ct = default)
    {
        if (_activityHistory.TryGetValue(sessionId, out var history))
        {
            lock (_syncLock)
            {
                history.Clear();
            }

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Activity history cleared for session {SessionId}", sessionId);
            }
        }

        return Task.CompletedTask;
    }
}
