using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

public sealed record ConsolePublicationLayer(string ResourceId, string Name, string GeometryType);

public interface IConsolePublicationNavigationSource
{
    Task<OperateSectionResult<HonuaAdminServicePage>> ReadServicesAsync(
        HonuaAdminCatalogQuery query, CancellationToken cancellationToken);
    Task<OperateSectionResult<IReadOnlyList<HonuaAdminPublishedLayerSummary>>> ReadConnectionLayersAsync(
        string connectionKey, CancellationToken cancellationToken);
    Task<OperateSectionResult<IReadOnlyList<string>>> ReadConnectionsAsync(CancellationToken cancellationToken);
    Task<OperateSectionResult<IReadOnlyList<ConsolePublicationLayer>>> ReadLayersAsync(
        string serviceName, IReadOnlyList<string> connectionKeys, CancellationToken cancellationToken);
}

public sealed class UnsupportedConsolePublicationNavigationSource : IConsolePublicationNavigationSource
{
    private static Task<OperateSectionResult<T>> Missing<T>() => Task.FromResult(
        OperateSectionResult<T>.Denied(OperateSectionStatus.Missing, "Connect an environment to browse services."));

    public Task<OperateSectionResult<HonuaAdminServicePage>> ReadServicesAsync(
        HonuaAdminCatalogQuery query, CancellationToken cancellationToken) =>
        Missing<HonuaAdminServicePage>();
    public Task<OperateSectionResult<IReadOnlyList<HonuaAdminPublishedLayerSummary>>> ReadConnectionLayersAsync(
        string connectionKey, CancellationToken cancellationToken) =>
        Missing<IReadOnlyList<HonuaAdminPublishedLayerSummary>>();
    public Task<OperateSectionResult<IReadOnlyList<string>>> ReadConnectionsAsync(CancellationToken cancellationToken) =>
        Missing<IReadOnlyList<string>>();
    public Task<OperateSectionResult<IReadOnlyList<ConsolePublicationLayer>>> ReadLayersAsync(
        string serviceName, IReadOnlyList<string> connectionKeys, CancellationToken cancellationToken) =>
        Missing<IReadOnlyList<ConsolePublicationLayer>>();
}
