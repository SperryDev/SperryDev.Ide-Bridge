namespace TokenSaver.TerminalControl.Discovery;

using System.Text.Json;
using Microsoft.Extensions.Logging;
using TokenSaver.TerminalControl.Contracts;
using TokenSaver.TerminalControl.Models;

public sealed class TerminalConfigurationDiscovery : ITerminalConfigurationDiscovery
{
    private readonly ILogger<TerminalConfigurationDiscovery> _logger;

    public TerminalConfigurationDiscovery(ILogger<TerminalConfigurationDiscovery> logger)
    {
        _logger = logger;
    }

    public async Task<TerminalConfiguration> DiscoverConfigurationAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Starting Windows Terminal configuration discovery");

        var profiles = await GetProfilesAsync(ct);
        var wslInstances = await GetWslInstancesAsync(ct);
        var extensions = await GetExtensionsAsync(ct);
        var version = await GetTerminalVersionAsync(ct);
        var defaultProfileGuid = await GetDefaultProfileGuidAsync(ct);

        var config = new TerminalConfiguration(
            Profiles: profiles,
            WslInstances: wslInstances,
            Extensions: extensions,
            DefaultProfileGuid: defaultProfileGuid,
            DiscoveredAt: DateTime.UtcNow,
            TerminalVersion: version);

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Terminal configuration discovery complete: {ProfileCount} profiles, {WslCount} WSL instances, {ExtensionCount} extensions",
                profiles.Count,
                wslInstances.Count,
                extensions.Count);
        }

        return config;
    }

    public async Task<IReadOnlyList<TerminalProfile>> GetProfilesAsync(CancellationToken ct = default)
    {
        try
        {
            var settingsPath = GetTerminalSettingsPath();
            if (!File.Exists(settingsPath))
            {
                _logger.LogWarning("Windows Terminal settings file not found at {Path}", settingsPath);
                return new List<TerminalProfile>();
            }

            var json = await File.ReadAllTextAsync(settingsPath, ct);
            using var doc = JsonDocument.Parse(json);

            var profiles = new List<TerminalProfile>();
            var profilesElement = doc.RootElement.GetProperty("profiles");

            if (profilesElement.TryGetProperty("list", out var listElement))
            {
                var defaultGuid = doc.RootElement
                    .GetProperty("defaultProfile")
                    .GetString();

                foreach (var profileElement in listElement.EnumerateArray())
                {
                    var profile = ParseProfile(profileElement, defaultGuid);
                    if (profile is not null)
                    {
                        profiles.Add(profile);
                    }
                }
            }

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Discovered {ProfileCount} terminal profiles", profiles.Count);
            }
            return profiles;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering terminal profiles");
            return new List<TerminalProfile>();
        }
    }

    public async Task<IReadOnlyList<WslInstance>> GetWslInstancesAsync(CancellationToken ct = default)
    {
        try
        {
            var instances = new List<WslInstance>();
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "wsl.exe",
                    Arguments = "--list --verbose",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            if (process.ExitCode == 0)
            {
                instances = ParseWslOutput(output);
            }

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Discovered {WslCount} WSL instances", instances.Count);
            }
            return instances;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error discovering WSL instances");
            return new List<WslInstance>();
        }
    }

    public async Task<IReadOnlyList<TerminalExtension>> GetExtensionsAsync(CancellationToken ct = default)
    {
        try
        {
            var extensionsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft",
                "Windows Terminal",
                "Packages");

            if (!Directory.Exists(extensionsDir))
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("Terminal extensions directory not found at {Path}", extensionsDir);
                }
                return new List<TerminalExtension>();
            }

            var extensions = new List<TerminalExtension>();
            var packageDirs = Directory.GetDirectories(extensionsDir);

            foreach (var packageDir in packageDirs)
            {
                var appxManifestPath = Path.Combine(packageDir, "AppxManifest.xml");
                if (File.Exists(appxManifestPath))
                {
                    var extension = ParseExtensionFromManifest(packageDir, appxManifestPath);
                    if (extension is not null)
                    {
                        extensions.Add(extension);
                    }
                }
            }

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Discovered {ExtensionCount} terminal extensions", extensions.Count);
            }
            return extensions;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error discovering terminal extensions");
            return new List<TerminalExtension>();
        }
    }

    /// <summary>
    /// Returns null. Running "wt.exe --version" opens a Windows Terminal Help dialog on this build,
    /// once per session, so the version is not read from the process.
    /// </summary>
    public Task<string?> GetTerminalVersionAsync(CancellationToken ct = default)
    {
        return Task.FromResult<string?>(null);
    }

    private static string GetTerminalSettingsPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Packages", "Microsoft.WindowsTerminal_8wekyb3d8bbwe", "LocalState", "settings.json");
    }

    private static async Task<string?> GetDefaultProfileGuidAsync(CancellationToken ct = default)
    {
        try
        {
            var settingsPath = GetTerminalSettingsPath();
            if (!File.Exists(settingsPath))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(settingsPath, ct);
            using var doc = JsonDocument.Parse(json);

            return doc.RootElement
                .GetProperty("defaultProfile")
                .GetString();
        }
        catch
        {
            return null;
        }
    }

    private static TerminalProfile? ParseProfile(JsonElement profileElement, string? defaultGuid)
    {
        try
        {
            var nameElement = profileElement.GetProperty("name");
            var guidElement = profileElement.GetProperty("guid");

            var name = nameElement.GetString() ?? "Unknown";
            var guid = guidElement.GetString() ?? "";

            var commandLine = profileElement.TryGetProperty("commandline", out var cmd)
                ? cmd.GetString()
                : null;

            var startingDirectory = profileElement.TryGetProperty("startingDirectory", out var dir)
                ? dir.GetString()
                : null;

            var tabTitle = profileElement.TryGetProperty("tabTitle", out var title)
                ? title.GetString()
                : null;

            var icon = profileElement.TryGetProperty("icon", out var iconProp)
                ? iconProp.GetString()
                : null;

            var hidden = profileElement.TryGetProperty("hidden", out var hiddenProp)
                && hiddenProp.GetBoolean();

            return new TerminalProfile(
                Name: name,
                ProfileGuid: guid,
                CommandLine: commandLine,
                StartingDirectory: startingDirectory,
                TabTitle: tabTitle,
                Icon: icon,
                Hidden: hidden)
            {
                IsDefault = guid == defaultGuid
            };
        }
        catch
        {
            return null;
        }
    }

    private static List<WslInstance> ParseWslOutput(string output)
    {
        var instances = new List<WslInstance>();
        var lines = output.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines.Skip(1)) // Skip header
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3)
            {
                var name = parts[0];
                var state = parts[1];
                var version = parts[2];

                instances.Add(new WslInstance(
                    Name: name,
                    State: state,
                    Version: version,
                    IsDefault: line.StartsWith("* ", StringComparison.Ordinal)));
            }
        }

        return instances;
    }

    private static TerminalExtension? ParseExtensionFromManifest(string packageDir, string manifestPath)
    {
        try
        {
            var dirName = new DirectoryInfo(packageDir).Name;
            var author = "Unknown";
            var version = "1.0.0";

            // Try to extract version and author from package directory name or manifest
            // Format is typically: PackageName_version_architecture_publisher
            var parts = dirName.Split('_');
            if (parts.Length >= 2)
            {
                version = parts[1];
                if (parts.Length >= 4)
                {
                    author = parts[3];
                }
            }

            return new TerminalExtension(
                Name: parts[0],
                Version: version,
                Author: author,
                InstallPath: packageDir,
                IsEnabled: true);
        }
        catch
        {
            return null;
        }
    }
}
