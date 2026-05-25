using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Honua.Console.Shell.DependencyInjection;

public static class HonuaConsoleShellServiceCollectionExtensions
{
    public static IServiceCollection AddHonuaConsoleShell(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IConsoleEnvironmentProfileStore>(
            _ => InMemoryConsoleEnvironmentProfileStore.CreateSeeded());
        services.TryAddSingleton<IConsoleAccountSessionStore, InMemoryConsoleAccountSessionStore>();
        services.TryAddSingleton<IStudioAuthoringShell, InMemoryStudioAuthoringShell>();
        services.TryAddSingleton<IOperateTransitionDataSource>(
            _ => InMemoryOperateTransitionDataSource.CreateSeeded());
        services.TryAddScoped<IConsoleCatalogReadContextResolver, ConsoleCatalogReadContextResolver>();
        services.TryAddSingleton<IConsoleCatalogClient, InMemoryConsoleCatalogClient>();

        return services;
    }
}
