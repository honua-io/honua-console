namespace Honua.Console.Shell.Models;

public sealed record DatasetTreeNode(
    string DatasetId,
    string Name,
    string Source,
    string Status,
    long FeatureCount,
    string? GeometryType,
    IReadOnlyList<DatasetEndpointNode> Endpoints)
{
    public bool HasEndpoints => Endpoints.Count > 0;
}

public sealed record DatasetEndpointNode(
    string ProtocolId,
    string ProtocolLabel,
    bool IsLive,
    string? Route)
{
    public string? ServiceName { get; init; }
}

public static class DatasetEndpointsTreeBuilder
{
    public static IReadOnlyList<DatasetTreeNode> Build(
        IReadOnlyList<OperateDatasetEditPreview> datasets,
        IReadOnlyList<OperateServiceDetail> services)
    {
        ArgumentNullException.ThrowIfNull(datasets);
        ArgumentNullException.ThrowIfNull(services);

        // Map each canonical dataset id → the (serviceType, serviceName) pairs that publish it.
        var endpointsByDataset = new Dictionary<string, List<(string ServiceType, string? ServiceName, string? FolderName, string ServiceIdentity)>>(StringComparer.Ordinal);
        foreach (var service in services)
        {
            foreach (var layer in service.Layers)
            {
                if (string.IsNullOrWhiteSpace(layer.CanonicalResourceId))
                {
                    continue;
                }

                if (!endpointsByDataset.TryGetValue(layer.CanonicalResourceId, out var list))
                {
                    list = [];
                    endpointsByDataset[layer.CanonicalResourceId] = list;
                }

                list.Add((service.ServiceType, service.PublicServiceName, service.FolderName, service.Name));
            }
        }

        var nodes = new List<DatasetTreeNode>(datasets.Count);
        foreach (var dataset in datasets)
        {
            endpointsByDataset.TryGetValue(dataset.ResourceId, out var endpoints);
            var endpointNodes = BuildEndpointNodes(endpoints);
            nodes.Add(new DatasetTreeNode(
                dataset.ResourceId,
                dataset.Name,
                dataset.Source,
                Status: endpointNodes.Count > 0 ? "Running" : "Draft",
                FeatureCount: 0,
                GeometryType: null,
                Endpoints: endpointNodes));
        }

        return nodes;
    }

    private static IReadOnlyList<DatasetEndpointNode> BuildEndpointNodes(
        List<(string ServiceType, string? ServiceName, string? FolderName, string ServiceIdentity)>? endpoints)
    {
        if (endpoints is null || endpoints.Count == 0)
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var nodes = new List<DatasetEndpointNode>();
        foreach (var (serviceType, serviceName, folderName, serviceIdentity) in endpoints)
        {
            // A service's ServiceType can arrive as a metadata-v2 string (esri-feature-service), a joined
            // list of protocol names (FeatureServer, MapServer), or a display name (Feature service) depending
            // on the source — resolve it to its protocol descriptors so existing published datasets show as
            // Running with previewable endpoints instead of falling through to Draft.
            foreach (var descriptor in PublishProtocolCatalog.ResolveServiceTypeProtocols(serviceType))
            {
                if (!seen.Add(descriptor.Id))
                {
                    continue;
                }

                nodes.Add(new DatasetEndpointNode(
                    descriptor.Id,
                    descriptor.Label,
                    IsLive: true,
                    Route: HonuaServedRoutes.PathFor(serviceName is null ? null : PublishProtocolCatalog.ServiceNameFor(serviceName), descriptor.Id, folderName))
                { ServiceName = serviceIdentity });
            }
        }

        // Stable protocol order (catalog order) for display.
        return nodes
            .OrderBy(node => PublishProtocolCatalog.All.ToList().FindIndex(d => d.Id == node.ProtocolId))
            .ToArray();
    }
}
