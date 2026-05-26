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
        AddSavedQueryEditor(services, honuaServerBaseUrl, honuaServerAdminApiKey);
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

    // Binds the Studio saved-query editor (/studio/query) to honua-server's Analysis Content API (#1182)
    // through the Honua.Console.Contracts shim when a server base address is configured; otherwise the
    // editor renders a missing-binding state (never mock query data).
    private static void AddSavedQueryEditor(
        IServiceCollection services,
        string? honuaServerBaseUrl,
        string? honuaServerAdminApiKey)
    {
        if (Uri.TryCreate(honuaServerBaseUrl, UriKind.Absolute, out var baseUri)
            && (baseUri.Scheme == Uri.UriSchemeHttp || baseUri.Scheme == Uri.UriSchemeHttps))
        {
            services.TryAddSingleton<IAnalysisContentClient>(_ =>
            {
                var httpClient = new HttpClient { BaseAddress = baseUri };
                return new AnalysisContentHttpClient(
                    httpClient,
                    new AnalysisContentClientOptions(baseUri, honuaServerAdminApiKey));
            });
            services.TryAddSingleton<ISavedQueryEditorService, HonuaServerSavedQueryEditorService>();
            return;
        }

        services.TryAddSingleton<ISavedQueryEditorService, UnsupportedSavedQueryEditorService>();
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
