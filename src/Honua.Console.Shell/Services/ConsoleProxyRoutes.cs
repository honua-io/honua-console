using System.Globalization;

namespace Honua.Console.Shell.Services;

// Each URL is pinned to the environment that produced it, is null when none is active, and fails closed with 409 rather than following another tab's later environment.
public static class ConsoleProxyRoutes
{
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

    public static string? MapStyle(string? environmentId, string? layerId) =>
        string.IsNullOrWhiteSpace(environmentId) || string.IsNullOrWhiteSpace(layerId)
            ? null
            : $"{MapPrefix(environmentId)}styles/{Uri.EscapeDataString(layerId)}.json";

    public static string? MapStyle(string? environmentId, int layerId) =>
        MapStyle(environmentId, layerId.ToString(CultureInfo.InvariantCulture));

    // Callers append {layerId}/{z}/{x}/{y}.mvt to this prefix.
    public static string MapTileBase(string environmentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);
        return $"{MapPrefix(environmentId)}tiles/";
    }

    public static string? MapFeatures(string? environmentId, string? serviceId, int layerId) =>
        string.IsNullOrWhiteSpace(environmentId) || string.IsNullOrWhiteSpace(serviceId)
            ? null
            : $"{MapPrefix(environmentId)}features/{Uri.EscapeDataString(serviceId)}/{layerId.ToString(CultureInfo.InvariantCulture)}";

    private static string MapPrefix(string environmentId) =>
        $"/map-proxy/{Uri.EscapeDataString(environmentId)}/";
}
