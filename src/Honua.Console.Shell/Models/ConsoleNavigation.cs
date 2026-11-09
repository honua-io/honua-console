using Honua.Console.Shell.Services;
using Microsoft.Extensions.Configuration;

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
    Folder,
    Service,
    Layer
}

public sealed record ConsoleNavigationContext(string Path, string Label, ConsoleNavigationContextKind Kind);

public static class ConsoleNavigation
{
    public static ConsoleNavigationDestination Data { get; } = new("/operate/data", "Datasets");
    public static ConsoleNavigationDestination AddData { get; } = new("/operate/data/new", "Add data");
    public static ConsoleNavigationDestination Health { get; } = new("/operate/health", "Health");
    public static ConsoleNavigationDestination Studio { get; } = new("/studio", "Maps");

    public static IReadOnlyList<ConsoleNavigationGroup> Groups { get; } =
    [
        new("Data", [Data, new("/operate/services", "Services"), new("/operate/layers", "Layers"),
            new("/operate/connections", "Connections"), AddData,
            new("/catalog", "Catalog") { RequiredArea = "catalog" }, new("/share/public", "Share") { RequiredArea = "share" }]),
        new("Protocols", [new("/operate/catalogs", "Discovery catalogs") { FullModeOnly = true }]),
        new("Studio", [Studio, new("/catalog", "Map from catalog") { FullModeOnly = true, RequiredArea = "catalog" }, new("/studio/processes", "Processes"),
            new("/studio/form", "Forms") { FullModeOnly = true }, new("/studio/report", "Reports") { FullModeOnly = true }]),
        new("Operate", [new("/operate", "Dashboard"), new("/inbox", "Approvals"), Health,
            new("/operate/copilot", "Findings") { FullModeOnly = true }, new("/operate/geoprocessing", "Geoprocessing"),
            new("/operate/observability", "Observability"), new("/operate/metrics", "Metrics"),
            new("/operate/deploy", "Deploy"), new("/operate/releases", "Releases")]),
        new("Access", [new("/operate/access/members", "Users") { FullModeOnly = true },
            new("/operate/access", "Roles") { FullModeOnly = true }]),
        new("Settings", [new("/operate/settings", "Server settings") { FullModeOnly = true }])
    ];

    private static IReadOnlyList<ConsoleNavigationGroup> CatalogAdminGroups { get; } = CreateCatalogAdministrationGroups();

    public static IReadOnlyList<ConsoleNavigationGroup> VisibleGroups(
        IConsoleProductMode mode, IConsoleCapabilityManifest capabilities, IEnumerable<ConsoleNavigationGroup> groups)
    {
        return groups.Select(group => group with
        {
            Destinations = group.Destinations
                .Where(entry => IsVisible(entry, mode, capabilities)).ToArray()
        })
            .Where(group => group.Destinations.Count > 0).ToArray();
    }

    public static IReadOnlyList<ConsoleNavigationGroup> Definitions(CatalogAdminOptions options) =>
        options.Enabled ? CatalogAdminGroups : Groups;

    public static IEnumerable<ConsoleNavigationGroup> EnabledGroups(CatalogAdminOptions options, IConfiguration? configuration)
    {
        var studioEnabled = bool.TryParse(configuration?["Studio:EmbedJsStudio"], out var enabled) && enabled;
        return Definitions(options).Where(group => group.Name != "Studio" || studioEnabled);
    }

    private static IReadOnlyList<ConsoleNavigationGroup> CreateCatalogAdministrationGroups()
    {
        var discovery = Groups.SelectMany(group => group.Destinations).Single(entry => entry.Path == "/operate/catalogs");
        var groups = new List<ConsoleNavigationGroup>();
        foreach (var group in Groups)
        {
            if (group.Name == "Operate")
                groups.Add(new("Catalogs", [new("/catalogs", "Catalogs"), new("/metadata/fields", "Metadata fields"),
                    discovery with { Label = "Discovery endpoints" },
                    new("/share/manage", "Open Data access") { FullModeOnly = true, RequiredArea = "share" }]));
            var destinations = group.Destinations.Where(entry => entry.Path != discovery.Path)
                .Select(entry => entry.Path == "/catalog"
                    ? entry with { Label = group.Name == "Studio" ? "Map from Content" : "Content" }
                    : entry).ToArray();
            if (destinations.Length > 0) groups.Add(group with { Destinations = destinations });
        }
        return groups;
    }

    public static ConsoleNavigationContext ContextForPath(string relativePath, IReadOnlyList<ConsoleNavigationGroup> groups)
    {
        var path = "/" + relativePath.Split('?', '#')[0].Trim('/');
        var segments = path.Trim('/').Split('/');
        if (segments is ["operate", "folders", var folder])
            return new(path, Uri.UnescapeDataString(folder), ConsoleNavigationContextKind.Folder);
        if (segments is ["operate", "services", var service, "settings"])
            return new(path, Uri.UnescapeDataString(service), ConsoleNavigationContextKind.Service);
        if (segments.Length >= 3 && segments[0] == "operate" && segments[1] == "layers")
            return new($"/operate/layers/{segments[2]}", Uri.UnescapeDataString(segments[2]), ConsoleNavigationContextKind.Layer);
        if (path == "/") return new(path, "Welcome", ConsoleNavigationContextKind.Page);
        if (path == "/approvals") return new(path, "Approvals", ConsoleNavigationContextKind.Page);
        var destinations = groups.SelectMany(group => group.Destinations);
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
