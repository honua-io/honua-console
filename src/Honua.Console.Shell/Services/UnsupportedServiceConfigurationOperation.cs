using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class UnsupportedServiceConfigurationOperation : IServiceConfigurationOperation
{
    private const string MissingBindingDetail =
        "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so the console can configure services on honua-server.";

    public Task<ServiceSettingsView> GetSettingsAsync(
        string serviceName,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ServiceSettingsView.Unbound(serviceName, MissingBindingDetail));

    public Task<ServiceConfigurationResult> SetLayerEnabledAsync(
        ServiceLayerEnableCommand command,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ServiceConfigurationResult.MissingBinding(MissingBindingDetail));

    public Task<ServiceConfigurationResult> UpdateProtocolsAsync(
        ServiceProtocolsCommand command,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ServiceConfigurationResult.MissingBinding(MissingBindingDetail));

    public Task<ServiceConfigurationResult> UpdateAccessPolicyAsync(
        ServiceAccessPolicyCommand command,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ServiceConfigurationResult.MissingBinding(MissingBindingDetail));

    public Task<ServiceConfigurationResult> UpdateMapServerSettingsAsync(
        ServiceMapServerSettingsCommand command,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ServiceConfigurationResult.MissingBinding(MissingBindingDetail));

}
