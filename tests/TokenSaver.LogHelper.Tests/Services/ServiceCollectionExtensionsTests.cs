using Microsoft.Extensions.DependencyInjection;
using TokenSaver.LogHelper.Contracts;
using TokenSaver.LogHelper.Models;
using TokenSaver.LogHelper.Services;
using Xunit;

namespace TokenSaver.LogHelper.Tests.Services;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddLogHelper_RegistersLogServiceWithConfiguredOptions()
    {
        var services = new ServiceCollection();
        services.AddLogHelper(o => o.AllowedRoots = new[] { @"C:\logs" });

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<ILogService>());
        Assert.Equal(new[] { @"C:\logs" }, provider.GetRequiredService<LogHelperOptions>().AllowedRoots);
    }
}
