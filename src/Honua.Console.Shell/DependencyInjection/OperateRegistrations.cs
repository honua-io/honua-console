using Honua.Console.Contracts;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Honua.Console.Shell.DependencyInjection;

internal static class OperateRegistrations
{
    public static void AddOperate(IServiceCollection services, HonuaServerBinding server, HonuaConsoleShellOptions options)
    {
        AddAdminClient(services, server);
        AddAdminOperations(services, server);
        AddTemporal(services, server, options);
        services.TryAddSingleton<IEsriMigrationRunDataSource, UnsupportedEsriMigrationRunDataSource>();
        AddPublishingWorkspace(services, server, options);
        AddSensorThings(services, server);
        AddScenes(services, server);
        AddAlertRules(services, server);
        AddVersionManagement(services, server);
        AddLayerStyleOverride(services, server);
    }

    private static void AddAdminClient(IServiceCollection services, HonuaServerBinding server)
    {
        if (server.BaseUri is not { } baseUri)
        {
            return;
        }

        services.TryAddSingleton<IHonuaAdminOperateClient>(serviceProvider =>
            new HonuaAdminOperateHttpClient(
                HonuaServerClientFactory.Create(serviceProvider, baseUri),
                new HonuaAdminOperateClientOptions(baseUri, server.AdminApiKey)));
    }

    private static void AddAdminOperations(IServiceCollection services, HonuaServerBinding server)
    {
        services.AddServerBound<IConsoleConnectionCreateOperation, HonuaServerConsoleConnectionCreateOperation, UnsupportedConsoleConnectionCreateOperation>(server);
        services.AddServerBound<IConsoleFileImportOperation, HonuaServerConsoleFileImportOperation, UnsupportedConsoleFileImportOperation>(server);
        services.AddServerBound<IConsoleServiceImportOperation, HonuaServerConsoleServiceImportOperation, UnsupportedConsoleServiceImportOperation>(server);
        services.AddServerBound<IOperateTransitionDataSource, HonuaServerOperateTransitionDataSource, UnsupportedOperateTransitionDataSource>(server);
        services.AddServerBound<IServiceLayerPublishOperation, HonuaServerServiceLayerPublishOperation, UnsupportedServiceLayerPublishOperation>(server);
        services.AddServerBound<IServiceConfigurationOperation, HonuaServerServiceConfigurationOperation, UnsupportedServiceConfigurationOperation>(server);
        services.AddServerBound<IConsoleTimeInfoOperation, HonuaServerConsoleTimeInfoOperation, UnsupportedConsoleTimeInfoOperation>(server);
        services.AddServerBound<IConsoleLayerFieldsOperation, HonuaServerConsoleLayerFieldsOperation, UnsupportedConsoleLayerFieldsOperation>(server);
        services.AddServerBound<IConsoleLayerMetadataOperation, HonuaServerConsoleLayerMetadataOperation, UnsupportedConsoleLayerMetadataOperation>(server);
        services.AddServerBound<IConsoleLayerRelationshipsOperation, HonuaServerConsoleLayerRelationshipsOperation, UnsupportedConsoleLayerRelationshipsOperation>(server);
        services.AddServerBound<IConsoleLayerFilterOperation, HonuaServerConsoleLayerFilterOperation, UnsupportedConsoleLayerFilterOperation>(server);
    }

    // The temporal API has no enumeration verb, so the viewer reads a configured "serviceId:layerId" list.
    private static void AddTemporal(IServiceCollection services, HonuaServerBinding server, HonuaConsoleShellOptions options)
    {
        if (server.BaseUri is not { } baseUri)
        {
            services.TryAddSingleton<ITemporalCapabilityClient, UnsupportedTemporalCapabilityClient>();
            return;
        }

        services.TryAddSingleton<IHonuaTemporalClient>(serviceProvider =>
            new HonuaTemporalHttpClient(
                HonuaServerClientFactory.Create(serviceProvider, baseUri),
                new HonuaTemporalClientOptions(baseUri, server.AdminApiKey)));

        var sources = HonuaServerTemporalOptions.FromConfiguredList(options.ServerTemporalSources);
        services.TryAddSingleton<ITemporalCapabilityClient>(serviceProvider =>
            new HonuaServerTemporalCapabilityClient(serviceProvider.GetRequiredService<IHonuaTemporalClient>(), sources));
    }

