using Microsoft.Extensions.Logging.Abstractions;
using TokenSaver.MCP.Services;
using Xunit;

namespace TokenSaver.MCP.Tests;

public sealed class ClientDebugContextServiceTests : IAsyncLifetime, IAsyncDisposable
{
    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await ((IAsyncDisposable)_service).DisposeAsync();

    ValueTask IAsyncDisposable.DisposeAsync() => new(DisposeAsync());

    private readonly ClientDebugContextService _service = new(
        NullLogger<ClientDebugContextService>.Instance,
        inactivityTimeoutMinutes: 30);

    [Fact]
    public void RegisterClient_CreatesClientContext()
    {
        var clientId = _service.RegisterClient("VsCode", "1.80.0", 1234);

        Assert.NotEmpty(clientId);
        var client = _service.GetClient(clientId);

        Assert.NotNull(client);
        Assert.Equal("VsCode", client.ClientType);
        Assert.Equal("1.80.0", client.ClientVersion);
        Assert.Equal(1234, client.ProcessId);
        Assert.Null(client.ActiveSessionId);
    }

    [Fact]
    public void RegisterClient_WithoutVersion_CreatesClientContext()
    {
        var clientId = _service.RegisterClient("Continue");

        var client = _service.GetClient(clientId);
        Assert.NotNull(client);
        Assert.Equal("Continue", client.ClientType);
        Assert.Null(client.ClientVersion);
    }

    [Fact]
    public void RegisterClient_ReturnsUniquIds()
    {
        var id1 = _service.RegisterClient("VsCode");
        var id2 = _service.RegisterClient("Continue");
        var id3 = _service.RegisterClient("Cursor");

        Assert.NotEqual(id1, id2);
        Assert.NotEqual(id2, id3);
        Assert.NotEqual(id1, id3);
    }

    [Fact]
    public void RecordActivity_UpdatesLastActivityAt()
    {
        var clientId = _service.RegisterClient("VsCode");
        var client1 = _service.GetClient(clientId)!;
        var firstActivityTime = client1.LastActivityAt;

        System.Threading.Thread.Sleep(100);
        _service.RecordActivity(clientId);

        var client2 = _service.GetClient(clientId)!;
        Assert.True(client2.LastActivityAt > firstActivityTime);
    }

    [Fact]
    public void StartSession_CreatesDebugSession()
    {
        var clientId = _service.RegisterClient("VsCode");
        var sessionId = _service.StartSession(clientId, "TestSession");

        Assert.NotEmpty(sessionId);
        var session = _service.GetSession(sessionId);

        Assert.NotNull(session);
        Assert.Equal("TestSession", session.SessionName);
        Assert.Equal(clientId, session.ClientId);
        Assert.True(session.IsActive);
        Assert.Equal(0, session.BreakpointCount);
        Assert.Equal(0, session.EventCount);
    }

    [Fact]
    public void StartSession_UpdatesClientActiveSessionId()
    {
        var clientId = _service.RegisterClient("VsCode");
        var sessionId = _service.StartSession(clientId);

        var client = _service.GetClient(clientId)!;
        Assert.Equal(sessionId, client.ActiveSessionId);
    }

    [Fact]
    public void StartSession_GeneratesDefaultSessionName()
    {
        var clientId = _service.RegisterClient("VsCode");
        var sessionId = _service.StartSession(clientId);

        var session = _service.GetSession(sessionId)!;
        Assert.StartsWith("Session-", session.SessionName, StringComparison.Ordinal);
    }

    [Fact]
    public void EndSession_MarksSessionAsInactive()
    {
        var clientId = _service.RegisterClient("VsCode");
        var sessionId = _service.StartSession(clientId);

        _service.EndSession(sessionId);

        var session = _service.GetSession(sessionId)!;
        Assert.False(session.IsActive);
        Assert.NotNull(session.EndedAt);
    }

    [Fact]
    public void EndSession_ClearsClientActiveSessionId()
    {
        var clientId = _service.RegisterClient("VsCode");
        var sessionId = _service.StartSession(clientId);

        _service.EndSession(sessionId);

        var client = _service.GetClient(clientId)!;
        Assert.Null(client.ActiveSessionId);
    }

    [Fact]
    public void IncrementSessionEventCount_IncreasesCount()
    {
        var clientId = _service.RegisterClient("VsCode");
        var sessionId = _service.StartSession(clientId);

        var session1 = _service.GetSession(sessionId)!;
        Assert.Equal(0, session1.EventCount);

        _service.IncrementSessionEventCount(sessionId);
        _service.IncrementSessionEventCount(sessionId);
        _service.IncrementSessionEventCount(sessionId);

        var session2 = _service.GetSession(sessionId)!;
        Assert.Equal(3, session2.EventCount);
    }

