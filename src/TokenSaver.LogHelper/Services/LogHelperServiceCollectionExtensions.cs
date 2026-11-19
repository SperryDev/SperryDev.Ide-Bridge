using Microsoft.Extensions.DependencyInjection;
using TokenSaver.LogHelper.Contracts;
using TokenSaver.LogHelper.Models;
using TokenSaver.LogHelper.Sources;

namespace TokenSaver.LogHelper.Services;

public static class LogHelperServiceCollectionExtensions
{
    /// <summary>Registers <see cref="ILogService"/> and <see cref="LogWatcher"/> with the file source and the given options.</summary>
    public static IServiceCollection AddLogHelper(this IServiceCollection services, Action<LogHelperOptions> configure)
    {
        var options = new LogHelperOptions();
        configure(options);

        services.AddSingleton(options);
        services.AddSingleton<ILogSourceProvider>(new FileLogSourceProvider(options));
        services.AddSingleton<ILogService>(sp => new LogService(sp.GetServices<ILogSourceProvider>(), options));
        services.AddSingleton(sp => new LogWatcher(sp.GetServices<ILogSourceProvider>(), options));
        return services;
    }
}
