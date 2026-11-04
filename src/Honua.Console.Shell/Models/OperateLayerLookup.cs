using System.Globalization;

namespace Honua.Console.Shell.Models;

public sealed record OperateLayerExposure(OperateServiceDetail Service, OperateServiceLayerProjection Layer);

/// <summary>
/// Resolves the layer reference in an <c>/operate/layers/{reference}</c> route. The reference is either the
/// canonical resource id (<c>{connection}-layer-{layerId}</c>) or the admin layer id that publishing returns.
/// Admin layer ids are only unique within a connection, so an admin id that several connections share
/// resolves to no layer and is reported through <see cref="MatchingCanonicalResourceIds"/>.
/// </summary>
public static class OperateLayerLookup
{
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
