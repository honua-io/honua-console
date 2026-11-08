namespace Honua.Console.Shell.Models;

// Only the GeoServices REST server types and the classic OGC services are routed per service; the other
// HTTP protocols have one server root and expose the service as a collection or type name inside it.
// Terrain and elevation are addressed per dataset and gRPC per RPC method, so they have no service URL.
// Checked against the honua-server endpoint registry.
public static class HonuaServedRoutes
{
    public static string? PathFor(string? serviceName, string protocol, string? folderName = null)
    {
        var service = serviceName is null ? null : (string.IsNullOrEmpty(folderName) ? "" : Uri.EscapeDataString(folderName) + "/") + Uri.EscapeDataString(serviceName);

        return protocol switch
        {
            "FeatureServer" when service is not null => $"/rest/services/{service}/FeatureServer",
            "MapServer" when service is not null => $"/rest/services/{service}/MapServer",
            "ImageServer" when service is not null => $"/rest/services/{service}/ImageServer",
            "GPServer" when service is not null => $"/rest/services/{service}/GPServer",
            "Wms" when service is not null => $"/ogc/services/{service}/wms",
            "Wmts" when service is not null => $"/ogc/services/{service}/wmts",
            "Wcs" when service is not null => $"/ogc/services/{service}/wcs",
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

    public static string CatalogPathFor(string? serviceName, string protocol, string? folderName = null) =>
        PathFor(serviceName, protocol, folderName)
        ?? throw new InvalidOperationException($"Catalog protocol '{protocol}' has no served route.");
}
