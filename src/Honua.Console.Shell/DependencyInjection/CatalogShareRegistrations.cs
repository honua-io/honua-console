using Honua.Console.Contracts;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Honua.Console.Shell.DependencyInjection;

internal static class CatalogShareRegistrations
{
    public static void AddCatalogAndShare(IServiceCollection services, HonuaServerBinding server)
    {
        AddCatalogContent(services, server);
        AddShareAccess(services, server);
        AddRbacAccess(services, server);
        AddCatalogDiscovery(services, server);
        services.TryAddScoped<IConsoleCatalogReadContextResolver, ConsoleCatalogReadContextResolver>();
    }

    // The content client also serves the anonymous /public open-data pages, so it forwards the operator
    // bearer when one resolves but must not fail closed like the privileged clients.
    private static void AddCatalogContent(IServiceCollection services, HonuaServerBinding server)
    {
        if (server.BaseUri is { } baseUri)
        {
            services.TryAddSingleton<IHonuaConsoleContentClient>(serviceProvider =>
                new HonuaConsoleContentHttpClient(
                    HonuaServerClientFactory.CreatePublic(serviceProvider, baseUri),
                    new HonuaConsoleContentClientOptions(baseUri, server.AdminApiKey)));
        }

        services.AddServerBound<IConsoleCatalogClient, HonuaServerConsoleCatalogClient, UnsupportedConsoleCatalogClient>(server);
    }

    private static void AddShareAccess(IServiceCollection services, HonuaServerBinding server)
    {
        if (server.BaseUri is { } baseUri)
        {
            services.TryAddSingleton<IHonuaConsoleShareClient>(serviceProvider =>
                new HonuaConsoleShareHttpClient(
                    HonuaServerClientFactory.Create(serviceProvider, baseUri),
                    new HonuaConsoleShareClientOptions(baseUri, server.AdminApiKey)));
        }

        services.AddServerBound<IShareAccessDataSource, HonuaServerShareAccessDataSource, UnsupportedShareAccessDataSource>(server);
    }

    private static void AddRbacAccess(IServiceCollection services, HonuaServerBinding server)
    {
        if (server.BaseUri is { } baseUri)
        {
            services.TryAddSingleton<IHonuaConsoleRbacClient>(serviceProvider =>
                new HonuaConsoleRbacHttpClient(
                    HonuaServerClientFactory.Create(serviceProvider, baseUri),
                    new HonuaConsoleRbacClientOptions(baseUri, server.AdminApiKey)));
        }

        services.AddServerBound<IRbacAccessDataSource, HonuaServerRbacAccessDataSource, UnsupportedRbacAccessDataSource>(server);
    }

    private static void AddCatalogDiscovery(IServiceCollection services, HonuaServerBinding server)
    {
        if (server.BaseUri is { } baseUri)
        {
            services.TryAddSingleton<IHonuaCatalogDiscoveryClient>(serviceProvider =>
                new HonuaCatalogDiscoveryHttpClient(
                    HonuaServerClientFactory.Create(serviceProvider, baseUri),
                    new HonuaCatalogDiscoveryClientOptions(baseUri, server.AdminApiKey)));
        }

        services.AddServerBound<ICatalogDiscoveryDataSource, HonuaServerCatalogDiscoveryDataSource, UnsupportedCatalogDiscoveryDataSource>(server);
    }
}
