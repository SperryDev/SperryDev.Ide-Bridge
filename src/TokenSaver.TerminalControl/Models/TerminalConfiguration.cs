namespace TokenSaver.TerminalControl.Models;

public sealed record TerminalConfiguration(
    IReadOnlyList<TerminalProfile> Profiles,
    IReadOnlyList<WslInstance> WslInstances,
    IReadOnlyList<TerminalExtension> Extensions,
    string? DefaultProfileGuid = null,
    DateTime DiscoveredAt = default,
    string? TerminalVersion = null)
{
    public TerminalProfile? GetDefaultProfile() =>
        DefaultProfileGuid is not null
            ? Profiles.FirstOrDefault(p => p.ProfileGuid == DefaultProfileGuid)
            : Profiles.FirstOrDefault(p => p.IsDefault);

    public IReadOnlyList<WslInstance> GetRunningInstances() =>
        WslInstances.Where(w => w.IsRunning).ToList();

    public IReadOnlyList<TerminalExtension> GetEnabledExtensions() =>
        Extensions.Where(e => e.IsEnabled).ToList();
}
