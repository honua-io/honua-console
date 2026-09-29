namespace Honua.Console.Shell.Models;

public static class ConsoleRouteMap
{
    public static IReadOnlyList<ConsoleWorkflowArea> Areas { get; } =
    [
        new("studio", "Studio", "/studio", "Builder", "AI-assisted map, dashboard, report, and app creation."),
        new("catalog", "Catalog", "/catalog", "Builder", "Data, services, maps, dashboards, apps, metadata, and provenance."),
        new("operate", "Operate", "/operate", "Operator", "Publishing, jobs, service configuration, identity, observability, and runtime administration."),
        new("share", "Share", "/share/public", "Builder", "Public links, embeds, open-data pages, exports, and external publishing flows.")
    ];

    public static IReadOnlyList<string> PortalParityRoutes { get; } =
    [
        "/auth/signin",
        "/catalog",
        "/catalog/{idOrSlug}",
        "/maps/{mapId}",
        "/maps/new",
        "/share/public",
        "/share/public/items/{idOrSlug}",
        "/public/items/{idOrSlug}",
        "/embed/maps/{mapId}"
    ];

    public static ConsoleWorkflowArea? FindArea(string areaId) =>
        Areas.FirstOrDefault(area => string.Equals(area.Id, areaId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Resolves the workflow area a relative route sits in, or <see langword="null"/> for the
    /// shell-owned routes outside the four areas (inbox, environments, support, auth, embed).
    /// </summary>
    public static ConsoleWorkflowArea? FindAreaForPath(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        var path = NormalizePath(relativePath);
        var separatorIndex = path.IndexOf('/');

        return FindArea(separatorIndex >= 0 ? path[..separatorIndex] : path);
    }

    public static bool IsOperateRoute(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        var path = NormalizePath(relativePath);

        return string.Equals(path, "operate", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("operate/", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string relativePath)
    {
        var queryOrFragmentIndex = relativePath.IndexOfAny(['?', '#']);
        var path = queryOrFragmentIndex >= 0 ? relativePath[..queryOrFragmentIndex] : relativePath;

        return path.Trim('/');
    }
}

public sealed record ConsoleWorkflowArea(
    string Id,
    string Name,
    string Path,
    string WorkflowBoundary,
    string Description);