    [Fact]
    public void IncrementSessionBreakpointCount_IncreasesCount()
    {
        var clientId = _service.RegisterClient("VsCode");
        var sessionId = _service.StartSession(clientId);

        _service.IncrementSessionBreakpointCount(sessionId);
        _service.IncrementSessionBreakpointCount(sessionId);

        var session = _service.GetSession(sessionId)!;
        Assert.Equal(2, session.BreakpointCount);
    }

    [Fact]
    public void UnregisterClient_RemovesClient()
    {
        var clientId = _service.RegisterClient("VsCode");
        Assert.NotNull(_service.GetClient(clientId));

        _service.UnregisterClient(clientId);

        Assert.Null(_service.GetClient(clientId));
    }

    [Fact]
    public void UnregisterClient_EndsActiveSession()
    {
        var clientId = _service.RegisterClient("VsCode");
        var sessionId = _service.StartSession(clientId);

        _service.UnregisterClient(clientId);

        var session = _service.GetSession(sessionId)!;
        Assert.False(session.IsActive);
    }

    [Fact]
    public void GetActiveClients_ReturnsOnlyRecentClients()
    {
        var clientId1 = _service.RegisterClient("VsCode");
        var clientId2 = _service.RegisterClient("Continue");

        var activeClients = _service.GetActiveClients();

        Assert.Equal(2, activeClients.Count);
        Assert.Contains(activeClients, c => c.ClientId == clientId1);
        Assert.Contains(activeClients, c => c.ClientId == clientId2);
    }

    [Fact]
    public void GetActiveSessions_ReturnsOnlyActiveSessions()
    {
        var clientId = _service.RegisterClient("VsCode");
        var sessionId1 = _service.StartSession(clientId);
        var sessionId2 = _service.StartSession(clientId);

        _service.EndSession(sessionId1);

        var activeSessions = _service.GetActiveSessions();

        Assert.Single(activeSessions);
        Assert.Equal(sessionId2, activeSessions[0].SessionId);
    }

    [Fact]
    public void GetClientSessions_ReturnsAllSessionsForClient()
    {
        var clientId1 = _service.RegisterClient("VsCode");
        var clientId2 = _service.RegisterClient("Continue");

        var session1 = _service.StartSession(clientId1);
        var session2 = _service.StartSession(clientId1);
        var session3 = _service.StartSession(clientId2);

        _service.EndSession(session1);

        var clientSessions = _service.GetClientSessions(clientId1);

        Assert.Equal(2, clientSessions.Count);
        Assert.Contains(clientSessions, s => s.SessionId == session1);
        Assert.Contains(clientSessions, s => s.SessionId == session2);
        Assert.DoesNotContain(clientSessions, s => s.SessionId == session3);
    }

    [Fact]
    public void MultipleClients_WithMultipleSessions_TracksCorrectly()
    {
        var client1 = _service.RegisterClient("VsCode", "1.80.0");
        var client2 = _service.RegisterClient("Continue");
        var client3 = _service.RegisterClient("Cursor");

        var session1a = _service.StartSession(client1, "Session 1A");
        var session1b = _service.StartSession(client1, "Session 1B");
        var session2a = _service.StartSession(client2, "Session 2A");

        _service.IncrementSessionEventCount(session1a);
        _service.IncrementSessionEventCount(session1a);
        _service.IncrementSessionBreakpointCount(session1b);
        _service.IncrementSessionEventCount(session2a);

        var clientSessions1 = _service.GetClientSessions(client1);
        Assert.Equal(2, clientSessions1.Count);
        Assert.Equal(2, clientSessions1.First(s => s.SessionId == session1a).EventCount);
        Assert.Equal(1, clientSessions1.First(s => s.SessionId == session1b).BreakpointCount);

        var clientSessions2 = _service.GetClientSessions(client2);
        Assert.Single(clientSessions2);
        Assert.Equal(1, clientSessions2[0].EventCount);

        var clientSessions3 = _service.GetClientSessions(client3);
        Assert.Empty(clientSessions3);
    }

    [Fact]
    public void GetClient_WithInvalidId_ReturnsNull()
    {
        var client = _service.GetClient("invalid-id");
        Assert.Null(client);
    }

    [Fact]
    public void GetSession_WithInvalidId_ReturnsNull()
    {
        var session = _service.GetSession("invalid-id");
        Assert.Null(session);
    }

    [Fact]
    public void SessionDuration_IsTrackedCorrectly()
    {
        var clientId = _service.RegisterClient("VsCode");
        var startTime = DateTime.UtcNow;
        var sessionId = _service.StartSession(clientId);

        System.Threading.Thread.Sleep(100);
        _service.EndSession(sessionId);

        var session = _service.GetSession(sessionId)!;
        var duration = session.EndedAt - session.StartedAt;

        Assert.True(duration > TimeSpan.FromMilliseconds(90));
        Assert.True(duration < TimeSpan.FromMilliseconds(500));
    }

}
