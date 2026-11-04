using Honua.Console.Contracts;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Honua.Console.Shell.DependencyInjection;

public static class HonuaConsoleShellServiceCollectionExtensions
{
    public static IServiceCollection AddHonuaConsoleShell(this IServiceCollection services) =>
        services.AddHonuaConsoleShell(new HonuaConsoleShellOptions());

    public static IServiceCollection AddHonuaConsoleShell(this IServiceCollection services, HonuaConsoleShellOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        var server = HonuaServerBinding.From(options);
        AddShellCore(services, options);
        AddContentPublicationClient(services, server);
        AddStudio(services, server, options);
        AddCatalogAndShare(services, server);
        AddOperate(services, server, options);
        AddOperateObservability(services, options);
        AddSupport(services, options);
        return services;
    }

    public static IServiceCollection AddHonuaConsoleDemoOperateTransitionData(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Replace(ServiceDescriptor.Singleton<IOperateTransitionDataSource>(_ => InMemoryOperateTransitionDataSource.CreateSeeded()));
        return services;
    }

    public static IServiceCollection AddHonuaConsoleDemoCatalogContent(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Replace(ServiceDescriptor.Singleton<IConsoleCatalogClient, InMemoryConsoleCatalogClient>());
        return services;
    }

    public static IServiceCollection AddHonuaConsoleDemoStudioAuthoringShell(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Replace(ServiceDescriptor.Singleton<IStudioAuthoringShell, InMemoryStudioAuthoringShell>());
        return services;
    }

    public static IServiceCollection AddHonuaConsoleDemoStudioWorkflowPackages(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Replace(ServiceDescriptor.Singleton<IStudioWorkflowPackageClient>(_ => InMemoryStudioWorkflowPackageClient.CreateSeeded()));
        return services;
    }

    public static IServiceCollection AddHonuaConsoleDemoCollectAutomations(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Replace(ServiceDescriptor.Singleton<ICollectAutomationClient>(_ => InMemoryCollectAutomationClient.CreateSeeded()));
        return services;
    }

    private static void AddShellCore(IServiceCollection services, HonuaConsoleShellOptions options)
    {
        services.TryAddSingleton(new ConsolePresentation(options.ConsoleMode));
        services.TryAddSingleton<IConsoleHostCapabilities, BrowserConsoleHostCapabilities>();
        services.TryAddSingleton<IConsoleProductMode>(
            _ => new ConfiguredConsoleProductMode(ConsoleProductModeParser.Parse(options.ConsoleMode)));
        services.TryAddSingleton<IConsoleShortcutPlatform, RuntimeConsoleShortcutPlatform>();

        // Capability snapshots are mutable per Blazor circuit and must never be shared between circuits.
        services.TryAddScoped<IConsoleCapabilityManifest>(serviceProvider =>
            new ManifestBackedConsoleCapabilityManifest(
                serviceProvider.GetRequiredService<ICapabilityRegistryClient>(),
                ConsoleCapabilityManifest.SplitList(options.AdvertisedCapabilities)));

        // Scoped so a toast is shown only to the circuit that raised it.
        services.TryAddScoped<IConsoleNotificationService, ConsoleNotificationService>();

        // Starts empty: the first run creates a real environment, never seeded demo profiles.
        services.TryAddSingleton<IConsoleEnvironmentProfileStore>(_ => new InMemoryConsoleEnvironmentProfileStore([]));
        services.TryAddSingleton<IConsoleAccountSessionStore, InMemoryConsoleAccountSessionStore>();

        // The exchange needs a per-operator BFF; a process-wide cookie client would bleed one operator's identity.
        services.TryAddSingleton<IConsoleOperatorBearerExchange, UnavailableConsoleOperatorBearerExchange>();
        services.TryAddSingleton<IConsoleOperatorBearerProvider>(serviceProvider =>
            new ConsoleOperatorBearerProvider(
                serviceProvider.GetRequiredService<IConsoleAccountSessionStore>(),
                serviceProvider.GetRequiredService<IConsoleOperatorBearerExchange>(),
                TimeProvider.System));
    }

