namespace Honua.Console.Shell.Services;

/// <summary>Deployment presentation only. Server authentication and authorization do not depend on this mode.</summary>
public sealed record ConsolePresentation
{
    public string Mode { get; }
    public bool IsWitness => Mode == "witness";

    public ConsolePresentation(string? mode = null)
    {
        Mode = string.IsNullOrWhiteSpace(mode) ? "full" : mode.Trim().ToLowerInvariant();
        if (Mode is not ("full" or "witness"))
        {
            throw new ArgumentException("Honua:Console:Mode must be full or witness.", nameof(mode));
        }
    }

    public static bool IsFocusedRoute(string path)
    {
        path = "/" + path.Split('?', '#')[0].Trim('/');
        if (path is "/" or "/operate" or "/catalog" or "/inbox" or "/support" or "/environments")
        {
            return true;
        }

        string[] routes = ["/catalog/", "/inbox/", "/support/", "/environments/",
            "/operate/connections", "/operate/services", "/operate/layers", "/operate/data",
            "/operate/geoprocessing", "/operate/publishing", "/operate/deploy", "/operate/health",
            "/operate/observability", "/operate/events", "/operate/metrics"];
        return routes.Any(route => route.EndsWith('/')
            ? path.StartsWith(route, StringComparison.OrdinalIgnoreCase)
            : path.Equals(route, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(route + "/", StringComparison.OrdinalIgnoreCase));
    }
}
