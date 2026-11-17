using System.Globalization;
using System.Text.RegularExpressions;
using Honua.Console.Contracts;

namespace Honua.Console.Shell.Models;

// Honua vocabulary for Exports: a Dataset has an Export with immutable Versions of Artifacts.
public static partial class ExportPresentation
{
    public const string PublicFollowsLayer =
        "Public files follow the Layer: when anyone can read every row and field, the files go on the public data path; otherwise they stay on signed-in routes.";

    public static bool IsUnpublished(ExportResponse export) =>
        string.Equals(export.Status, "unpublished", StringComparison.OrdinalIgnoreCase);

    public static string StatusLabel(string status) => status.ToLowerInvariant() switch
    {
        "running" => "Exporting",
        "ready" => "Ready",
        "failed" => "Failed",
        "unpublished" => "Unpublished",
        _ => string.IsNullOrWhiteSpace(status) ? "Unknown" : status
    };

    public static string StatusClass(string status) => status.ToLowerInvariant() switch
    {
        "failed" => "console-status console-status-danger",
        "running" or "unpublished" => "console-status console-status-warning",
        _ => "console-status"
    };

    public static string AccessLabel(ExportResponse export) =>
        string.IsNullOrWhiteSpace(export.PublicUrl) ? "Signed-in" : "Public";

    public static string? PreferredUrl(ExportResponse export) =>
        string.IsNullOrWhiteSpace(export.PublicUrl) ? export.CollectionUrl : export.PublicUrl;

    public static string SourceLabel(ExportSourceDescriptor source) => source.Kind.ToLowerInvariant() switch
    {
        "layer" => source.Name ?? $"Layer {source.LayerId?.ToString(CultureInfo.InvariantCulture)}",
        "upload" => source.Name ?? "Uploaded file",
        "object" => source.Name ?? "Bucket object",
        _ => source.Name ?? "Unknown source"
    };

    public static string ArtifactLabel(string kind) => kind.ToLowerInvariant() switch
    {
        "geoparquet" => "GeoParquet",
        "pmtiles" => "PMTiles",
        "cog" => "Cloud-optimized GeoTIFF",
        "geotiff" => "GeoTIFF",
        "collection" => "Collection document",
        _ => string.IsNullOrWhiteSpace(kind) ? "Other" : kind
    };

    public static string Size(long? bytes)
    {
        if (bytes is not { } value || value < 0)
        {
            return "—";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = value;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{value} B")
            : string.Create(CultureInfo.InvariantCulture, $"{size:0.#} {units[unit]}");
    }

    public static string When(DateTimeOffset? value) =>
        value?.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) ?? "—";

    public static string NewExportRoute(string serviceId, int layerId, string? layerName) =>
        $"/operate/exports/new?serviceId={Uri.EscapeDataString(serviceId)}&layerId={layerId.ToString(CultureInfo.InvariantCulture)}" +
        (string.IsNullOrWhiteSpace(layerName) ? string.Empty : $"&name={Uri.EscapeDataString(layerName)}");

    // Same rule the server applies to Export ids: 3-64 lowercase letters, digits or hyphens.
    public static bool IsValidExportId(string? value) => value is not null && ExportIdPattern().IsMatch(value);

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{2,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ExportIdPattern();
}
