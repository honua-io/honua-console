using Honua.Console.Contracts;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Honua.Console.Shell.DependencyInjection;

public static class HonuaConsoleShellServiceCollectionExtensions
{
    public static IServiceCollection AddHonuaConsoleShell(
        this IServiceCollection services,
        string? honuaServerBaseUrl = null,
        string? honuaServerAdminApiKey = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IConsoleHostCapabilities, BrowserConsoleHostCapabilities>();
        services.TryAddSingleton<IConsoleEnvironmentProfileStore>(
            _ => InMemoryConsoleEnvironmentProfileStore.CreateSeeded());
        services.TryAddSingleton<IConsoleAccountSessionStore, InMemoryConsoleAccountSessionStore>();
        services.TryAddSingleton<IStudioAuthoringShell, InMemoryStudioAuthoringShell>();
        AddOperateTransitionDataSource(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        services.TryAddScoped<IConsoleCatalogReadContextResolver, ConsoleCatalogReadContextResolver>();
        services.TryAddSingleton<IConsoleCatalogClient, InMemoryConsoleCatalogClient>();
        services.TryAddSingleton<IStudioWorkflowPackageClient>(
            _ => InMemoryStudioWorkflowPackageClient.CreateSeeded());

        return services;
    }

    public static IServiceCollection AddHonuaConsoleDemoOperateTransitionData(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Replace(ServiceDescriptor.Singleton<IOperateTransitionDataSource>(
            _ => InMemoryOperateTransitionDataSource.CreateSeeded()));

        return services;
    }

    private static void AddOperateTransitionDataSource(
        IServiceCollection services,
        string? honuaServerBaseUrl,
        string? honuaServerAdminApiKey)
    {
        if (Uri.TryCreate(honuaServerBaseUrl, UriKind.Absolute, out var baseUri)
            && (baseUri.Scheme == Uri.UriSchemeHttp || baseUri.Scheme == Uri.UriSchemeHttps))
        {
            services.TryAddSingleton<IHonuaAdminOperateClient>(_ =>
            {
                var httpClient = new HttpClient { BaseAddress = baseUri };
                return new HonuaAdminOperateHttpClient(
                    httpClient,
                    new HonuaAdminOperateClientOptions(baseUri, honuaServerAdminApiKey));
            });
            services.TryAddSingleton<IOperateTransitionDataSource, HonuaServerOperateTransitionDataSource>();
            return;
        }

        services.TryAddSingleton<IOperateTransitionDataSource, UnsupportedOperateTransitionDataSource>();
    }
}
