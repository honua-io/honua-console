using Honua.Console.Contracts;

namespace Honua.Console.Shell.Models;

public static class ConsoleFileImportLayerChoice
{
    public static HonuaAdminImportCoordinateSystem? ActiveSystem(ConsoleFileImportPreview preview, string? selected)
    {
        if (Applies(preview, selected)) return preview.CoordinateSystem;
        var layer = preview.Layers.FirstOrDefault(item => string.Equals(item.Name, selected, StringComparison.OrdinalIgnoreCase));
        if (layer?.Srid is not int srid) return null;
        return new HonuaAdminImportCoordinateSystem
        {
            DetectedSrid = srid,
            EffectiveSourceSrid = srid,
            StoredSrid = srid,
            SourceCrsDeclared = true,
            DetectionMethod = "file-metadata",
            DetectedCrs = new HonuaAdminCoordinateSystem { Code = srid, Name = $"EPSG:{srid}" }
        };
    }

    public static bool Applies(ConsoleFileImportPreview preview, string? selected)
    {
        if (preview.CoordinateSystem is null) return false;
        if (preview.Layers.Count <= 1) return true;
        return string.Equals(selected, preview.PreviewedLayer, StringComparison.OrdinalIgnoreCase);
    }

    public static string? Initial(ConsoleFileImportPreview preview) =>
        preview.Layers.Count == 0
            ? null
            : preview.PreviewedLayer is { Length: > 0 } previewed
                && preview.Layers.Any(layer => string.Equals(layer.Name, previewed, StringComparison.OrdinalIgnoreCase))
                ? preview.Layers.First(layer => string.Equals(layer.Name, previewed, StringComparison.OrdinalIgnoreCase)).Name
                : preview.Layers[0].Name;

    public static string DescribeFailure(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail)) return "The Honua server could not import the file.";
        return detail.Contains("sourceLayer", StringComparison.OrdinalIgnoreCase)
            ? "Choose one layer in this file before importing."
            : detail;
    }

    public static string OptionLabel(ConsoleFileImportLayer layer)
    {
        var parts = new List<string> { layer.Name };
        if (!string.IsNullOrWhiteSpace(layer.GeometryType)) parts.Add(layer.GeometryType);
        if (layer.Srid is int srid) parts.Add($"EPSG:{srid}");
        return string.Join(" — ", parts);
    }
}