    // Publications are discovered from the server; configured ids only filter them.
    private static void AddPublishingWorkspace(IServiceCollection services, HonuaServerBinding server, HonuaConsoleShellOptions options)
    {
        if (!server.IsLive)
        {
            services.TryAddSingleton<IPublishingWorkspaceDataSource, UnsupportedPublishingWorkspaceDataSource>();
            return;
        }

        var publications = PublishingWorkspaceOptions.FromConfiguredList(options.ServerPublicationIds);
        services.TryAddSingleton<IPublishingWorkspaceDataSource>(serviceProvider =>
            new HonuaServerPublishingWorkspaceDataSource(
                serviceProvider.GetRequiredService<IHonuaContentPublicationClient>(),
                publications));
    }

    private static void AddSensorThings(IServiceCollection services, HonuaServerBinding server)
    {
        if (!server.IsLive)
        {
            services.TryAddSingleton<IConsoleSensorThingsClient, UnsupportedConsoleSensorThingsClient>();
            return;
        }

        services.TryAddSingleton<IConsoleSensorThingsClient>(serviceProvider =>
            new HttpConsoleSensorThingsClient(
                ShellHttpClients.ServerBound(serviceProvider),
                serviceProvider.GetRequiredService<IConsoleEnvironmentProfileStore>(),
                serviceProvider.GetRequiredService<IConsoleAccountSessionStore>(),
                server.AdminApiKey));
    }

    // Point-cloud ingest uploads up to 256 MB, so it gets the long-request budget.
    private static void AddScenes(IServiceCollection services, HonuaServerBinding server)
    {
        if (!server.IsLive)
        {
            services.TryAddSingleton<IConsoleSceneClient, UnsupportedConsoleSceneClient>();
            return;
        }

        services.TryAddSingleton<IConsoleSceneClient>(serviceProvider =>
            new HttpConsoleSceneClient(
                ShellHttpClients.ServerBoundUpload(serviceProvider),
                serviceProvider.GetRequiredService<IConsoleEnvironmentProfileStore>(),
                serviceProvider.GetRequiredService<IConsoleAccountSessionStore>(),
                server.AdminApiKey));
    }

    private static void AddAlertRules(IServiceCollection services, HonuaServerBinding server)
    {
        if (!server.IsLive)
        {
            services.TryAddSingleton<IOperateAlertRulesDataSource, UnsupportedOperateAlertRulesDataSource>();
            return;
        }

        services.TryAddSingleton<IConsoleAlertRulesClient>(serviceProvider =>
            new HttpConsoleAlertRulesClient(
                ShellHttpClients.ServerBound(serviceProvider),
                serviceProvider.GetRequiredService<IConsoleEnvironmentProfileStore>(),
                server.AdminApiKey));
        services.TryAddSingleton<IOperateAlertRulesDataSource>(serviceProvider =>
            new ServerOperateAlertRulesDataSource(
                serviceProvider.GetRequiredService<IConsoleOperateObservabilityClient>(),
                serviceProvider.GetRequiredService<IConsoleAlertRulesClient>()));
    }

    private static void AddVersionManagement(IServiceCollection services, HonuaServerBinding server)
    {
        if (server.BaseUri is { } baseUri)
        {
            services.TryAddSingleton<IHonuaVersionManagementClient>(serviceProvider =>
                new HonuaVersionManagementHttpClient(
                    HonuaServerClientFactory.Create(serviceProvider, baseUri),
                    new HonuaVersionManagementClientOptions(baseUri, server.AdminApiKey)));
        }

        services.AddServerBound<IVersionManagementOperation, HonuaServerVersionManagementOperation, UnsupportedVersionManagementOperation>(server);
    }

    private static void AddLayerStyleOverride(IServiceCollection services, HonuaServerBinding server)
    {
        if (!server.IsLive)
        {
            services.TryAddSingleton<IOperateLayerStyleOverrideDataSource, UnsupportedOperateLayerStyleOverrideDataSource>();
            return;
        }

        services.TryAddSingleton<IOperateLayerStyleOverrideDataSource>(serviceProvider =>
            new ServerOperateLayerStyleOverrideDataSource(
                serviceProvider.GetRequiredService<IOperateTransitionDataSource>(),
                serviceProvider.GetRequiredService<IHonuaAdminOperateClient>()));
    }
}
