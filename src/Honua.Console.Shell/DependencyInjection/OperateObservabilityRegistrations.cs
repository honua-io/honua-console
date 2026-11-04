using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Honua.Console.Shell.DependencyInjection;

// These clients resolve the active environment profile per request, so they register without a base URL
// and render the missing-binding state while no environment is connected.
internal static class OperateObservabilityRegistrations
{
    public static void AddOperateObservability(IServiceCollection services, HonuaConsoleShellOptions options)
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
}
