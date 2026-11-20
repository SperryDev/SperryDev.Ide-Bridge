namespace TokenSaver.TerminalControl.Discovery;

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using TokenSaver.TerminalControl.Contracts;
using TokenSaver.TerminalControl.Models;

public sealed class TerminalSettingsManager : ITerminalSettingsManager
{
    private readonly ITerminalConfigurationDiscovery _discovery;
    private readonly ILogger<TerminalSettingsManager> _logger;
    private JsonNode? _originalSettings;
    private string _settingsPath = "";

    public TerminalSettingsManager(
        ITerminalConfigurationDiscovery discovery,
        ILogger<TerminalSettingsManager> logger)
    {
        _discovery = discovery;
        _logger = logger;
    }

    public async Task<IAsyncDisposable> ApplyTemporarySettingsAsync(
        TemporaryTerminalSettings settings,
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Applying temporary terminal settings");

            _settingsPath = GetTerminalSettingsPath();
            if (!File.Exists(_settingsPath))
            {
                throw new InvalidOperationException($"Windows Terminal settings file not found at {_settingsPath}");
            }

            // Backup original settings
            var json = await File.ReadAllTextAsync(_settingsPath, ct);
            _originalSettings = JsonNode.Parse(json);

            // Apply temporary settings
            var modifiedSettings = JsonNode.Parse(json);
            if (modifiedSettings is null)
            {
                throw new InvalidOperationException("Failed to parse settings JSON");
            }

            if (settings.DefaultProfileGuid is not null && modifiedSettings["defaultProfile"] is not null)
            {
                modifiedSettings["defaultProfile"] = JsonValue.Create(settings.DefaultProfileGuid);
            }

            if (settings.ColorScheme is not null && modifiedSettings["schemes"] is not null)
            {
                // Apply color scheme to profiles
                if (modifiedSettings["profiles"]?["list"] is JsonArray profiles)
                {
                    foreach (var profile in profiles)
                    {
                        if (profile is JsonObject profileObj)
                        {
                            profileObj["colorScheme"] = JsonValue.Create(settings.ColorScheme);
                        }
                    }
                }
            }

            if (settings.AlwaysShowTabs.HasValue && modifiedSettings["alwaysShowTabs"] is not null)
            {
                modifiedSettings["alwaysShowTabs"] = JsonValue.Create(settings.AlwaysShowTabs.Value);
            }

            await File.WriteAllTextAsync(_settingsPath, modifiedSettings.ToJsonString(), ct);
            _logger.LogInformation("Temporary terminal settings applied");

            return new SettingsReverter(this, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying temporary terminal settings");
            throw;
        }
    }

    public async Task<TerminalConfiguration> GetCurrentSettingsAsync(CancellationToken ct = default)
    {
        return await _discovery.DiscoverConfigurationAsync(ct);
    }

    public async Task<IAsyncDisposable> ApplyProfileSettingsAsync(
        string profileGuid,
        ProfileSettings settings,
        CancellationToken ct = default)
    {
        try
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Applying temporary profile settings to profile {ProfileGuid}", profileGuid);
            }

            _settingsPath = GetTerminalSettingsPath();
            if (!File.Exists(_settingsPath))
            {
                throw new InvalidOperationException($"Windows Terminal settings file not found at {_settingsPath}");
            }

            var json = await File.ReadAllTextAsync(_settingsPath, ct);
            _originalSettings = JsonNode.Parse(json);

            var modifiedSettings = JsonNode.Parse(json);
            if (modifiedSettings is null)
            {
                throw new InvalidOperationException("Failed to parse settings JSON");
            }

            // Find and modify the profile
            if (modifiedSettings["profiles"]?["list"] is JsonArray profiles)
            {
                foreach (var profile in profiles)
                {
                    if (profile is JsonObject profileObj &&
                        profileObj["guid"]?.GetValue<string>() == profileGuid)
                    {
                        if (settings.TabTitle is not null)
                        {
                            profileObj["tabTitle"] = JsonValue.Create(settings.TabTitle);
                        }

                        if (settings.CommandLine is not null)
                        {
                            profileObj["commandline"] = JsonValue.Create(settings.CommandLine);
                        }

                        if (settings.StartingDirectory is not null)
                        {
                            profileObj["startingDirectory"] = JsonValue.Create(settings.StartingDirectory);
                        }

                        if (settings.ColorScheme is not null)
                        {
                            profileObj["colorScheme"] = JsonValue.Create(settings.ColorScheme);
                        }

                        if (settings.UseAcrylic.HasValue)
                        {
                            profileObj["useAcrylic"] = JsonValue.Create(settings.UseAcrylic.Value);
                        }

                        if (settings.AcrylicOpacity.HasValue)
                        {
                            profileObj["acrylicOpacity"] = JsonValue.Create(settings.AcrylicOpacity.Value);
                        }

                        break;
                    }
                }
            }

            await File.WriteAllTextAsync(_settingsPath, modifiedSettings.ToJsonString(), ct);
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Temporary profile settings applied to profile {ProfileGuid}", profileGuid);
            }

            return new SettingsReverter(this, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying temporary profile settings");
            throw;
        }
    }

    private async Task RevertSettingsAsync(CancellationToken ct)
    {
        try
        {
            if (_originalSettings is null || string.IsNullOrEmpty(_settingsPath))
            {
                return;
            }

            await File.WriteAllTextAsync(_settingsPath, _originalSettings.ToJsonString(), ct);
            _logger.LogInformation("Terminal settings reverted");
            _originalSettings = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reverting terminal settings");
        }
    }

    private static string GetTerminalSettingsPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Packages", "Microsoft.WindowsTerminal_8wekyb3d8bbwe", "LocalState", "settings.json");
    }

    private sealed class SettingsReverter : IAsyncDisposable
    {
        private readonly TerminalSettingsManager _manager;
        private readonly CancellationToken _ct;

        public SettingsReverter(TerminalSettingsManager manager, CancellationToken ct)
        {
            _manager = manager;
            _ct = ct;
        }

        public async ValueTask DisposeAsync()
        {
            await _manager.RevertSettingsAsync(_ct);
        }
    }
}
