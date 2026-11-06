using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class ServerConsolePublicationNavigationSource(IHonuaAdminOperateClient client) : IConsolePublicationNavigationSource
{
    private const int ConcurrentConnectionReads = 2;

    public async Task<OperateSectionResult<HonuaAdminServicePage>> ReadServicesAsync(
        HonuaAdminCatalogQuery query, CancellationToken cancellationToken)
    {
        var result = await client.ListServicesAsync(query, cancellationToken);
        if (result.Issue is { } issue) return Denied<HonuaAdminServicePage>(issue);
        if (result.Data is null || (result.Data.Items ?? []).Any(service => string.IsNullOrWhiteSpace(service?.ServiceName)))
            return Unreadable<HonuaAdminServicePage>();
        return OperateSectionResult<HonuaAdminServicePage>.Allowed(result.Data);
    }

    public async Task<OperateSectionResult<IReadOnlyList<string>>> ReadConnectionsAsync(CancellationToken cancellationToken)
    {
        var result = await client.ListConnectionsAsync(cancellationToken);
        if (result.Issue is { } issue) return Denied<IReadOnlyList<string>>(issue);
        if (result.Data is null) return Unreadable<IReadOnlyList<string>>();
        return OperateSectionResult<IReadOnlyList<string>>.Allowed(result.Data.Select(OperateResourceIdentity.ForConnection).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public async Task<OperateSectionResult<IReadOnlyList<ConsolePublicationLayer>>> ReadLayersAsync(
        string serviceName, IReadOnlyList<string> connectionKeys, CancellationToken cancellationToken)
    {
        if (connectionKeys.Count == 0)
            return OperateSectionResult<IReadOnlyList<ConsolePublicationLayer>>.Denied(OperateSectionStatus.Unsupported,
                "The server returned no connections for browsing this service's layers.");
        var reads = await BoundedFanOut.RunAsync(connectionKeys,
            (connectionKey, token) => HonuaAdminCatalogReader.ReadAllAsync(
                (page, readToken) => client.ListConnectionLayersAsync(connectionKey, new HonuaAdminLayerListRequest(serviceName, page), readToken),
                page => page.Items,
                page => page.Total,
                token),
            maxConcurrency: ConcurrentConnectionReads, cancellationToken: cancellationToken);
        var layers = new List<ConsolePublicationLayer>();
        for (var index = 0; index < reads.Length; index++)
            layers.AddRange(ProjectServiceLayers(connectionKeys[index], reads[index].Data ?? [], serviceName));

        var failure = reads.FirstOrDefault(read => read.Issue is not null || read.Data is null);
        if (failure is not null && layers.Count == 0)
            return failure.Issue is { } issue ? Denied<IReadOnlyList<ConsolePublicationLayer>>(issue) : Unreadable<IReadOnlyList<ConsolePublicationLayer>>();
        return OperateSectionResult<IReadOnlyList<ConsolePublicationLayer>>.Allowed(
            layers.DistinctBy(layer => layer.ResourceId).OrderBy(layer => layer.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
            partialResult: failure is not null,
            message: failure is null ? "" : "Some connections could not be read. These layers are partial results.");
    }

    private static IEnumerable<ConsolePublicationLayer> ProjectServiceLayers(
        string connectionKey, IReadOnlyList<HonuaAdminPublishedLayerSummary> layers, string serviceName) => layers
        .Where(layer => string.IsNullOrWhiteSpace(layer.ServiceName) || string.Equals(layer.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase))
        .Select(layer => new ConsolePublicationLayer(OperateResourceIdentity.ForPublishedLayer(connectionKey, layer.LayerId),
            string.IsNullOrWhiteSpace(layer.LayerName) ? $"Layer {layer.LayerId}" : layer.LayerName, layer.GeometryType ?? "Unknown"));

    private static OperateSectionResult<T> Unreadable<T>() =>
        OperateSectionResult<T>.Denied(OperateSectionStatus.Unavailable, "The server did not return navigation data.");

    private static OperateSectionResult<T> Denied<T>(HonuaAdminEndpointIssue issue)
    {
        var status = issue.StatusCode switch
        {
            401 or 403 => OperateSectionStatus.Forbidden,
            404 => OperateSectionStatus.Missing,
            501 => OperateSectionStatus.Unsupported,
            _ when issue.State == "Unsupported" => OperateSectionStatus.Unsupported,
            _ => OperateSectionStatus.Unavailable
        };
        return OperateSectionResult<T>.Denied(status, issue.StatusCode == 401
            ? "Your sign-in has expired. Sign in again to browse services."
            : $"Service navigation: {OperateSectionPresentation.Title(status)}.");
    }
}
