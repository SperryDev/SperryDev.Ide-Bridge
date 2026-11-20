namespace TokenSaver.TerminalControl.Tests;

using Microsoft.Extensions.Logging;
using Xunit;
using TokenSaver.TerminalControl.Contracts;
using TokenSaver.TerminalControl.Core;
using TokenSaver.TerminalControl.Models;

public sealed class SessionRegistryTests : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<SessionRegistry> _logger;
    private readonly SessionRegistry _registry;

    public SessionRegistryTests()
    {
        _loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = _loggerFactory.CreateLogger<SessionRegistry>();
        _registry = new SessionRegistry(_logger);
    }

    public void Dispose()
    {
        _loggerFactory.Dispose();
    }

    [Fact]
    public async Task RegisterSessionAsync_CreatesNewSession()
    {
        // Arrange
        var sessionId = "test-session-001";
        var name = "Build Process";
        var openedBy = "user@machine";

        // Act
        var metadata = await _registry.RegisterSessionAsync(
            sessionId,
            name,
            openedBy,
            isElevated: true);

        // Assert
        Assert.NotNull(metadata);
        Assert.Equal(sessionId, metadata.SessionId);
        Assert.Equal(name, metadata.Name);
        Assert.Equal(openedBy, metadata.OpenedBy);
        Assert.True(metadata.IsElevated);
        Assert.Equal("Initialized", metadata.CurrentActivity);
    }

    [Fact]
    public async Task RegisterSessionAsync_DuplicateSessionReturnsExisting()
    {
        // Arrange
        var sessionId = "test-session-dup";
        var name1 = "First Session";
        var name2 = "Second Session";

        // Act
        var first = await _registry.RegisterSessionAsync(sessionId, name1, "user1", false);
        var second = await _registry.RegisterSessionAsync(sessionId, name2, "user2", true);

        // Assert
        Assert.Equal(first.SessionId, second.SessionId);
        Assert.Equal(name1, second.Name); // Original name preserved
    }

    [Fact]
    public async Task GetSessionAsync_ReturnsRegisteredSession()
    {
        // Arrange
        var sessionId = "get-test-001";
        await _registry.RegisterSessionAsync(sessionId, "Test Session", "user", false);

        // Act
        var retrieved = await _registry.GetSessionAsync(sessionId);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(sessionId, retrieved.SessionId);
    }

    [Fact]
    public async Task GetSessionAsync_ReturnsNullForUnregisteredSession()
    {
        // Act
        var retrieved = await _registry.GetSessionAsync("nonexistent");

        // Assert
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task UpdateActivityAsync_UpdatesCurrentActivity()
    {
        // Arrange
        var sessionId = "activity-test-001";
        await _registry.RegisterSessionAsync(sessionId, "Test", "user", false);

        // Act
        await _registry.UpdateActivityAsync(sessionId, "Running build");

        // Assert
        var session = await _registry.GetSessionAsync(sessionId);
        Assert.NotNull(session);
        Assert.Equal("Running build", session.CurrentActivity);
    }

    [Fact]
    public async Task UpdateActivityAsync_RecordsActivityHistory()
    {
        // Arrange
        var sessionId = "history-test-001";
        await _registry.RegisterSessionAsync(sessionId, "Test", "user", false);

        // Act
        await _registry.UpdateActivityAsync(sessionId, "Activity 1", details: "First activity");
        await _registry.UpdateActivityAsync(sessionId, "Activity 2", details: "Second activity");

        // Assert
        var history = await _registry.GetActivityHistoryAsync(sessionId);
        Assert.Equal(2, history.Count);
        Assert.Equal("Activity 1", history[0].Activity);
        Assert.Equal("Activity 2", history[1].Activity);
    }

    [Fact]
    public async Task UpdateActivityAsync_RecordsTabIdInHistory()
    {
        // Arrange
        var sessionId = "tab-test-001";
        var tabId = "tab-123";
        await _registry.RegisterSessionAsync(sessionId, "Test", "user", false);

        // Act
        await _registry.UpdateActivityAsync(
            sessionId,
            "Running command",
            tabId: tabId,
            details: "dotnet build");

        // Assert
        var history = await _registry.GetActivityHistoryAsync(sessionId);
        Assert.Single(history);
        Assert.Equal(tabId, history[0].TabId);
        Assert.Equal("dotnet build", history[0].Details);
    }

    [Fact]
    public async Task AddTabToSessionAsync_AddsTabToSession()
    {
        // Arrange
        var sessionId = "tab-add-test";
        await _registry.RegisterSessionAsync(sessionId, "Test", "user", false);
        var tabId = "tab-001";

        // Act
        await _registry.AddTabToSessionAsync(sessionId, tabId);

        // Assert
        var session = await _registry.GetSessionAsync(sessionId);
        Assert.NotNull(session?.TabIds);
        Assert.Contains(tabId, session.TabIds);
    }

    [Fact]
    public async Task AddTabToSessionAsync_DoesNotAddDuplicateTabs()
    {
        // Arrange
        var sessionId = "tab-dup-test";
        await _registry.RegisterSessionAsync(sessionId, "Test", "user", false);
        var tabId = "tab-001";

        // Act
        await _registry.AddTabToSessionAsync(sessionId, tabId);
        await _registry.AddTabToSessionAsync(sessionId, tabId);

        // Assert
        var session = await _registry.GetSessionAsync(sessionId);
        Assert.NotNull(session?.TabIds);
        Assert.Single(session.TabIds);
    }

    [Fact]
    public async Task RemoveTabFromSessionAsync_RemovesTabFromSession()
    {
        // Arrange
        var sessionId = "tab-remove-test";
        await _registry.RegisterSessionAsync(sessionId, "Test", "user", false);
        var tabId1 = "tab-001";
        var tabId2 = "tab-002";

        await _registry.AddTabToSessionAsync(sessionId, tabId1);
        await _registry.AddTabToSessionAsync(sessionId, tabId2);

        // Act
        await _registry.RemoveTabFromSessionAsync(sessionId, tabId1);

        // Assert
        var session = await _registry.GetSessionAsync(sessionId);
        Assert.NotNull(session?.TabIds);
        Assert.Single(session.TabIds);
        Assert.Contains(tabId2, session.TabIds);
        Assert.DoesNotContain(tabId1, session.TabIds);
    }

    [Fact]
    public async Task CloseSessionAsync_UnregistersSession()
    {
        // Arrange
        var sessionId = "close-test-001";
        await _registry.RegisterSessionAsync(sessionId, "Test", "user", false);

        // Act
        await _registry.CloseSessionAsync(sessionId);

        // Assert
        var retrieved = await _registry.GetSessionAsync(sessionId);
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task GetActiveSessionsAsync_ReturnsList()
    {
        // Arrange
        await _registry.RegisterSessionAsync("active-1", "Session 1", "user", false);
        await _registry.RegisterSessionAsync("active-2", "Session 2", "user", true);
        await _registry.RegisterSessionAsync("active-3", "Session 3", "user", false);

        // Act
        var activeSessions = await _registry.GetActiveSessionsAsync();

        // Assert
        Assert.NotEmpty(activeSessions);
        Assert.True(activeSessions.Count >= 3);
    }

    [Fact]
    public async Task GetAllSessionsAsync_ReturnsList()
    {
        // Arrange
        var initial = await _registry.GetAllSessionsAsync();
        await _registry.RegisterSessionAsync("all-1", "Session 1", "user", false);
        await _registry.RegisterSessionAsync("all-2", "Session 2", "user", true);

        // Act
        var allSessions = await _registry.GetAllSessionsAsync();

        // Assert
        Assert.True(allSessions.Count >= initial.Count + 2);
    }

    [Fact]
    public async Task GetActivityHistoryAsync_RespectsMaxEntries()
    {
        // Arrange
        var sessionId = "history-limit-test";
        await _registry.RegisterSessionAsync(sessionId, "Test", "user", false);

        // Act
        for (int i = 0; i < 50; i++)
        {
            await _registry.UpdateActivityAsync(sessionId, $"Activity {i}");
        }

        var history = await _registry.GetActivityHistoryAsync(sessionId, maxEntries: 10);

        // Assert
        Assert.Equal(10, history.Count);
    }

    [Fact]
    public async Task ClearActivityHistoryAsync_ClearsHistory()
    {
        // Arrange
        var sessionId = "clear-history-test";
        await _registry.RegisterSessionAsync(sessionId, "Test", "user", false);
        await _registry.UpdateActivityAsync(sessionId, "Activity 1");
        await _registry.UpdateActivityAsync(sessionId, "Activity 2");

        // Act
        await _registry.ClearActivityHistoryAsync(sessionId);
        var history = await _registry.GetActivityHistoryAsync(sessionId);

        // Assert
        Assert.Empty(history);
    }

    [Fact]
    public async Task SessionMetadata_IsActiveProperty_WorksCorrectly()
    {
        // Arrange
        var sessionId = "active-check-test";
        await _registry.RegisterSessionAsync(sessionId, "Test", "user", false);

        // Act
        var session = await _registry.GetSessionAsync(sessionId);

        // Assert
        Assert.NotNull(session);
        Assert.True(session.IsActive);
    }

    [Fact]
    public async Task SessionMetadata_ElapsedTimeProperty_WorksCorrectly()
    {
        // Arrange
        var sessionId = "elapsed-time-test";
        await _registry.RegisterSessionAsync(sessionId, "Test", "user", false);

        // Act
        await Task.Delay(100);
        var session = await _registry.GetSessionAsync(sessionId);

        // Assert
        Assert.NotNull(session);
        Assert.True(session.ElapsedTime > TimeSpan.Zero);
        Assert.True(session.ElapsedTime < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task MultipleTabsAcrossMultipleSessions()
    {
        // Arrange
        var session1 = "multi-session-1";
        var session2 = "multi-session-2";

        await _registry.RegisterSessionAsync(session1, "Session 1", "user", false);
        await _registry.RegisterSessionAsync(session2, "Session 2", "user", true);

        // Act
        await _registry.AddTabToSessionAsync(session1, "tab-1-a");
        await _registry.AddTabToSessionAsync(session1, "tab-1-b");
        await _registry.AddTabToSessionAsync(session2, "tab-2-a");
        await _registry.AddTabToSessionAsync(session2, "tab-2-b");
        await _registry.AddTabToSessionAsync(session2, "tab-2-c");

        // Assert
        var s1 = await _registry.GetSessionAsync(session1);
        var s2 = await _registry.GetSessionAsync(session2);

        Assert.NotNull(s1?.TabIds);
        Assert.NotNull(s2?.TabIds);
        Assert.Equal(2, s1.TabIds.Count);
        Assert.Equal(3, s2.TabIds.Count);
    }

    [Fact]
    public async Task UpdateActivityAsync_HandlesNonexistentSession()
    {
        // Act & Assert (should not throw)
        await _registry.UpdateActivityAsync("nonexistent", "Activity");
    }

    [Fact]
    public async Task RemoveTabFromSessionAsync_HandlesMissingTab()
    {
        // Arrange
        var sessionId = "remove-missing-tab-test";
        await _registry.RegisterSessionAsync(sessionId, "Test", "user", false);
        await _registry.AddTabToSessionAsync(sessionId, "tab-001");

        // Act & Assert (should not throw)
        await _registry.RemoveTabFromSessionAsync(sessionId, "nonexistent-tab");
    }
}