    // Shared by Studio report generation and the Operate publishing workspace.
    private static void AddContentPublicationClient(IServiceCollection services, HonuaServerBinding server)
    {
        if (server.BaseUri is not { } baseUri)
        {
            return;
        }

        services.TryAddSingleton<IHonuaContentPublicationClient>(serviceProvider =>
            new HonuaContentPublicationHttpClient(
                HonuaServerClientFactory.Create(serviceProvider, baseUri, ShellHttpClients.LongRequestTimeout),
                new HonuaContentPublicationClientOptions(baseUri, server.AdminApiKey)));
    }

    private static void AddStudio(IServiceCollection services, HonuaServerBinding server, HonuaConsoleShellOptions options)
    {
        AddPackageAuthoring(services, server);
        AddFormPackages(services, server);
        AddMapStyleCatalog(services, server);
        services.AddServerBound<IStudioReportPublicationDataSource, HonuaServerStudioReportPublicationDataSource, UnsupportedStudioReportPublicationDataSource>(server);
        AddWorkflowPackages(services, server);
        services.TryAddSingleton<ICollectAutomationClient, UnsupportedCollectAutomationClient>();

        // Deterministic and server-independent: it only chooses the prompt lane and never actuates.
        services.TryAddSingleton<IOmniPromptIntentClassifier, OmniPromptIntentClassifier>();
        AddIntentResolution(services, server, options);
    }

    private static void AddPackageAuthoring(IServiceCollection services, HonuaServerBinding server)
    {
        if (server.BaseUri is { } baseUri)
        {
            services.TryAddSingleton<IStudioPackageLifecycleClient>(serviceProvider =>
                new HttpStudioPackageLifecycleClient(
                    HonuaServerClientFactory.Create(serviceProvider, baseUri),
                    new StudioPackageLifecycleClientOptions(baseUri, server.AdminApiKey)));
        }

        services.AddServerBound<IStudioAuthoringShell, ServerStudioAuthoringShell, UnsupportedStudioAuthoringShell>(server);
    }

    private static void AddFormPackages(IServiceCollection services, HonuaServerBinding server)
    {
        if (server.BaseUri is { } baseUri)
        {
            services.TryAddSingleton<IHonuaFormPackageClient>(serviceProvider =>
                new HonuaFormPackageHttpClient(
                    HonuaServerClientFactory.Create(serviceProvider, baseUri),
                    new HonuaFormPackageClientOptions(baseUri, server.AdminApiKey)));
        }

        services.AddServerBound<IStudioFormPackageDataSource, HonuaServerStudioFormPackageDataSource, UnsupportedStudioFormPackageDataSource>(server);
    }

    // OGC API - Styles is a public read, so it uses the anonymous-capable client instead of failing closed.
    private static void AddMapStyleCatalog(IServiceCollection services, HonuaServerBinding server)
    {
        if (server.BaseUri is { } baseUri)
        {
            services.TryAddSingleton<IHonuaOgcStylesClient>(serviceProvider =>
                new HonuaOgcStylesHttpClient(
                    HonuaServerClientFactory.CreatePublic(serviceProvider, baseUri),
                    new HonuaOgcStylesClientOptions(
                        baseUri,
                        server.AdminApiKey,
                        AuthorityBindingSupported: serviceProvider.GetService<IHonuaServerBoundClientFactory>() is not null)));
        }

        services.AddServerBound<IStudioMapStyleCatalogDataSource, HonuaServerStudioMapStyleCatalogDataSource, UnsupportedStudioMapStyleCatalogDataSource>(server);
    }

    private static void AddWorkflowPackages(IServiceCollection services, HonuaServerBinding server)
    {
        if (server.BaseUri is { } baseUri)
        {
            services.TryAddSingleton<IWorkflowPackageApiClient>(serviceProvider =>
                new HttpWorkflowPackageApiClient(
                    HonuaServerClientFactory.Create(serviceProvider, baseUri, ShellHttpClients.LongRequestTimeout),
                    new WorkflowPackageClientOptions(baseUri, server.AdminApiKey)));
        }

        services.AddServerBound<IStudioWorkflowPackageClient, ServerStudioWorkflowPackageClient, UnsupportedStudioWorkflowPackageClient>(server);
    }

