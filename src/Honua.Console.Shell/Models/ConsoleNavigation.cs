using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Models;

public sealed record ConsoleNavigationDestination(string Path, string Label)
{
    public bool FullModeOnly { get; init; }
    public string? RequiredArea { get; init; }
}

public sealed record ConsoleNavigationGroup(string Name, IReadOnlyList<ConsoleNavigationDestination> Destinations);

public enum ConsoleNavigationContextKind
{
    Page,
    Service,
    Layer
}

public sealed record ConsoleNavigationContext(string Path, string Label, ConsoleNavigationContextKind Kind);

public static class ConsoleNavigation
{
    public static ConsoleNavigationDestination Data { get; } = new("/operate/data", "Data & Layers");
    public static ConsoleNavigationDestination AddData { get; } = new("/operate/data/new", "Add data");
    public static ConsoleNavigationDestination Health { get; } = new("/operate/health", "Health");
    public static ConsoleNavigationDestination Studio { get; } = new("/studio", "Maps");

    public static IReadOnlyList<ConsoleNavigationGroup> Groups { get; } =
    [
        new("Data", [Data, new("/operate/services", "Services"), new("/operate/layers", "Layers"),
            new("/operate/connections", "Connections"), AddData,
            new("/catalog", "Catalog") { RequiredArea = "catalog" }, new("/share/public", "Share") { RequiredArea = "share" }]),
        new("Protocols", [new("/operate/catalogs", "Discovery catalogs") { FullModeOnly = true }]),
        new("Studio", [Studio, new("/maps/new", "New map") { FullModeOnly = true }, new("/studio/processes", "Processes"),
            new("/studio/form", "Forms") { FullModeOnly = true }, new("/studio/report", "Reports") { FullModeOnly = true },
            new("/studio/styles", "Styles") { FullModeOnly = true }, new("/studio/automations", "Automations") { FullModeOnly = true }]),
        new("Operate", [new("/operate", "Dashboard"), new("/inbox", "Approvals"), Health,
            new("/operate/copilot", "Findings") { FullModeOnly = true }, new("/operate/geoprocessing", "Geoprocessing"),
            new("/operate/observability", "Observability"), new("/operate/metrics", "Metrics"),
            new("/operate/deploy", "Deploy"), new("/operate/releases", "Releases")]),
        new("Access", [new("/operate/access/members", "Members") { FullModeOnly = true },
            new("/operate/access", "Roles and permissions") { FullModeOnly = true }]),
        new("Settings", [new("/operate/settings", "Server settings") { FullModeOnly = true }])
    ];

    public static ConsoleNavigationContext ContextForPath(string relativePath)
    {
        var path = "/" + relativePath.Split('?', '#')[0].Trim('/');
        var segments = path.Trim('/').Split('/');
        if (segments is ["operate", "services", var service, "settings"])
            return new(path, Uri.UnescapeDataString(service), ConsoleNavigationContextKind.Service);
        if (segments.Length >= 3 && segments[0] == "operate" && segments[1] == "layers")
            return new($"/operate/layers/{segments[2]}", Uri.UnescapeDataString(segments[2]), ConsoleNavigationContextKind.Layer);
        if (path == "/") return new(path, "Welcome", ConsoleNavigationContextKind.Page);
        if (path == "/approvals") return new(path, "Approvals", ConsoleNavigationContextKind.Page);
        var destinations = Groups.SelectMany(group => group.Destinations);
        var exact = destinations.FirstOrDefault(entry => entry.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        var label = exact?.Label ?? Uri.UnescapeDataString(segments[^1]);
        return new(path, label, ConsoleNavigationContextKind.Page);
    }

    public static bool IsVisible(ConsoleNavigationDestination destination, IConsoleProductMode mode, IConsoleCapabilityManifest capabilities)
    {
        if (mode.IsWitness && destination.FullModeOnly) return false;
        return destination.RequiredArea is not { } areaId ||
            (mode.ShowsArea(areaId) && ConsoleRouteMap.FindArea(areaId)?.IsAvailable(capabilities) == true);
    }
}
