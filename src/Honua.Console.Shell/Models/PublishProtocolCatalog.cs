namespace Honua.Console.Shell.Models;

/// <summary>
/// The single source of truth for the resource-first publish step (redesign §3.0 / §5.4b): the set of
/// protocol surfaces a <em>resource</em> can be exposed through, and the metadata-v2 mapping each protocol
/// implies — its <c>ServiceProtocols</c> string constant, the <c>MetadataV2ServiceType</c> service category
/// the binding lands under, and a human label/hint for the toggle grid.
///
/// Each enabled protocol toggle corresponds to exactly one metadata-v2 <b>Publication</b> (a binding of
/// <c>resourceId</c> + <c>serviceId</c>). The picker, the treeview's "+ expose" affordance, and the
/// go-live route preview all read this catalog so the protocol→publication mapping is defined once and
/// stays coherent (Console Patterns Charter: no fabricated data, one mapping authority).
/// </summary>
public static class PublishProtocolCatalog
{
    /// <summary>
    /// Protocol identifiers mirroring honua-server <c>ServiceProtocols</c> string constants. The toggle
    /// set maps 1:1 onto the metadata-v2 services that publish a resource (redesign §3.0).
    /// </summary>
    public static class Protocols
    {
        public const string FeatureServer = "FeatureServer";
        public const string MapServer = "MapServer";
        public const string ImageServer = "ImageServer";
        public const string Stac = "Stac";
        public const string OgcFeatures = "OgcFeatures";
        public const string Wms = "Wms";
        public const string Wfs20 = "Wfs20";
        public const string Wmts = "Wmts";
        public const string OData = "OData";
    }

    /// <summary>
    /// A single protocol surface: its <c>ServiceProtocols</c> id, the <c>MetadataV2ServiceType</c> category
    /// its Publication binds under, a display label, a one-line hint, and the toggle-grid group it renders in.
    /// </summary>
    public sealed record ProtocolDescriptor(
        string Id,
        string ServiceType,
        string Label,
        string Hint,
        string Group);

    /// <summary>The toggle-grid groups, in render order (GeoServices · OGC · STAC/Catalog).</summary>
    public static IReadOnlyList<(string Key, string Label)> Groups { get; } =
    [
        ("geoservices", "GeoServices (Esri)"),
        ("ogc", "OGC"),
        ("catalog", "STAC / Catalog"),
    ];

    /// <summary>
    /// Every protocol the publish step exposes, keyed by its <c>ServiceProtocols</c> id. The
    /// <c>ServiceType</c> values mirror <c>MetadataV2ServiceType</c> in honua-server metadata-v2.
    /// </summary>
    public static IReadOnlyList<ProtocolDescriptor> All { get; } =
    [
        new(Protocols.FeatureServer, "esri-feature-service", "FeatureServer", "editable features", "geoservices"),
        new(Protocols.MapServer, "esri-map-service", "MapServer", "rendered tiles", "geoservices"),
        new(Protocols.ImageServer, "esri-image-service", "ImageServer", "raster only", "geoservices"),
        new(Protocols.OgcFeatures, "ogc-api-features", "OGC API Features", "features", "ogc"),
        new(Protocols.Wms, "wms", "WMS", "rendered", "ogc"),
        new(Protocols.Wfs20, "wfs", "WFS", "feature download", "ogc"),
        new(Protocols.Wmts, "wmts", "WMTS", "tiled", "ogc"),
        new(Protocols.OData, "odata", "OData", "tabular/query", "ogc"),
        new(Protocols.Stac, "stac-api", "STAC API", "collection", "catalog"),
    ];

    private static readonly IReadOnlyDictionary<string, ProtocolDescriptor> ById =
        All.ToDictionary(descriptor => descriptor.Id, StringComparer.Ordinal);

    /// <summary>The default-on protocol set for a freshly-staged publish (redesign §5.4b: FeatureServer + MapServer + STAC).</summary>
    public static IReadOnlyList<string> DefaultEnabled { get; } =
    [
        Protocols.FeatureServer,
        Protocols.MapServer,
        Protocols.Stac,
    ];

    /// <summary>Looks up a protocol descriptor by its <c>ServiceProtocols</c> id, or <c>null</c> when unknown.</summary>
    public static ProtocolDescriptor? Find(string protocolId) =>
        protocolId is not null && ById.TryGetValue(protocolId, out var descriptor) ? descriptor : null;

