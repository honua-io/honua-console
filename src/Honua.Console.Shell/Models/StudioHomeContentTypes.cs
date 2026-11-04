namespace Honua.Console.Shell.Models;

public sealed record StudioHomeContentType(
    string Key,
    string Glyph,
    string Title,
    string Description,
    string Route);

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

    public static IReadOnlyList<string> PromptSuggestions { get; } =
    [
        "Map of parcels coloured by area",
        "Dashboard for land use trends",
        "Form for hydrant inspection",
        "Report comparing wetlands FY23 vs FY24",
        "Heatmap of fire observations"
    ];
}
