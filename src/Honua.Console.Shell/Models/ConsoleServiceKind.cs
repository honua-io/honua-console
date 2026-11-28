namespace Honua.Console.Shell.Models;

public enum ConsoleServiceCategory
{
    Data,
    Geoprocessing,
    Geocoding,
    Geometry
}

public static class ConsoleServiceKind
{
    private static readonly HashSet<string> LayerProtocols = new(StringComparer.OrdinalIgnoreCase)
    {
        "FeatureServer",
        "MapServer",
        "ImageServer",
        "OgcFeatures",
        "OGC-API-Features",
        "Wms",
        "Wfs",
        "Wfs20",
        "Wmts",
        "OData",
        "OGC-API-Tiles",
        "Stac",
        "VectorTiles",
        "SceneServer",
        "SensorThings"
    };

    private static readonly HashSet<string> GeoprocessingProtocols = new(StringComparer.OrdinalIgnoreCase)
    {
        "GPServer",
        "OgcProcesses",
        "OGC-API-Processes",
        "Processes"
    };

    private static readonly HashSet<string> GeocodingProtocols = new(StringComparer.OrdinalIgnoreCase)
    {
        "GeocodeServer"
    };

    private static readonly HashSet<string> GeometryProtocols = new(StringComparer.OrdinalIgnoreCase)
    {
        "GeometryServer"
    };

    public static ConsoleServiceCategory Classify(IEnumerable<string>? protocols)
    {
        var items = Normalize(protocols);
        if (items.Length == 0 || items.Any(LayerProtocols.Contains))
        {
            return ConsoleServiceCategory.Data;
        }

        if (items.Any(GeoprocessingProtocols.Contains))
        {
            return ConsoleServiceCategory.Geoprocessing;
        }

        if (items.Any(GeocodingProtocols.Contains))
        {
            return ConsoleServiceCategory.Geocoding;
        }

        return items.Any(GeometryProtocols.Contains)
            ? ConsoleServiceCategory.Geometry
            : ConsoleServiceCategory.Data;
    }

    public static bool IsLayerService(IEnumerable<string>? protocols) =>
        Classify(protocols) == ConsoleServiceCategory.Data;

    public static string TabLabel(ConsoleServiceCategory category) => category switch
    {
        ConsoleServiceCategory.Geoprocessing => "Processes / Tasks",
        ConsoleServiceCategory.Geocoding => "Locators",
        ConsoleServiceCategory.Geometry => "Operations",
        _ => "Layers"
    };

    public static string SummaryLabel(ConsoleServiceCategory category) => category switch
    {
        ConsoleServiceCategory.Geoprocessing => "Geoprocessing service",
        ConsoleServiceCategory.Geocoding => "Geocoding service",
        ConsoleServiceCategory.Geometry => "Geometry service",
        _ => "Data service"
    };

    public static string InventoryLabel(ConsoleServiceCategory category) => category switch
    {
        ConsoleServiceCategory.Geoprocessing => "Processes / Tasks",
        ConsoleServiceCategory.Geocoding => "Locators",
        ConsoleServiceCategory.Geometry => "Operations",
        _ => "Layers"
    };

    public static string TreeIconName(ConsoleServiceCategory category) => category switch
    {
        ConsoleServiceCategory.Geoprocessing => "process",
        _ => "service"
    };

    private static string[] Normalize(IEnumerable<string>? protocols) =>
        protocols is null
            ? []
            : protocols
                .SelectMany(value => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                .ToArray();
}
