namespace Honua.Console.Shell.Models;

/// <summary>
/// The GeoServer-style resource→publications tree model (redesign §5.3). Each <see cref="ResourceTreeNode"/>
/// is a canonical resource; its <see cref="ResourceTreeNode.Publications"/> are the protocol bindings that
/// expose it (a checked protocol == a live metadata-v2 Publication; an unchecked one is one click away via
/// "+ expose"). This is the structural cure for the resources-vs-layers split: the resource is the parent
/// and its publications hang under it, instead of a flat layer list with a "Canonical resource" back-link.
/// </summary>
public sealed record ResourceTreeNode(
    string ResourceId,
    string Name,
    string Source,
    string Status,
    long FeatureCount,
    string? GeometryType,
    IReadOnlyList<PublicationTreeNode> Publications)
{
    /// <summary>True when the resource has at least one live publication (a "Running" resource); false = draft.</summary>
    public bool HasPublications => Publications.Count > 0;
}

/// <summary>
/// One protocol publication under a resource: the protocol id/label, whether it is live, and the served
/// route the publication exposes. Reuses <see cref="PublishProtocolCatalog"/> for label/route so the tree
/// and the publish step agree on the mapping.
/// </summary>
public sealed record PublicationTreeNode(
    string ProtocolId,
    string ProtocolLabel,
    bool IsLive,
    string Route);

/// <summary>
/// Builds the resource→publications tree from the Operate workspace projections. Resources come from the
/// resource edits; their publications are derived by grouping the services' published layers under their
/// <c>CanonicalResourceId</c> and mapping each service's type onto the protocol catalog. Never fabricates:
/// a resource with no published layers renders as a draft node with no publications.
/// </summary>
public static class ResourcePublicationsTreeBuilder
{
    public static IReadOnlyList<ResourceTreeNode> Build(
        IReadOnlyList<OperateResourceEditPreview> resources,
        IReadOnlyList<OperateServiceDetail> services)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(services);

        // Map each canonical resource id → the (serviceType, serviceName) pairs that publish it.
        var publicationsByResource = new Dictionary<string, List<(string ServiceType, string ServiceSlot)>>(StringComparer.Ordinal);
        foreach (var service in services)
        {
            foreach (var layer in service.Layers)
            {
                if (string.IsNullOrWhiteSpace(layer.CanonicalResourceId))
                {
                    continue;
                }

                if (!publicationsByResource.TryGetValue(layer.CanonicalResourceId, out var list))
                {
                    list = [];
                    publicationsByResource[layer.CanonicalResourceId] = list;
                }

                list.Add((service.ServiceType, service.Name));
            }
        }

        var nodes = new List<ResourceTreeNode>(resources.Count);
        foreach (var resource in resources)
        {
            publicationsByResource.TryGetValue(resource.ResourceId, out var publications);
            var publicationNodes = BuildPublicationNodes(publications);
            nodes.Add(new ResourceTreeNode(
                resource.ResourceId,
                resource.Name,
                resource.Source,
                Status: publicationNodes.Count > 0 ? "Running" : "Draft",
                FeatureCount: 0,
                GeometryType: null,
                Publications: publicationNodes));
        }

        return nodes;
    }

    private static IReadOnlyList<PublicationTreeNode> BuildPublicationNodes(
        List<(string ServiceType, string ServiceSlot)>? publications)
    {
        if (publications is null || publications.Count == 0)
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var nodes = new List<PublicationTreeNode>();
        foreach (var (serviceType, serviceSlot) in publications)
        {
            // A service's ServiceType can arrive as a metadata-v2 string (esri-feature-service), a joined
            // list of protocol names (FeatureServer, MapServer), or a display name (Feature service) depending
            // on the source — resolve it to its protocol descriptors so existing published resources show as
            // Running with previewable publications instead of falling through to Draft.
            foreach (var descriptor in PublishProtocolCatalog.ResolveServiceTypeProtocols(serviceType))
            {
                if (!seen.Add(descriptor.Id))
                {
                    continue;
                }

                nodes.Add(new PublicationTreeNode(
                    descriptor.Id,
                    descriptor.Label,
                    IsLive: true,
                    Route: PublishProtocolCatalog.RouteFor(descriptor.Id, serviceSlot)));
            }
        }

        // Stable protocol order (catalog order) for display.
        return nodes
            .OrderBy(node => PublishProtocolCatalog.All.ToList().FindIndex(d => d.Id == node.ProtocolId))
            .ToArray();
    }
}