    // Console gates always read the live capability registry when a server is bound; the flag only opts
    // Studio AI into registry-driven intent resolution.
    private static void AddIntentResolution(IServiceCollection services, HonuaServerBinding server, HonuaConsoleShellOptions options)
    {
        if (server.BaseUri is not { } baseUri)
        {
            services.TryAddSingleton<ICapabilityRegistryClient, UnsupportedCapabilityRegistryClient>();
            services.TryAddSingleton<IStudioIntentResolver>(serviceProvider =>
                new NoopStudioIntentResolver(serviceProvider.GetRequiredService<IOmniPromptIntentClassifier>()));
            return;
        }

        services.TryAddSingleton<Honua.Sdk.Studio.Capabilities.IHonuaCapabilityManifestClient>(serviceProvider =>
            new Honua.Sdk.Studio.Capabilities.HonuaCapabilityManifestClient(HonuaServerClientFactory.Create(serviceProvider, baseUri)));
        services.TryAddSingleton<ICapabilityRegistryClient>(serviceProvider =>
            new HonuaServerCapabilityRegistryClient(serviceProvider.GetRequiredService<Honua.Sdk.Studio.Capabilities.IHonuaCapabilityManifestClient>()));
        services.TryAddSingleton<IStudioIntentResolver>(serviceProvider =>
            options.RegistryIntentResolutionEnabled
                ? new StudioIntentResolver(
                    serviceProvider.GetRequiredService<IOmniPromptIntentClassifier>(),
                    serviceProvider.GetRequiredService<ICapabilityRegistryClient>())
                : new NoopStudioIntentResolver(serviceProvider.GetRequiredService<IOmniPromptIntentClassifier>()));
    }

    private static void AddCatalogAndShare(IServiceCollection services, HonuaServerBinding server)
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

    private static void AddOperate(IServiceCollection services, HonuaServerBinding server, HonuaConsoleShellOptions options)
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

    // These clients resolve the active environment profile per request, so they register without a base URL
    // and render the missing-binding state while no environment is connected.
    private static void AddOperateObservability(IServiceCollection services, HonuaConsoleShellOptions options)
    {
        var adminApiKey = options.ServerAdminApiKey;
        var credentialMode = ConsoleServerCredentialModeParser.Parse(options.ServerCredentialMode);

        services.TryAddSingleton<IConsoleOperateObservabilityClient>(serviceProvider =>
            new HttpConsoleOperateObservabilityClient(
                ShellHttpClients.ServerBound(serviceProvider),
                serviceProvider.GetRequiredService<IConsoleEnvironmentProfileStore>(),
                serviceProvider.GetRequiredService<IConsoleAccountSessionStore>(),
                adminApiKey,
                serviceProvider.GetService<ILogger<HttpConsoleOperateObservabilityClient>>()));
        services.TryAddSingleton<IConsoleGitOpsReleaseClient>(serviceProvider =>
            new HttpConsoleGitOpsReleaseClient(
                ShellHttpClients.ServerBound(serviceProvider),
                serviceProvider.GetRequiredService<IConsoleEnvironmentProfileStore>(),
                adminApiKey));
        AddDeployments(services, adminApiKey, credentialMode);
        AddProposals(services, adminApiKey, credentialMode);
        services.TryAddSingleton<IConsoleServerVersionClient>(serviceProvider =>
            new HttpConsoleServerVersionClient(
                ShellHttpClients.ServerBound(serviceProvider),
                serviceProvider.GetRequiredService<IConsoleEnvironmentProfileStore>(),
                adminApiKey));
        services.TryAddSingleton<IConsoleMonitoringMetricsClient>(serviceProvider =>
            new HttpConsoleMonitoringMetricsClient(
                ShellHttpClients.ServerBound(serviceProvider),
                serviceProvider.GetRequiredService<IConsoleEnvironmentProfileStore>(),
                adminApiKey));
        services.TryAddSingleton<IOperateMetricsDataSource>(serviceProvider =>
            new OperateMetricsDataSource(serviceProvider.GetRequiredService<IConsoleMonitoringMetricsClient>()));
        AddOpsHealth(services, adminApiKey, credentialMode);
    }

