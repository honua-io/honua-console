using System.Globalization;

namespace Honua.Console.Shell.Models;

public sealed record OperateLayerExposure(OperateServiceDetail Service, OperateServiceLayerProjection Layer);

public static class OperateLayerLookup
{
    // An admin layer id is unique only inside one connection, so several matches resolve to no layer.
    public static IReadOnlyList<OperateLayerExposure> FindExposures(this OperateServicesView view, string? layerReference)
    {
        var exposures = AllExposures(view);
        var canonicalIds = MatchingCanonicalResourceIds(exposures, layerReference);
        if (canonicalIds.Count != 1)
        {
            return [];
        }

        return exposures
            .Where(exposure => IsCanonicalResource(exposure, canonicalIds[0]))
            .OrderBy(exposure => exposure.Service.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static OperateServiceLayerProjection? FindLayer(this OperateServicesView view, string? layerReference) =>
        view.FindExposures(layerReference).FirstOrDefault()?.Layer;

    public static IReadOnlyDictionary<string, string> GeometryByCanonicalResourceId(this OperateServicesView view) =>
        AllExposures(view)
            .Where(exposure => !string.IsNullOrWhiteSpace(exposure.Layer.CanonicalResourceId))
            .OrderBy(exposure => exposure.Service.Name, StringComparer.OrdinalIgnoreCase)
            .DistinctBy(exposure => exposure.Layer.CanonicalResourceId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(exposure => exposure.Layer.CanonicalResourceId, exposure => exposure.Layer.Geometry, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> MatchingCanonicalResourceIds(this OperateServicesView view, string? layerReference) =>
        MatchingCanonicalResourceIds(AllExposures(view), layerReference);

    private static IReadOnlyList<string> MatchingCanonicalResourceIds(
        IReadOnlyList<OperateLayerExposure> exposures,
        string? layerReference)
    {
        if (string.IsNullOrWhiteSpace(layerReference))
        {
            return [];
        }

        var canonicalMatch = exposures.FirstOrDefault(exposure => IsCanonicalResource(exposure, layerReference));
        if (canonicalMatch is not null)
        {
            return [canonicalMatch.Layer.CanonicalResourceId];
        }

        if (!int.TryParse(layerReference, NumberStyles.None, CultureInfo.InvariantCulture, out var adminLayerId))
        {
            return [];
        }

        return exposures
            .Where(exposure => exposure.Layer.LayerId == adminLayerId)
            .Select(exposure => exposure.Layer.CanonicalResourceId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static OperateLayerExposure[] AllExposures(OperateServicesView view) =>
        view.Services
            .SelectMany(service => service.Layers.Select(layer => new OperateLayerExposure(service, layer)))
            .ToArray();

    private static bool IsCanonicalResource(OperateLayerExposure exposure, string canonicalResourceId) =>
        string.Equals(exposure.Layer.CanonicalResourceId, canonicalResourceId, StringComparison.OrdinalIgnoreCase);
}
