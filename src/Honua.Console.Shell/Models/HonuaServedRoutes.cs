namespace Honua.Console.Shell.Models;

// Only the Esri GeoServices server types and the classic OGC services are routed per service; the other
// HTTP protocols have one server root and expose the service as a collection or type name inside it.
// Terrain and elevation are addressed per dataset and gRPC per RPC method, so they have no service URL.
// Checked against the honua-server endpoint registry.
public static class HonuaServedRoutes
{
    public static string? PathFor(string serviceName, string protocol)
    {
        var service = Uri.EscapeDataString(serviceName);

        return protocol switch
        {
            "FeatureServer" => $"/rest/services/{service}/FeatureServer",
            "MapServer" => $"/rest/services/{service}/MapServer",
            "ImageServer" => $"/rest/services/{service}/ImageServer",
            "GPServer" => $"/rest/services/{service}/GPServer",
            "Wms" => $"/ogc/services/{service}/wms",
            "Wmts" => $"/ogc/services/{service}/wmts",
            "Wcs" => $"/ogc/services/{service}/wcs",
            "Wfs20" => "/wfs",
            "OgcFeatures" => "/ogc/features",
            "OgcApiTiles" => "/ogc/tiles",
            "OgcApiMaps" => "/ogc/maps",
            "OgcApiCoverages" => "/ogc/coverages",
            "OData" => "/odata",
            "Stac" => "/stac",
            _ => null,
        };
    }

    public static string CatalogPathFor(string serviceName, string protocol) =>
        PathFor(serviceName, protocol)
        ?? throw new InvalidOperationException($"Catalog protocol '{protocol}' has no served route.");
}