    // Interactive approvals require the operator bearer; the admin API key serves only sessionless
    // HeadlessService callers.
    private static void AddDeployments(IServiceCollection services, string? adminApiKey, ConsoleServerCredentialMode credentialMode)
    {
        services.TryAddSingleton<IConsoleDeployApprovalClient>(serviceProvider =>
            new HttpConsoleDeployApprovalClient(
                ShellHttpClients.ManagerBound(serviceProvider),
                serviceProvider.GetRequiredService<IConsoleEnvironmentProfileStore>(),
                serviceProvider.GetRequiredService<IConsoleAccountSessionStore>(),
                adminApiKey,
                serviceProvider.GetRequiredService<IConsoleOperatorBearerProvider>(),
                credentialMode));
        services.TryAddSingleton<IConsoleDeployOperationsClient>(serviceProvider =>
            new HttpConsoleDeployOperationsClient(
                ShellHttpClients.ManagerBound(serviceProvider),
                serviceProvider.GetRequiredService<IConsoleEnvironmentProfileStore>(),
                adminApiKey));
        services.TryAddScoped<IConsoleDeployOperationRealtimeClient>(serviceProvider =>
            new SignalRConsoleDeployOperationRealtimeClient(
                serviceProvider.GetRequiredService<IConsoleEnvironmentProfileStore>(),
                serviceProvider.GetRequiredService<IConsoleAccountSessionStore>(),
                serviceProvider.GetRequiredService<ILogger<SignalRConsoleDeployOperationRealtimeClient>>(),
                adminApiKey,
                serviceProvider.GetService<IConsoleManagerEndpoint>()));
    }

