using System.Net.Http;
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
        AddStudioAuthoringShell(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddStudioFormPackageDataSource(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddOperateTransitionDataSource(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        services.TryAddScoped<IConsoleCatalogReadContextResolver, ConsoleCatalogReadContextResolver>();
        services.TryAddSingleton<IConsoleCatalogClient, InMemoryConsoleCatalogClient>();
        services.TryAddSingleton<IStudioWorkflowPackageClient>(
            _ => InMemoryStudioWorkflowPackageClient.CreateSeeded());

        // Operate observability binds to a real honua-server through a thin
        // typed HttpClient behind Honua.Console.Contracts (the sanctioned interim
        // until honua-sdk-dotnet projects the Operate contracts). Server-owned
        // telemetry/events/alerts/rules/jobs/investigations are no longer wired
        // to OperateObservabilityFixture.Default at runtime. TryAdd keeps an
        // explicit test/demo provider overridable.
        services.TryAddSingleton<IConsoleOperateObservabilityClient>(serviceProvider =>
            new HttpConsoleOperateObservabilityClient(
                CreateOperateObservabilityHttpClient(),
                serviceProvider.GetRequiredService<IConsoleEnvironmentProfileStore>(),
                serviceProvider.GetRequiredService<IConsoleAccountSessionStore>()));

        return services;
    }

    public static IServiceCollection AddHonuaConsoleDemoOperateTransitionData(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Replace(ServiceDescriptor.Singleton<IOperateTransitionDataSource>(
            _ => InMemoryOperateTransitionDataSource.CreateSeeded()));

        return services;
    }

    /// <summary>
    /// Swaps the Studio authoring shell for the local in-memory simulator. For explicit demo/local
    /// composition only — never the merged runtime path for server-owned Studio package data.
    /// </summary>
    public static IServiceCollection AddHonuaConsoleDemoStudioAuthoringShell(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Replace(ServiceDescriptor.Singleton<IStudioAuthoringShell, InMemoryStudioAuthoringShell>());

        return services;
    }

    // Binds the Studio package draft/lifecycle/validation/preview-plan surface to honua-server
    // (#1180/#1181) through the Honua.Console.Contracts shim when a server base address is configured;
    // otherwise the shell renders a missing-binding state (never mock package data).
    private static void AddStudioAuthoringShell(
        IServiceCollection services,
        string? honuaServerBaseUrl,
        string? honuaServerAdminApiKey)
    {
        if (Uri.TryCreate(honuaServerBaseUrl, UriKind.Absolute, out var baseUri)
            && (baseUri.Scheme == Uri.UriSchemeHttp || baseUri.Scheme == Uri.UriSchemeHttps))
        {
            services.TryAddSingleton<IStudioPackageLifecycleClient>(_ =>
            {
                var httpClient = new HttpClient { BaseAddress = baseUri };
                return new HttpStudioPackageLifecycleClient(
                    httpClient,
                    new StudioPackageLifecycleClientOptions(baseUri, honuaServerAdminApiKey));
            });
            services.TryAddSingleton<IStudioAuthoringShell, ServerStudioAuthoringShell>();
            return;
        }

        services.TryAddSingleton<IStudioAuthoringShell, UnsupportedStudioAuthoringShell>();
    }

    // Binds the Studio form-builder surface (/studio/form) to honua-server's form package lifecycle
    // (#1184) through the Honua.Console.Contracts shim when a server base address is configured;
    // otherwise the builder renders a missing-binding state (never mock form data).
    private static void AddStudioFormPackageDataSource(
        IServiceCollection services,
        string? honuaServerBaseUrl,
        string? honuaServerAdminApiKey)
    {
        if (Uri.TryCreate(honuaServerBaseUrl, UriKind.Absolute, out var baseUri)
            && (baseUri.Scheme == Uri.UriSchemeHttp || baseUri.Scheme == Uri.UriSchemeHttps))
        {
            services.TryAddSingleton<IHonuaFormPackageClient>(_ =>
            {
                var httpClient = new HttpClient { BaseAddress = baseUri };
                return new HonuaFormPackageHttpClient(
                    httpClient,
                    new HonuaFormPackageClientOptions(baseUri, honuaServerAdminApiKey));
            });
            services.TryAddSingleton<IStudioFormPackageDataSource, HonuaServerStudioFormPackageDataSource>();
            return;
        }

        services.TryAddSingleton<IStudioFormPackageDataSource, UnsupportedStudioFormPackageDataSource>();
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

    private static HttpClient CreateOperateObservabilityHttpClient() =>
        new(new SocketsHttpHandler
        {
            // Refresh pooled connections so a long-lived singleton client does
            // not pin stale DNS for the active environment's server.
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
}
