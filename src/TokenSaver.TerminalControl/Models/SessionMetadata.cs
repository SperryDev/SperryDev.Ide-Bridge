namespace TokenSaver.TerminalControl.Models;

public sealed record SessionMetadata(
    string SessionId,
    string Name,
    string OpenedBy,
    bool IsElevated,
    DateTime CreatedAt,
    string? CurrentActivity = null,
    IReadOnlyList<string>? TabIds = null)
{
    public DateTime? LastActivityAt { get; set; }

    public bool IsActive => LastActivityAt is null ||
        (DateTime.UtcNow - LastActivityAt) < TimeSpan.FromMinutes(5);

    public TimeSpan ElapsedTime => DateTime.UtcNow - CreatedAt;
}

public sealed record SessionActivity(
    string SessionId,
    string Activity,
    DateTime RecordedAt,
    string? TabId = null,
    string? Details = null)
{
}
