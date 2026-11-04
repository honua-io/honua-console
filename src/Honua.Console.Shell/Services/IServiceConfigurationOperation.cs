using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IServiceConfigurationOperation
{
    Task<ServiceSettingsView> GetSettingsAsync(
        string serviceName,
        CancellationToken cancellationToken = default);

    Task<ServiceConfigurationResult> SetLayerEnabledAsync(
        ServiceLayerEnableCommand command,
        CancellationToken cancellationToken = default);

    Task<ServiceConfigurationResult> UpdateProtocolsAsync(
        ServiceProtocolsCommand command,
        CancellationToken cancellationToken = default);

    Task<ServiceConfigurationResult> UpdateAccessPolicyAsync(
        ServiceAccessPolicyCommand command,
        CancellationToken cancellationToken = default);

    Task<ServiceConfigurationResult> UpdateMapServerSettingsAsync(
        ServiceMapServerSettingsCommand command,
        CancellationToken cancellationToken = default);

}
