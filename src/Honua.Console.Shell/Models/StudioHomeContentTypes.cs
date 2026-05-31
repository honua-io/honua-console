namespace Honua.Console.Shell.Models;

/// <summary>
/// A single "Start with a content type" card on the Studio home landing. Each card links to the existing
/// builder route so the home grid stays in lockstep with the per-family editors. Mirrors the StudioHome
/// content-type grid in <c>docs/design-handoff/console-canvas/screens-studio.jsx</c>.
/// </summary>
public sealed record StudioHomeContentType(
    string Key,
    string Glyph,
    string Title,
    string Description,
    string Route);

/// <summary>
/// The eight content types offered on the Studio home landing, in mockup order, each bound to its existing
/// builder route. Defined once so the page and its render tests assert against the same source of truth.
/// </summary>
public static class StudioHomeContentTypes
{
    public static IReadOnlyList<StudioHomeContentType> All { get; } =
    [
        new("map", "◐", "Map", "Layers, style, popups, interactions", "/studio/map"),
        new("dashboard", "▤", "Dashboard", "Charts, tables, filters, narrative", "/studio/dashboard"),
        new("report", "⊟", "Report", "Long-form pages with maps + charts", "/studio/report"),
        new("form", "☱", "Form", "Survey-style field data capture", "/studio/form"),
        new("app", "❒", "App", "Multi-page workflow surface", "/studio/app"),
        new("query", "◇", "Query", "SQL/filter/spatial predicates", "/studio/query"),
        new("analysis", "∑", "Analysis", "Spatial/statistical analysis jobs", "/studio/analysis"),
        new("workflow", "⇋", "Workflow", "ETL pipelines, GP services", "/studio/workflows/new")
    ];

    /// <summary>
    /// Suggestion chips rendered under the hero prompt. Each chip seeds the inline-authoring prompt.
    /// </summary>
    public static IReadOnlyList<string> PromptSuggestions { get; } =
    [
        "Map of parcels coloured by area",
        "Dashboard for land use trends",
        "Form for hydrant inspection",
        "Report comparing wetlands FY23 vs FY24",
        "Heatmap of fire observations"
    ];
}
