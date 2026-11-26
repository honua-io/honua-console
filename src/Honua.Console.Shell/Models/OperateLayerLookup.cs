using System.Globalization;

using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Models;

public sealed record OperateLayerExposure(OperateServiceDetail Service, OperateServiceLayer Layer);

public static class OperateLayerLookup
{
    public const string UnavailableState = "Unavailable";
    public const string ThrottledState = "Throttled";
    public const string MissingPermissionState = "Missing permission";
    public const string TemporaryFailureMessage =
        "honua-server did not answer in time or could not be reached. This is usually temporary; retry in a moment.";

    // A layer that is absent from a projection whose read failed may still exist, so callers report the failure
    // instead of "not found". A retryable failure wins over a permission failure.
    public static OperateCapabilityState? ReadFailure(this OperateServicesView view) =>
        view.CapabilityStates.ReadFailure();

    // The same rule for any item read: services, connections and datasets as well as layers.
    public static OperateCapabilityState? ReadFailure(this IReadOnlyList<OperateCapabilityState> states) =>
        states.FirstOrDefault(state => state.State is UnavailableState or ThrottledState)
        ?? states.FirstOrDefault(state => state.State == MissingPermissionState);

    // A transport failure (no HTTP status) carries a raw client error, so it is reworded; an HTTP failure or an
    // environment change keeps its own detail.
    public static string ReadFailureMessage(this OperateCapabilityState failure) =>
        failure.StatusCode is null && failure.Contract != ConsoleEnvironmentRead.Contract
            ? TemporaryFailureMessage
            : failure.Detail;

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

    public static OperateServiceLayer? FindLayer(this OperateServicesView view, string? layerReference) =>
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
