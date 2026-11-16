using System.Globalization;

namespace Honua.Console.Shell.Models;

public sealed record OperateLayerExposure(OperateServiceDetail Service, OperateServiceLayer Layer);

public static class OperateLayerLookup
{
    // An admin layer id is unique only inside one connection, so several matches resolve to no layer.
    public static IReadOnlyList<OperateLayerExposure> FindExposures(this OperateServicesView view, string? layerReference)
    {
        var exposures = AllExposures(view);
        var canonicalIds = MatchingDatasetIds(exposures, layerReference);
        if (canonicalIds.Count != 1)
        {
            return [];
        }

        return exposures
            .Where(exposure => IsDataset(exposure, canonicalIds[0]))
            .OrderBy(exposure => exposure.Service.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static OperateServiceLayer? FindLayer(this OperateServicesView view, string? layerReference) =>
        view.FindExposures(layerReference).FirstOrDefault()?.Layer;

    public static IReadOnlyDictionary<string, string> GeometryByDatasetId(this OperateServicesView view) =>
        AllExposures(view)
            .Where(exposure => !string.IsNullOrWhiteSpace(exposure.Layer.DatasetId))
            .OrderBy(exposure => exposure.Service.Name, StringComparer.OrdinalIgnoreCase)
            .DistinctBy(exposure => exposure.Layer.DatasetId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(exposure => exposure.Layer.DatasetId, exposure => exposure.Layer.Geometry, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> MatchingDatasetIds(this OperateServicesView view, string? layerReference) =>
        MatchingDatasetIds(AllExposures(view), layerReference);

    private static IReadOnlyList<string> MatchingDatasetIds(
        IReadOnlyList<OperateLayerExposure> exposures,
        string? layerReference)
    {
        if (string.IsNullOrWhiteSpace(layerReference))
        {
            return [];
        }

        var canonicalMatch = exposures.FirstOrDefault(exposure => IsDataset(exposure, layerReference));
        if (canonicalMatch is not null)
        {
            return [canonicalMatch.Layer.DatasetId];
        }

        if (!int.TryParse(layerReference, NumberStyles.None, CultureInfo.InvariantCulture, out var adminLayerId))
        {
            return [];
        }

        return exposures
            .Where(exposure => exposure.Layer.LayerId == adminLayerId)
            .Select(exposure => exposure.Layer.DatasetId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static OperateLayerExposure[] AllExposures(OperateServicesView view) =>
        view.Services
            .SelectMany(service => service.Layers.Select(layer => new OperateLayerExposure(service, layer)))
            .ToArray();

    private static bool IsDataset(OperateLayerExposure exposure, string datasetId) =>
        string.Equals(exposure.Layer.DatasetId, datasetId, StringComparison.OrdinalIgnoreCase);
}
