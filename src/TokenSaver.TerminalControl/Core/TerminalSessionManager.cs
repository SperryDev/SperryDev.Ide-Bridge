namespace TokenSaver.TerminalControl.Core;

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TokenSaver.TerminalControl.Contracts;
using TokenSaver.TerminalControl.Models;

public sealed class TerminalSessionManager : ITerminalSessionManager, IAsyncDisposable
{
    private readonly ITerminalConfigurationDiscovery _configDiscovery;
    private readonly ISessionRegistry _sessionRegistry;
    private readonly ILogger<TerminalSessionManager> _logger;
    private readonly ConcurrentDictionary<string, ITerminalSession> _activeSessions;
    private TerminalConfiguration? _terminalConfiguration;
    private bool _disposed;

    public TerminalConfiguration TerminalConfiguration =>
        _terminalConfiguration ?? throw new InvalidOperationException(
            "Terminal configuration not initialized. Call InitializeAsync first.");

    public TerminalSessionManager(
        ITerminalConfigurationDiscovery configDiscovery,
        ISessionRegistry sessionRegistry,
        ILogger<TerminalSessionManager> logger)
    {
        _configDiscovery = configDiscovery;
        _sessionRegistry = sessionRegistry;
        _logger = logger;
        _activeSessions = new ConcurrentDictionary<string, ITerminalSession>();
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        _logger.LogInformation("Initializing TerminalSessionManager");

        try
        {
            _terminalConfiguration = await _configDiscovery.DiscoverConfigurationAsync(ct);
            _logger.LogInformation("Terminal configuration initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize terminal configuration");
            throw;
        }
    }

    public async Task<ITerminalSession> CreateSessionAsync(
        TerminalSessionConfig config,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Creating new terminal session (Elevated: {Elevated})", config.IsElevated);
        }

        ITerminalSession session = config.IsElevated
            ? new ElevatedTerminalSession(_logger)
            : new StandardTerminalSession(_logger);

        try
        {
            await session.InitializeAsync(config, ct);
            _activeSessions.TryAdd(session.SessionId, session);

            // Register in session registry
            await _sessionRegistry.RegisterSessionAsync(
                session.SessionId,
                $"{(config.IsElevated ? "Elevated" : "Standard")} Terminal",
                Environment.UserName,
                config.IsElevated,
                ct);

            await _sessionRegistry.UpdateActivityAsync(
                session.SessionId,
                "Session initialized",
                ct: ct);

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Terminal session created successfully (SessionId: {SessionId})",
                    session.SessionId);
            }

            return session;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create terminal session");
            throw;
        }
    }

    public ITerminalSession? GetSession(string sessionId)
    {
        ThrowIfDisposed();
        _activeSessions.TryGetValue(sessionId, out var session);
        return session;
    }

    public IReadOnlyList<ITerminalSession> GetActiveSessions()
    {
        ThrowIfDisposed();
        return _activeSessions.Values.ToList();
    }

    public async Task CloseSessionAsync(string sessionId, bool graceful = true, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (_activeSessions.TryRemove(sessionId, out var session))
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Closing terminal session {SessionId} (Graceful: {Graceful})",
                    sessionId,
                    graceful);
            }

            try
            {
                await session.TerminateAsync(graceful, ct);
                await _sessionRegistry.CloseSessionAsync(sessionId, ct);
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation("Terminal session {SessionId} closed successfully", sessionId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error closing terminal session {SessionId}", sessionId);
            }
        }
    }

    public async Task ShutdownAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Shutting down TerminalSessionManager");

        var sessionIds = _activeSessions.Keys.ToList();
        foreach (var sessionId in sessionIds)
        {
            await CloseSessionAsync(sessionId, graceful: true, ct);
        }

        _logger.LogInformation("TerminalSessionManager shutdown complete");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await ShutdownAsync(CancellationToken.None);
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
