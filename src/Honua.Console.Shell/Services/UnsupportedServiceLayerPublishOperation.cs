using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Missing-binding implementation of the service-layer-publish operation. Used when no honua-server base URL
/// is configured: it performs no network call and returns an explicit missing-binding result so the
/// publishing wizard surfaces the binding requirement instead of fabricating a publish (Console Patterns
/// Charter section 11).
/// </summary>
public sealed class UnsupportedServiceLayerPublishOperation : IServiceLayerPublishOperation
{
    public Task<ServiceLayerPublishResult> PublishAsync(
        ServiceLayerPublishCommand command,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ServiceLayerPublishResult.MissingBinding(
            "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so the console can publish layers to honua-server."));

    public Task<ServiceProtocolEnableResult> EnableProtocolsAsync(
        string serviceName,
        IReadOnlyList<string> protocols,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ServiceProtocolEnableResult.MissingBinding(
            "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so the console can enable service protocols on honua-server."));

    public Task<IReadOnlyList<ServiceLayerPublishTable>> ListTablesAsync(
        string connectionId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ServiceLayerPublishTable>>([]);
}
