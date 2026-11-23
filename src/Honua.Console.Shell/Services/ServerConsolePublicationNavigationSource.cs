using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class ServerConsolePublicationNavigationSource(IHonuaAdminOperateClient client) : IConsolePublicationNavigationSource
{
    private const int ConcurrentConnectionReads = 2;
    private const int LayerSearchPageSize = 25;

    public async Task<OperateSectionResult<HonuaAdminServicePage>> ReadServicesAsync(
        HonuaAdminCatalogQuery query, CancellationToken cancellationToken)
    {
        var result = await client.ListServicesAsync(query, cancellationToken);
        if (result.Issue is { } issue) return Denied<HonuaAdminServicePage>(issue);
        if (result.Data is null || !HonuaAdminCatalogReader.IsValidPage(result.Data, query) || (result.Data.Items ?? []).Any(service => string.IsNullOrWhiteSpace(service?.ServiceName)))
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
            (connectionKey, token) => ReadConnectionLayerSummariesAsync(connectionKey, serviceName, token),
            maxConcurrency: ConcurrentConnectionReads, cancellationToken: cancellationToken);
        var layers = new List<ConsolePublicationLayer>();
        for (var index = 0; index < reads.Length; index++)
            layers.AddRange(ProjectServiceLayers(connectionKeys[index], reads[index].Data ?? [], serviceName));
        return Merge(reads, Ordered(layers, layer => layer.ResourceId, layer => layer.Name), LayerBrowse);
    }

    public async Task<OperateSectionResult<IReadOnlyList<ConsolePublicationLayerMatch>>> SearchLayersAsync(
        string nameQuery, IReadOnlyList<string> connectionKeys, CancellationToken cancellationToken)
    {
        if (connectionKeys.Count == 0) return OperateSectionResult<IReadOnlyList<ConsolePublicationLayerMatch>>.Allowed([]);
        var query = HonuaAdminCatalogQuery.First with { PageSize = LayerSearchPageSize, NameQuery = nameQuery };
        var reads = await BoundedFanOut.RunAsync(connectionKeys,
            (connectionKey, token) => client.ListConnectionLayersAsync(connectionKey, new(null, query), token),
            maxConcurrency: ConcurrentConnectionReads, cancellationToken: cancellationToken);
        var matches = connectionKeys.Zip(reads).SelectMany(read => (read.Second.Data?.Items ?? [])
            .Select(layer => new ConsolePublicationLayerMatch(ProjectLayer(read.First, layer),
                string.IsNullOrWhiteSpace(layer.ServiceName) ? null : layer.ServiceName))).ToList();
        var result = Merge(reads, Ordered(matches, match => match.Layer.ResourceId, match => match.Layer.Name), LayerSearch);
        var truncated = reads.Any(read => read.Data is { } page && page.Total > page.Items.Length);
        return truncated && result is { IsAllowed: true, PartialResult: false }
            ? result with { PartialResult = true, Message = $"Showing the first {LayerSearchPageSize} matching layers from each connection. Search with more of the name to narrow the list." }
            : result;
    }

    private Task<HonuaAdminEndpointResult<IReadOnlyList<HonuaAdminPublishedLayerSummary>>> ReadConnectionLayerSummariesAsync(
        string connectionKey, string? serviceName, CancellationToken cancellationToken) =>
        HonuaAdminCatalogReader.ReadAllAsync(
            (page, token) => client.ListConnectionLayersAsync(connectionKey, new(serviceName, page), token),
            page => page.Items, page => page.Total, cancellationToken);

    private static IEnumerable<ConsolePublicationLayer> ProjectServiceLayers(
        string connectionKey, IReadOnlyList<HonuaAdminPublishedLayerSummary> layers, string serviceName) => layers
        .Where(layer => string.IsNullOrWhiteSpace(layer.ServiceName) || string.Equals(layer.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase))
        .Select(layer => ProjectLayer(connectionKey, layer));

    private static ConsolePublicationLayer ProjectLayer(string connectionKey, HonuaAdminPublishedLayerSummary layer) =>
        ConsolePublicationLayer.Project(OperateResourceIdentity.ForPublishedLayer(connectionKey, layer.LayerId),
            string.IsNullOrWhiteSpace(layer.LayerName) ? $"Layer {layer.LayerId}" : layer.LayerName, layer.GeometryType);

    private static T[] Ordered<T>(IEnumerable<T> items, Func<T, string> identity, Func<T, string> name) =>
        items.DistinctBy(identity).OrderBy(name, StringComparer.OrdinalIgnoreCase).ToArray();

    private sealed record LayerReadSubject(string Name, string PartialMessage);

    private static readonly LayerReadSubject LayerBrowse = new("Service navigation", "Some connections could not be read. These layers are partial results.");
    private static readonly LayerReadSubject LayerSearch = new("Layer search", "Some connections could not be searched. These layers are partial results.");

    // One unreadable connection keeps the layers the others returned and marks them partial.
    private static OperateSectionResult<IReadOnlyList<T>> Merge<T, TPage>(
        IReadOnlyList<HonuaAdminEndpointResult<TPage>> reads, IReadOnlyList<T> items, LayerReadSubject subject)
    {
        var failure = reads.FirstOrDefault(read => read.Issue is not null || read.Data is null);
        if (failure is not null && items.Count == 0)
            return failure.Issue is { } issue ? Denied<IReadOnlyList<T>>(issue, subject.Name) : Unreadable<IReadOnlyList<T>>();
        return OperateSectionResult<IReadOnlyList<T>>.Allowed(items, partialResult: failure is not null,
            message: failure is null ? "" : subject.PartialMessage);
    }

    private static OperateSectionResult<T> Unreadable<T>() =>
        OperateSectionResult<T>.Denied(OperateSectionStatus.Unavailable, "The server did not return navigation data.");

    private static OperateSectionResult<T> Denied<T>(HonuaAdminEndpointIssue issue, string subject = "Service navigation")
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
            : $"{subject}: {OperateSectionPresentation.Title(status)}.");
    }
}
