using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

public sealed record ConsolePublicationLayer(string ResourceId, string Name, string GeometryType);

public interface IConsolePublicationNavigationSource
{
    Task<OperateSectionResult<IReadOnlyList<HonuaAdminServiceSummary>>> ReadServicesAsync(CancellationToken cancellationToken);
    Task<OperateSectionResult<IReadOnlyList<string>>> ReadConnectionsAsync(CancellationToken cancellationToken);
    Task<OperateSectionResult<IReadOnlyList<ConsolePublicationLayer>>> ReadLayersAsync(
        string serviceName, IReadOnlyList<string> connectionKeys, CancellationToken cancellationToken);
}

public sealed class UnsupportedConsolePublicationNavigationSource : IConsolePublicationNavigationSource
{
    private static Task<OperateSectionResult<T>> Missing<T>() => Task.FromResult(
        OperateSectionResult<T>.Denied(OperateSectionStatus.Missing, "Connect an environment to browse services."));

    public Task<OperateSectionResult<IReadOnlyList<HonuaAdminServiceSummary>>> ReadServicesAsync(CancellationToken cancellationToken) =>
        Missing<IReadOnlyList<HonuaAdminServiceSummary>>();
    public Task<OperateSectionResult<IReadOnlyList<string>>> ReadConnectionsAsync(CancellationToken cancellationToken) =>
        Missing<IReadOnlyList<string>>();
    public Task<OperateSectionResult<IReadOnlyList<ConsolePublicationLayer>>> ReadLayersAsync(
        string serviceName, IReadOnlyList<string> connectionKeys, CancellationToken cancellationToken) =>
        Missing<IReadOnlyList<ConsolePublicationLayer>>();
}
