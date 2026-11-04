using Honua.Console.Contracts;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        StudioRegistrations.AddStudio(services, server, options);
        CatalogShareRegistrations.AddCatalogAndShare(services, server);
        OperateRegistrations.AddOperate(services, server, options);
        OperateObservabilityRegistrations.AddOperateObservability(services, options);
        SupportRegistrations.AddSupport(services, options);
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
}
