using System.Globalization;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Builds the browser host's same-origin map/scene proxy URLs. Every URL carries the id of the
/// environment profile that produced it, so a view rendered for environment A keeps fetching from A
/// (or fails closed with 409 once A is no longer active) instead of silently retargeting its tiles,
/// rows or scene assets to whichever environment the operator activated later in another tab.
/// Each builder returns null when there is no active environment, so callers fall back to their
/// missing-binding state rather than emitting an unpinned proxy URL.
/// </summary>
public static class ConsoleProxyRoutes
{
    /// <summary>The active environment profile id, or null when no profile store or active profile exists.</summary>
    public static async Task<string?> ResolveActiveEnvironmentIdAsync(
        IConsoleEnvironmentProfileStore? profiles,
        CancellationToken cancellationToken = default)
    {
        if (profiles is null)
        {
            return null;
        }

        var active = await profiles.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(active?.Id) ? null : active.Id;
    }

    /// <summary><c>/map-proxy/{environmentId}/styles/{layerId}.json</c>.</summary>
    public static string? MapStyle(string? environmentId, string? layerId) =>
        string.IsNullOrWhiteSpace(environmentId) || string.IsNullOrWhiteSpace(layerId)
            ? null
            : $"{MapPrefix(environmentId)}styles/{Uri.EscapeDataString(layerId)}.json";

    /// <summary><c>/map-proxy/{environmentId}/styles/{layerId}.json</c>.</summary>
    public static string? MapStyle(string? environmentId, int layerId) =>
        MapStyle(environmentId, layerId.ToString(CultureInfo.InvariantCulture));

    /// <summary><c>/map-proxy/{environmentId}/tiles/</c>; the style proxy appends <c>{layerId}/{z}/{x}/{y}.mvt</c>.</summary>
    public static string MapTileBase(string environmentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);
        return $"{MapPrefix(environmentId)}tiles/";
    }

    /// <summary><c>/map-proxy/{environmentId}/features/{serviceId}/{layerId}</c>.</summary>
    public static string? MapFeatures(string? environmentId, string? serviceId, int layerId) =>
        string.IsNullOrWhiteSpace(environmentId) || string.IsNullOrWhiteSpace(serviceId)
            ? null
            : $"{MapPrefix(environmentId)}features/{Uri.EscapeDataString(serviceId)}/{layerId.ToString(CultureInfo.InvariantCulture)}";

    private static string MapPrefix(string environmentId) =>
        $"/map-proxy/{Uri.EscapeDataString(environmentId)}/";
}
