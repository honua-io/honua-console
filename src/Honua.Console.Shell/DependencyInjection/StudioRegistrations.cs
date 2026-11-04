using Honua.Console.Contracts;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Honua.Console.Shell.DependencyInjection;

internal static class StudioRegistrations
{
    public static void AddStudio(IServiceCollection services, HonuaServerBinding server, HonuaConsoleShellOptions options)
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
}