    // A denied approve/reject surfaces as Forbidden; a human decision never falls back to the admin API key.
    private static void AddProposals(IServiceCollection services, string? adminApiKey, ConsoleServerCredentialMode credentialMode)
    {
        services.TryAddSingleton<IConsoleProposalsClient>(serviceProvider =>
        {
            var profiles = serviceProvider.GetRequiredService<IConsoleEnvironmentProfileStore>();
            var sessions = serviceProvider.GetRequiredService<IConsoleAccountSessionStore>();
            var bearers = serviceProvider.GetRequiredService<IConsoleOperatorBearerProvider>();
            return new DualConsoleProposalsClient(
                new HttpConsoleProposalsClient(ShellHttpClients.ServerBound(serviceProvider), profiles, sessions,
                    adminApiKey, bearers, credentialMode),
                new HttpConsoleProposalsClient(ShellHttpClients.ManagerBound(serviceProvider), profiles, sessions,
                    adminApiKey, bearers, credentialMode),
                profiles, serviceProvider.GetService<IConsoleManagerEndpoint>());
        });
        services.TryAddSingleton<IConsoleReleaseWitnessClient>(serviceProvider =>
            new HttpConsoleReleaseWitnessClient(
                ShellHttpClients.ServerBound(serviceProvider),
                serviceProvider.GetRequiredService<IConsoleEnvironmentProfileStore>(),
                serviceProvider.GetRequiredService<IConsoleAccountSessionStore>(),
                adminApiKey));
        services.TryAddScoped<IConsoleProposalRealtimeClient>(serviceProvider =>
            new SignalRConsoleProposalRealtimeClient(
                serviceProvider.GetRequiredService<IConsoleEnvironmentProfileStore>(),
                serviceProvider.GetRequiredService<IConsoleAccountSessionStore>(),
                serviceProvider.GetRequiredService<ILogger<SignalRConsoleProposalRealtimeClient>>(),
                adminApiKey,
                serviceProvider.GetService<IConsoleManagerEndpoint>()));

        // honua-devops exposes no Console proposals endpoint, so its source reads an empty, allowed result.
        services.TryAddSingleton<IConsoleDevOpsProposalsClient, UnavailableConsoleDevOpsProposalsClient>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConsoleProposalSource, ServerConsoleProposalSource>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConsoleProposalSource, DevOpsConsoleProposalSource>());
        services.TryAddSingleton<IConsoleApprovalInboxClient>(serviceProvider =>
            new ConsoleApprovalInboxClient(serviceProvider.GetServices<IConsoleProposalSource>()));
    }

    private static void AddOpsHealth(IServiceCollection services, string? adminApiKey, ConsoleServerCredentialMode credentialMode)
    {
        services.TryAddSingleton<IConsoleOpsHealthClient>(serviceProvider =>
            new HttpConsoleOpsHealthClient(
                ShellHttpClients.ManagerBound(serviceProvider),
                serviceProvider.GetRequiredService<IConsoleEnvironmentProfileStore>(),
                adminApiKey));
        services.TryAddSingleton<IOpsHealthDataSource>(serviceProvider =>
            new OpsHealthDataSource(serviceProvider.GetRequiredService<IConsoleOpsHealthClient>()));
        services.TryAddScoped<IConsoleOpsHealthRealtimeClient>(serviceProvider =>
            new SignalRConsoleOpsHealthRealtimeClient(
                serviceProvider.GetRequiredService<IConsoleEnvironmentProfileStore>(),
                serviceProvider.GetRequiredService<IConsoleAccountSessionStore>(),
                serviceProvider.GetRequiredService<ILogger<SignalRConsoleOpsHealthRealtimeClient>>(),
                adminApiKey,
                serviceProvider.GetService<IConsoleManagerEndpoint>()));
        services.TryAddSingleton<IConsoleOpsFindingsClient>(serviceProvider =>
            new HttpConsoleOpsFindingsClient(
                ShellHttpClients.ManagerBound(serviceProvider),
                serviceProvider.GetRequiredService<IConsoleEnvironmentProfileStore>(),
                serviceProvider.GetRequiredService<IConsoleAccountSessionStore>(),
                adminApiKey,
                serviceProvider.GetRequiredService<IConsoleOperatorBearerProvider>(),
                credentialMode));
        services.TryAddSingleton<IConsoleOpsAutonomyClient>(serviceProvider =>
            new HttpConsoleOpsAutonomyClient(
                ShellHttpClients.ManagerBound(serviceProvider),
                serviceProvider.GetRequiredService<IConsoleEnvironmentProfileStore>(),
                serviceProvider.GetRequiredService<IConsoleAccountSessionStore>(),
                adminApiKey,
                serviceProvider.GetRequiredService<IConsoleOperatorBearerProvider>(),
                credentialMode));
    }

    private static void AddSupport(IServiceCollection services, HonuaConsoleShellOptions options)
    {
        AddTicketClient(services, options.SupportBaseUrl);

        // Validates every bundle against the pinned diagnostic-bundle.v1 schema before it can leave the Console.
        services.TryAddSingleton<Diagnostics.DiagnosticBundleExporter>();
        services.TryAddSingleton<ConsoleSupportContextBuilder>(serviceProvider =>
            new ConsoleSupportContextBuilder(
                serviceProvider.GetRequiredService<IConsoleEnvironmentProfileStore>(),
                serviceProvider.GetRequiredService<IConsoleAccountSessionStore>(),
                serviceProvider.GetRequiredService<IConsoleOperateObservabilityClient>()));

        AddAssistantClient(services, options);
        services.TryAddSingleton<IConsoleSupportKnowledgeBase>(_ => FileConsoleSupportKnowledgeBase.Load(options.SupportKbPath));
        services.TryAddSingleton<IConsoleSupportDeflectionLog, ConsoleSupportDeflectionLog>();
    }

    private static void AddTicketClient(IServiceCollection services, string? supportBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(supportBaseUrl)
            || !Uri.TryCreate(supportBaseUrl.Trim(), UriKind.Absolute, out var supportBaseUri))
        {
            services.TryAddSingleton<IConsoleSupportTicketClient, UnsupportedSupportTicketClient>();
            return;
        }

        services.TryAddSingleton<IConsoleSupportTicketClient>(serviceProvider =>
            new HttpSupportTicketClient(
                ShellHttpClients.Standalone(ShellHttpClients.ReadTimeout),
                supportBaseUri,
                serviceProvider.GetRequiredService<IConsoleEnvironmentProfileStore>(),
                serviceProvider.GetRequiredService<IConsoleAccountSessionStore>()));
    }

    // Without both an http(s) endpoint and a model id the /support page hides the assistant.
    private static void AddAssistantClient(IServiceCollection services, HonuaConsoleShellOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.LlmBaseUrl)
            || string.IsNullOrWhiteSpace(options.LlmModel)
            || !Uri.TryCreate(options.LlmBaseUrl.Trim(), UriKind.Absolute, out var llmBaseUri)
            || (llmBaseUri.Scheme != Uri.UriSchemeHttp && llmBaseUri.Scheme != Uri.UriSchemeHttps))
        {
            services.TryAddSingleton<IConsoleSupportAssistantClient, UnsupportedConsoleSupportAssistantClient>();
            return;
        }

        var model = options.LlmModel.Trim();
        services.TryAddSingleton<IConsoleSupportAssistantClient>(_ =>
            new HttpConsoleSupportAssistantClient(
                ShellHttpClients.Standalone(ShellHttpClients.LongRequestTimeout),
                llmBaseUri,
                model,
                options.LlmApiKey));
    }
}