    // Display/protocol service-type aliases honua-server's Operate transition sources actually populate
    // (HonuaServerOperateTransitionDataSource joins enabled protocol names like "FeatureServer, MapServer"
    // or falls back to "Geo service"; the in-memory source uses display names like "Feature service"),
    // mapped onto their ServiceProtocols ids. Lets the tree builder resolve already-published resources
    // whose ServiceType is a display/protocol value rather than a metadata-v2 service-type string.
    private static readonly IReadOnlyDictionary<string, string> ServiceTypeAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // metadata-v2 service-type strings (the catalog's own ServiceType values).
            ["esri-feature-service"] = Protocols.FeatureServer,
            ["esri-map-service"] = Protocols.MapServer,
            ["esri-image-service"] = Protocols.ImageServer,
            ["ogc-api-features"] = Protocols.OgcFeatures,
            ["wms"] = Protocols.Wms,
            ["wfs"] = Protocols.Wfs20,
            ["wmts"] = Protocols.Wmts,
            ["odata"] = Protocols.OData,
            ["stac-api"] = Protocols.Stac,
            // ServiceProtocols ids as they arrive joined from honua-server runtime protocols.
            ["FeatureServer"] = Protocols.FeatureServer,
            ["MapServer"] = Protocols.MapServer,
            ["ImageServer"] = Protocols.ImageServer,
            ["OgcFeatures"] = Protocols.OgcFeatures,
            ["Wfs20"] = Protocols.Wfs20,
            ["Stac"] = Protocols.Stac,
            // Display names the in-memory/demo source and human-facing projections use.
            ["Feature service"] = Protocols.FeatureServer,
            ["Map service"] = Protocols.MapServer,
            ["Image service"] = Protocols.ImageServer,
        };

    /// <summary>
    /// Resolves a service's <c>ServiceType</c> value — a metadata-v2 service-type string
    /// (<c>esri-feature-service</c>), a single <c>ServiceProtocols</c> id, a comma-joined list of protocol
    /// names (<c>FeatureServer, MapServer</c>), or a display name (<c>Feature service</c>) — into the protocol
    /// descriptors it exposes, in catalog order with duplicates removed. Unknown tokens are skipped (never
    /// fabricated). This is what lets the resource→publications tree show already-published resources as
    /// Running rather than Draft regardless of which source populated the service type.
    /// </summary>
    public static IReadOnlyList<ProtocolDescriptor> ResolveServiceTypeProtocols(string? serviceType)
    {
        if (string.IsNullOrWhiteSpace(serviceType))
        {
            return [];
        }

        var resolved = new List<ProtocolDescriptor>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in serviceType.Split([',', ';', '+', '·', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // Direct catalog ServiceType match (e.g. an exact metadata-v2 string), then alias fallback.
            var descriptor = All.FirstOrDefault(d => string.Equals(d.ServiceType, token, StringComparison.OrdinalIgnoreCase));
            if (descriptor is null && ServiceTypeAliases.TryGetValue(token, out var protocolId))
            {
                descriptor = Find(protocolId);
            }

            if (descriptor is not null && seen.Add(descriptor.Id))
            {
                resolved.Add(descriptor);
            }
        }

        var order = All.Select((d, index) => (d.Id, index)).ToDictionary(pair => pair.Id, pair => pair.index, StringComparer.Ordinal);
        return resolved.OrderBy(d => order[d.Id]).ToArray();
    }

    /// <summary>The descriptors in a group, in catalog order.</summary>
    public static IReadOnlyList<ProtocolDescriptor> InGroup(string group) =>
        All.Where(descriptor => string.Equals(descriptor.Group, group, StringComparison.Ordinal)).ToArray();

    /// <summary>
    /// Maps an enabled protocol set into the metadata-v2 Publications they imply for a service slot:
    /// one <see cref="PublishProtocolPlan"/> per protocol, carrying its service type and the served route
    /// the Publication will expose (redesign §5.4b "Will create N publications" + §5.6 per-protocol routes).
    /// Unknown protocol ids are skipped (never fabricated). The order follows <see cref="All"/> so the
    /// preview is stable regardless of toggle order.
    /// </summary>
    public static IReadOnlyList<PublishProtocolPlan> PlanPublications(
        string serviceSlot,
        IEnumerable<string> enabledProtocols)
    {
        ArgumentNullException.ThrowIfNull(enabledProtocols);
        var enabled = enabledProtocols.ToHashSet(StringComparer.Ordinal);
        var slot = string.IsNullOrWhiteSpace(serviceSlot) ? "service" : serviceSlot.Trim().Trim('/');

        return All
            .Where(descriptor => enabled.Contains(descriptor.Id))
            .Select(descriptor => new PublishProtocolPlan(
                descriptor.Id,
                descriptor.ServiceType,
                descriptor.Label,
                RouteFor(descriptor.Id, slot)))
            .ToArray();
    }

    /// <summary>The served endpoint a protocol Publication exposes for a service slot (redesign §5.3/§5.6 routes).</summary>
    public static string RouteFor(string protocolId, string serviceSlot)
    {
        var slot = string.IsNullOrWhiteSpace(serviceSlot) ? "service" : serviceSlot.Trim().Trim('/');
        var leaf = slot.Contains('/') ? slot[(slot.LastIndexOf('/') + 1)..] : slot;
        return protocolId switch
        {
            Protocols.FeatureServer => $"/{slot}/FeatureServer/0",
            Protocols.MapServer => $"/{slot}/MapServer",
            Protocols.ImageServer => $"/{slot}/ImageServer",
            Protocols.Stac => $"/stac/collections/{leaf}",
            Protocols.OgcFeatures => $"/ogc/{slot}/collections/{leaf}",
            Protocols.Wms => $"/ogc/{slot}/wms",
            Protocols.Wfs20 => $"/ogc/{slot}/wfs",
            Protocols.Wmts => $"/ogc/{slot}/wmts",
            Protocols.OData => $"/odata/{slot}",
            _ => $"/{slot}",
        };
    }
}

/// <summary>
/// One planned metadata-v2 Publication: the protocol being exposed, the service type its binding lands
/// under, a display label, and the served route. The publish step creates one server publication per plan.
/// </summary>
public sealed record PublishProtocolPlan(
    string ProtocolId,
    string ServiceType,
    string Label,
    string Route);
