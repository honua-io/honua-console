using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

public sealed record ConsolePublicationLayer(string ResourceId, string Name, string GeometryType);

public interface IConsolePublicationNavigationSource
{
    Task<OperateSectionResult<IReadOnlyList<HonuaAdminServiceSummary>>> ReadServicesAsync(CancellationToken cancellationToken);
    Task<OperateSectionResult<IReadOnlyList<Guid>>> ReadConnectionsAsync(CancellationToken cancellationToken);
    Task<OperateSectionResult<IReadOnlyList<ConsolePublicationLayer>>> ReadLayersAsync(
        string serviceName, IReadOnlyList<Guid> connectionIds, CancellationToken cancellationToken);
}

public sealed class UnsupportedConsolePublicationNavigationSource : IConsolePublicationNavigationSource
{
    private static Task<OperateSectionResult<T>> Missing<T>() => Task.FromResult(
        OperateSectionResult<T>.Denied(OperateSectionStatus.Missing, "Connect an environment to browse services."));

    public Task<OperateSectionResult<IReadOnlyList<HonuaAdminServiceSummary>>> ReadServicesAsync(CancellationToken cancellationToken) =>
        Missing<IReadOnlyList<HonuaAdminServiceSummary>>();
    public Task<OperateSectionResult<IReadOnlyList<Guid>>> ReadConnectionsAsync(CancellationToken cancellationToken) =>
        Missing<IReadOnlyList<Guid>>();
    public Task<OperateSectionResult<IReadOnlyList<ConsolePublicationLayer>>> ReadLayersAsync(
        string serviceName, IReadOnlyList<Guid> connectionIds, CancellationToken cancellationToken) =>
        Missing<IReadOnlyList<ConsolePublicationLayer>>();
}
