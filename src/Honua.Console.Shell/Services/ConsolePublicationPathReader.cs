using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class ConsolePublicationPathReader(IConsolePublicationNavigationSource source)
{
    public async Task<OperateSectionResult<IReadOnlyList<ConsolePublicationNode>>> ReadAsync(
        string path, CancellationToken cancellationToken)
    {
        var connections = await source.ReadConnectionsAsync(cancellationToken);
        if (!connections.IsAllowed || connections.PartialResult || connections.Value is null)
            return Denied(connections.Status, connections.Message);
        var resourceId = Uri.UnescapeDataString(path["/operate/layers/".Length..]);
        var owners = connections.Value.Where(connection => resourceId.StartsWith(
            OperateResourceIdentity.NormalizeToken(connection) + "-layer-", StringComparison.Ordinal)).ToArray();
        if (owners.Length != 1) return Empty();
        var layers = await source.ReadConnectionLayersAsync(owners[0], cancellationToken);
        if (!layers.IsAllowed || layers.PartialResult || layers.Value is null) return Denied(layers.Status, layers.Message);
        var matches = layers.Value.Where(layer => OperateResourceIdentity.ForPublishedLayer(owners[0], layer.LayerId) == resourceId).ToArray();
        if (matches.Length != 1 || string.IsNullOrWhiteSpace(matches[0].ServiceName)) return Empty();
        var layer = matches[0];
        var services = await ReadServicesAsync(layer.ServiceName!, cancellationToken);
        if (!services.IsAllowed || services.Value is null) return Denied(services.Status, services.Message);
        if (services.Value.Count != 1) return Empty();

        var tree = new ConsolePublicationTree();
        tree.Load(new HonuaAdminServicePage { Items = services.Value.ToArray(), Total = 1 }, "Services");
        var service = ConsolePublicationTree.Descendants(tree.Root).Single(node => node.Kind == ConsolePublicationNodeKind.Service);
        service.Children = ConsolePublicationTree.LayerNodes(service,
            [new(resourceId, string.IsNullOrWhiteSpace(layer.LayerName) ? $"Layer {layer.LayerId}" : layer.LayerName, layer.GeometryType ?? "Unknown")]);
        return OperateSectionResult<IReadOnlyList<ConsolePublicationNode>>.Allowed(tree.PathTo(path));
    }

    private static OperateSectionResult<IReadOnlyList<ConsolePublicationNode>> Empty() =>
        OperateSectionResult<IReadOnlyList<ConsolePublicationNode>>.Allowed([]);

    private async Task<OperateSectionResult<IReadOnlyList<HonuaAdminServiceSummary>>> ReadServicesAsync(string serviceName, CancellationToken token)
    {
        var services = new List<HonuaAdminServiceSummary>();
        var query = HonuaAdminCatalogQuery.First with { NameQuery = serviceName };
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var read = await source.ReadServicesAsync(query, token);
            if (!read.IsAllowed || read.PartialResult || read.Value is null)
                return OperateSectionResult<IReadOnlyList<HonuaAdminServiceSummary>>.Denied(read.Status, read.Message);
            var page = read.Value;
            services.AddRange(page.Items);
            if (services.Count >= page.Total)
                return OperateSectionResult<IReadOnlyList<HonuaAdminServiceSummary>>.Allowed(services
                    .Where(service => string.Equals(service.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase)).ToArray());
            if (page.Items.Length == 0 || page.Page != query.Page)
                return OperateSectionResult<IReadOnlyList<HonuaAdminServiceSummary>>.Denied(
                    OperateSectionStatus.Unavailable, "The service catalog did not return a complete page.");
            query = query.Next();
        }
    }

    private static OperateSectionResult<IReadOnlyList<ConsolePublicationNode>> Denied(OperateSectionStatus status, string message) =>
        OperateSectionResult<IReadOnlyList<ConsolePublicationNode>>.Denied(status, message);
}
