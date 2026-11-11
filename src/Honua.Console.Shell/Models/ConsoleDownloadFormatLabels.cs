namespace Honua.Console.Shell.Models;

public static class ConsoleDownloadFormatLabels
{
    public static string Display(string format) => format switch
    {
        "GeoServices:FeatureService" => "GeoServices feature service",
        "GeoServices:MapServer" => "GeoServices map service",
        "OGC:API:Features" => "OGC API Features",
        _ => format
    };
}
