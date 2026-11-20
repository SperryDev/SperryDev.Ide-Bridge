namespace TokenSaver.TerminalControl.Tests;

using Microsoft.Extensions.Logging;
using Xunit;
using TokenSaver.TerminalControl.Core;
using TokenSaver.TerminalControl.Models;
using TokenSaver.TerminalControl.Output;

public sealed class WindowsTerminalProviderTests : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<WindowsTerminalProvider> _logger;
    private readonly NamedPipeManager _pipeManager;
    private readonly WindowsTerminalProvider _provider;

    public WindowsTerminalProviderTests()
    {
        _loggerFactory = LoggerFactory.Create(builder => builder.AddDebug());
        _logger = _loggerFactory.CreateLogger<WindowsTerminalProvider>();
        _pipeManager = new NamedPipeManager(_loggerFactory.CreateLogger<NamedPipeManager>());
        _provider = new WindowsTerminalProvider(_pipeManager, _logger);
    }

    public void Dispose()
    {
        _pipeManager.Dispose();
        _loggerFactory.Dispose();
    }

    [Fact]
    public async Task CheckAvailabilityAsync_ReturnsCorrectProviderType()
    {
        // Act
        var availability = await _provider.CheckAvailabilityAsync();

        // Assert
        Assert.Equal(TerminalProviderType.WindowsTerminal, availability.Provider);
    }

    [Fact]
    public async Task CheckAvailabilityAsync_ReturnsStoreLink()
    {
        // Act
        var availability = await _provider.CheckAvailabilityAsync();

        // Assert
        if (!availability.IsInstalled)
        {
            Assert.NotNull(availability.InstallUrl);
            Assert.Contains("microsoft.com/store", availability.InstallUrl.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task CheckAvailabilityAsync_PopulatesErrorMessageWhenNotInstalled()
    {
        // Act
        var availability = await _provider.CheckAvailabilityAsync();

        // Assert
        if (!availability.IsInstalled)
        {
            Assert.NotNull(availability.ErrorMessage);
            Assert.Contains("not installed", availability.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void GetDisplayName_ReturnsHumanReadableName()
    {
        // Act
        var name = _provider.GetDisplayName();

        // Assert
        Assert.Equal("Windows Terminal", name);
    }

    [Fact]
    public async Task ProviderType_IsWindowsTerminal()
    {
        // Assert
        Assert.Equal(TerminalProviderType.WindowsTerminal, _provider.ProviderType);
    }
}

public sealed class IntegratedTerminalProviderTests : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<IntegratedTerminalProvider> _logger;
    private readonly NamedPipeManager _pipeManager;
    private readonly IntegratedTerminalProvider _provider;

    public IntegratedTerminalProviderTests()
    {
        _loggerFactory = LoggerFactory.Create(builder => builder.AddDebug());
        _logger = _loggerFactory.CreateLogger<IntegratedTerminalProvider>();
        _pipeManager = new NamedPipeManager(_loggerFactory.CreateLogger<NamedPipeManager>());
        _provider = new IntegratedTerminalProvider(_pipeManager, _logger);
    }

    public void Dispose()
    {
        _pipeManager.Dispose();
        _loggerFactory.Dispose();
    }

    [Fact]
    public async Task CheckAvailabilityAsync_AlwaysAvailable()
    {
        // Act
        var availability = await _provider.CheckAvailabilityAsync();

        // Assert
        Assert.True(availability.IsInstalled);
        Assert.True(availability.IsAvailable);
    }

    [Fact]
    public async Task CheckAvailabilityAsync_ReturnsCorrectProviderType()
    {
        // Act
        var availability = await _provider.CheckAvailabilityAsync();

        // Assert
        Assert.Equal(TerminalProviderType.Integrated, availability.Provider);
    }

    [Fact]
    public async Task CheckAvailabilityAsync_NoErrorMessage()
    {
        // Act
        var availability = await _provider.CheckAvailabilityAsync();

        // Assert
        Assert.Null(availability.ErrorMessage);
    }

    [Fact]
    public void GetDisplayName_ReturnsHumanReadableName()
    {
        // Act
        var name = _provider.GetDisplayName();

        // Assert
        Assert.Contains("Integrated", name, StringComparison.Ordinal);
        Assert.Contains("PowerShell", name, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderType_IsIntegrated()
    {
        // Assert
        Assert.Equal(TerminalProviderType.Integrated, _provider.ProviderType);
    }
}

public sealed class TerminalProviderSelectorTests : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<TerminalProviderSelector> _logger;
    private readonly NamedPipeManager _pipeManager;
    private readonly TerminalProviderSelector _selector;

    public TerminalProviderSelectorTests()
    {
        _loggerFactory = LoggerFactory.Create(builder => builder.AddDebug());
        _logger = _loggerFactory.CreateLogger<TerminalProviderSelector>();
        _pipeManager = new NamedPipeManager(_loggerFactory.CreateLogger<NamedPipeManager>());
        _selector = new TerminalProviderSelector(_pipeManager, _logger);
    }

    public void Dispose()
    {
        _pipeManager.Dispose();
        _loggerFactory.Dispose();
    }

    [Fact]
    public async Task SelectProviderAsync_ReturnsIntegratedByDefault()
    {
        // Arrange
        var selection = new TerminalProviderSelection(
            PreferredProvider: TerminalProviderType.Integrated);

        // Act
        var provider = await _selector.SelectProviderAsync(selection);

        // Assert
        Assert.Equal(TerminalProviderType.Integrated, provider.ProviderType);
    }

    [Fact]
    public async Task SelectProviderAsync_FallsBackWhenPreferredNotAvailable()
    {
        // Arrange - if WT is not available, should fallback to Integrated
        var selection = new TerminalProviderSelection(
            PreferredProvider: TerminalProviderType.WindowsTerminal,
            FallbackProvider: TerminalProviderType.Integrated);

        // Act
        var provider = await _selector.SelectProviderAsync(selection);

        // Assert
        var wtAvailability = await _selector.GetAvailableProvidersAsync();
        var wtInstalled = wtAvailability.FirstOrDefault(p => p.Provider == TerminalProviderType.WindowsTerminal)?.IsInstalled;

        if (wtInstalled == false)
        {
            Assert.Equal(TerminalProviderType.Integrated, provider.ProviderType);
        }
    }

    [Fact]
    public async Task GetAvailableProvidersAsync_ReturnsAllProviders()
    {
        // Act
        var providers = await _selector.GetAvailableProvidersAsync();

        // Assert
        Assert.NotEmpty(providers);
        Assert.Contains(providers, p => p.Provider == TerminalProviderType.Integrated);
        Assert.Contains(providers, p => p.Provider == TerminalProviderType.WindowsTerminal);
    }

    [Fact]
    public async Task GetAvailableProvidersAsync_IntegratedAlwaysAvailable()
    {
        // Act
        var providers = await _selector.GetAvailableProvidersAsync();

        // Assert
        var integrated = providers.FirstOrDefault(p => p.Provider == TerminalProviderType.Integrated);
        Assert.NotNull(integrated);
        Assert.True(integrated.IsInstalled);
    }

    [Fact]
    public async Task ValidateProviderSelectionAsync_SucceedsForIntegrated()
    {
        // Arrange
        var selection = new TerminalProviderSelection(
            PreferredProvider: TerminalProviderType.Integrated);

        // Act
        var result = await _selector.ValidateProviderSelectionAsync(selection);

        // Assert
        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task ValidateProviderSelectionAsync_FailsWithMessageForMissingWT()
    {
        // Arrange
        var selection = new TerminalProviderSelection(
            PreferredProvider: TerminalProviderType.WindowsTerminal);

        // Act
        var result = await _selector.ValidateProviderSelectionAsync(selection);
        var providers = await _selector.GetAvailableProvidersAsync();
        var wtInstalled = providers.FirstOrDefault(p => p.Provider == TerminalProviderType.WindowsTerminal)?.IsInstalled;

        // Assert
        if (wtInstalled == false)
        {
            Assert.False(result.IsValid);
            Assert.NotNull(result.ErrorMessage);
            Assert.Contains("not installed", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ValidateProviderSelectionAsync_IncludesStoreLink()
    {
        // Arrange
        var selection = new TerminalProviderSelection(
            PreferredProvider: TerminalProviderType.WindowsTerminal);

        // Act
        var result = await _selector.ValidateProviderSelectionAsync(selection);
        var providers = await _selector.GetAvailableProvidersAsync();
        var wtInstalled = providers.FirstOrDefault(p => p.Provider == TerminalProviderType.WindowsTerminal)?.IsInstalled;

        // Assert
        if (wtInstalled == false)
        {
            Assert.NotNull(result.ActionUrl);
            Assert.Contains("microsoft.com/store", result.ActionUrl.ToString(), StringComparison.Ordinal);
            Assert.NotNull(result.ActionText);
            Assert.Contains("Microsoft Store", result.ActionText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ValidateProviderSelectionAsync_HasInstallAction()
    {
        // Arrange
        var selection = new TerminalProviderSelection(
            PreferredProvider: TerminalProviderType.WindowsTerminal);

        // Act
        var result = await _selector.ValidateProviderSelectionAsync(selection);
        var providers = await _selector.GetAvailableProvidersAsync();
        var wtInstalled = providers.FirstOrDefault(p => p.Provider == TerminalProviderType.WindowsTerminal)?.IsInstalled;

        // Assert
        if (wtInstalled == false)
        {
            Assert.True(result.HasInstallAction);
            Assert.NotNull(result.ActionUrl);
            Assert.StartsWith("http", result.ActionUrl.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task SelectProviderAsync_ReturnsWindowsTerminalWhenAvailable()
    {
        // Arrange
        var selection = new TerminalProviderSelection(
            PreferredProvider: TerminalProviderType.WindowsTerminal,
            FallbackProvider: TerminalProviderType.Integrated);

        // Act
        var provider = await _selector.SelectProviderAsync(selection);
        var availability = await _selector.GetAvailableProvidersAsync();
        var wtAvailable = availability.FirstOrDefault(p => p.Provider == TerminalProviderType.WindowsTerminal)?.IsAvailable;

        // Assert
        if (wtAvailable == true)
        {
            Assert.Equal(TerminalProviderType.WindowsTerminal, provider.ProviderType);
        }
    }
}
