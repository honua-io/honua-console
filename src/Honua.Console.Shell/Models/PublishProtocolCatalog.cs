namespace Honua.Console.Shell.Models;

public static class PublishProtocolCatalog
{
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

    public sealed record ProtocolDescriptor(
        string Id,
        string ServiceType,
        string Label,
        string Hint,
        string Group);

    public static IReadOnlyList<(string Key, string Label)> Groups { get; } =
    [
        ("geoservices", "GeoServices REST"),
        ("ogc", "OGC"),
        ("catalog", "STAC / Catalog"),
    ];

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

    public static IReadOnlyList<string> DefaultEnabled { get; } =
    [
        Protocols.FeatureServer,
        Protocols.MapServer,
        Protocols.Stac,
    ];

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

    public static IReadOnlyList<ProtocolDescriptor> InGroup(string group) =>
        All.Where(descriptor => string.Equals(descriptor.Group, group, StringComparison.Ordinal)).ToArray();

    public static IReadOnlyList<PublishProtocolPlan> PlanPublications(
        string serviceSlot,
        IEnumerable<string> enabledProtocols)
    {
        ArgumentNullException.ThrowIfNull(enabledProtocols);
        var enabled = enabledProtocols.ToHashSet(StringComparer.Ordinal);
        var serviceName = ServiceNameFor(serviceSlot);

        return All
            .Where(descriptor => enabled.Contains(descriptor.Id))
            .Select(descriptor => new PublishProtocolPlan(
                descriptor.Id,
                descriptor.ServiceType,
                descriptor.Label,
                HonuaServedRoutes.CatalogPathFor(serviceName, descriptor.Id)))
            .ToArray();
    }

    public static string ServiceNameFor(string serviceSlot) =>
        string.IsNullOrWhiteSpace(serviceSlot) ? "service" : serviceSlot.Trim().Trim('/');
}

public sealed record PublishProtocolPlan(
    string ProtocolId,
    string ServiceType,
    string Label,
    string Route);
