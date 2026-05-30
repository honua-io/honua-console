using System.Text.Json;

namespace Honua.Console.Shell.Models;

/// <summary>
/// Mutable Studio dashboard-builder editor state. This is Console-owned authoring state bound to the
/// editor UI; it maps to and from the server-owned <c>honua.dashboard-package.v1</c> document
/// (publication registry, honua-server#1183) through the <c>IStudioDashboardPackageDataSource</c>
/// shim. It is not a server wire contract and is never persisted as a standing mock data source: the
/// only persistence path is the real honua-server dashboard package lifecycle behind the
/// Honua.Console.Contracts shim (Console Patterns Charter section 11).
/// </summary>
public sealed class StudioDashboardEditorState
{
    /// <summary>Stable server package id. Null until the first draft is created server-side.</summary>
    public string? DashboardId { get; set; }

    /// <summary>Server package version currently open in the editor.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Server lifecycle status: draft, published, or archived.</summary>
    public string Status { get; set; } = StudioDashboardStatuses.Draft;

    /// <summary>Optimistic-concurrency ETag for draft updates. Empty for a not-yet-saved draft.</summary>
    public string ETag { get; set; } = string.Empty;

    /// <summary>Published version a reopened draft was created from, when applicable.</summary>
    public int? ReopenedFromVersion { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>Narrative block rendered above the layout (intro/context copy).</summary>
    public string Narrative { get; set; } = string.Empty;

    /// <summary>
    /// Data bindings: source content refs (with version pins) every panel can read. The version pin is
    /// a deliberate authoring decision so a published dashboard reads a known content version.
    /// </summary>
    public List<StudioDashboardBindingEditor> Bindings { get; } = [];

    /// <summary>Layout slots holding chart, map, table, and filter panels.</summary>
    public List<StudioDashboardPanelEditor> Panels { get; } = [];

    /// <summary>Responsive preview breakpoint currently selected in the editor (desktop or narrow).</summary>
    public string PreviewBreakpoint { get; set; } = StudioDashboardBreakpoints.Desktop;

    public bool IsExistingPackage => !string.IsNullOrWhiteSpace(DashboardId);

    public bool IsPublished =>
        string.Equals(Status, StudioDashboardStatuses.Published, StringComparison.OrdinalIgnoreCase);
}

/// <summary>A source content binding for dashboard panels, with an explicit version pin.</summary>
public sealed class StudioDashboardBindingEditor
{
    /// <summary>Author-facing alias panels reference (for example <c>requests</c>).</summary>
    public string Alias { get; set; } = string.Empty;

    /// <summary>Server content item id the alias resolves to.</summary>
    public string ContentRef { get; set; } = string.Empty;

    /// <summary>
    /// Pinned content version. Blank pins to the latest published version at publish time; an explicit
    /// value (for example <c>v7</c>) pins the dashboard to a known version.
    /// </summary>
    public string VersionPin { get; set; } = string.Empty;
}

/// <summary>A layout panel. Charts use Vega-Lite specs; map/table/filter/narrative are layout slots.</summary>
public sealed class StudioDashboardPanelEditor
{
    public string Title { get; set; } = string.Empty;

    /// <summary>One of <see cref="StudioDashboardPanelKinds"/>.</summary>
    public string Kind { get; set; } = StudioDashboardPanelKinds.Chart;

    /// <summary>Binding alias this panel reads from.</summary>
    public string BindingAlias { get; set; } = string.Empty;

    /// <summary>
    /// Vega-Lite chart spec (JSON). Required for <see cref="StudioDashboardPanelKinds.Chart"/> panels;
    /// Vega-Lite is the chart standard for all dashboard charts (issue AC).
    /// </summary>
    public string VegaLiteSpec { get; set; } = string.Empty;

    public bool IsChart =>
        string.Equals(Kind, StudioDashboardPanelKinds.Chart, StringComparison.OrdinalIgnoreCase);
}

public static class StudioDashboardStatuses
{
    public const string Draft = "draft";
    public const string Published = "published";
    public const string Archived = "archived";
}

public static class StudioDashboardPanelKinds
{
    public const string Chart = "chart";
    public const string Map = "map";
    public const string Table = "table";
    public const string Filter = "filter";

    public static IReadOnlyList<string> All { get; } = [Chart, Map, Table, Filter];
}

public static class StudioDashboardBreakpoints
{
    public const string Desktop = "desktop";
    public const string Narrow = "narrow";

    public static IReadOnlyList<string> All { get; } = [Desktop, Narrow];
}

/// <summary>Builds a default Vega-Lite bar-chart spec for a freshly added chart panel.</summary>
public static class StudioDashboardChartSpec
{
    public const string VegaLiteSchema = "https://vega.github.io/schema/vega-lite/v5.json";

    private static readonly JsonSerializerOptions SpecOptions = new() { WriteIndented = true };

    public static string DefaultBarChart(string dimension = "category", string measure = "value")
    {
        var spec = new Dictionary<string, object?>
        {
            ["$schema"] = VegaLiteSchema,
            ["mark"] = "bar",
            ["encoding"] = new Dictionary<string, object?>
            {
                ["x"] = new Dictionary<string, object?> { ["field"] = dimension, ["type"] = "nominal" },
                ["y"] = new Dictionary<string, object?> { ["field"] = measure, ["type"] = "quantitative" }
            }
        };

        return JsonSerializer.Serialize(spec, SpecOptions);
    }

    /// <summary>
    /// True when the spec is non-empty, parses as a JSON object, and declares the Vega-Lite schema URL.
    /// The issue requires every dashboard chart to declare a Vega-Lite schema before publish.
    /// </summary>
    public static bool DeclaresVegaLiteSchema(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(spec);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("$schema", out var schema)
                && schema.ValueKind == JsonValueKind.String
                && (schema.GetString()?.Contains("vega-lite", StringComparison.OrdinalIgnoreCase) ?? false);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

// --- Data-source-facing projections (immutable). ---

public sealed record StudioDashboardWorkspace(
    IReadOnlyList<StudioDashboardPackageListItem> Packages,
    IReadOnlyList<StudioDashboardCapabilityState> CapabilityStates);

public sealed record StudioDashboardPackageListItem(
    string DashboardId,
    string Title,
    int PanelCount,
    int? DraftVersion,
    int? PublishedVersion,
    DateTimeOffset UpdatedAt);

/// <summary>
/// A binding/permission/empty surface for the dashboard builder, mirroring the form builder and Operate
/// capability-state pattern so missing bindings, missing permissions, and unsupported contracts render
/// consistently.
/// </summary>
public sealed record StudioDashboardCapabilityState(
    string Surface,
    string State,
    string Contract,
    string Detail);

public sealed record StudioDashboardEditorLoad(
    StudioDashboardEditorState? State,
    IReadOnlyList<StudioDashboardCapabilityState> CapabilityStates)
{
    public bool HasEditor => State is not null;
}

/// <summary>Outcome of a mutating dashboard lifecycle command (save, validate, publish, reopen).</summary>
public sealed record StudioDashboardCommandResult(
    bool Succeeded,
    string Message,
    StudioDashboardEditorState? State = null,
    StudioDashboardCapabilityState? Issue = null);

/// <summary>
/// Pure pre-publish gate. Publish requires a titled dashboard with at least one panel, every panel bound
/// to a declared data binding, and every chart panel declaring a Vega-Lite spec (issue AC). Server-side
/// publish validation still applies; this gate keeps the Console from offering publish before those
/// authoring decisions are made.
/// </summary>
public sealed record StudioDashboardPublishReadiness(bool CanPublish, IReadOnlyList<string> UnmetRequirements);

public static class StudioDashboardPublishEvaluator
{
    public static StudioDashboardPublishReadiness Evaluate(StudioDashboardEditorState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var unmet = new List<string>();

        if (string.IsNullOrWhiteSpace(state.Title))
        {
            unmet.Add("Give the dashboard a title.");
        }

        if (state.Panels.Count == 0)
        {
            unmet.Add("Add at least one panel.");
        }

        var aliases = state.Bindings
            .Select(binding => binding.Alias)
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (state.Bindings.Any(binding =>
                string.IsNullOrWhiteSpace(binding.Alias) || string.IsNullOrWhiteSpace(binding.ContentRef)))
        {
            unmet.Add("Give every data binding an alias and a content reference.");
        }

        foreach (var panel in state.Panels)
        {
            if (string.IsNullOrWhiteSpace(panel.BindingAlias) || !aliases.Contains(panel.BindingAlias))
            {
                unmet.Add($"Bind panel \"{PanelLabel(panel)}\" to a declared data binding.");
            }

            // Issue AC: Vega-Lite is the chart spec for all dashboard charts, and every chart must declare
            // a Vega-Lite schema URL before publish.
            if (panel.IsChart && !StudioDashboardChartSpec.DeclaresVegaLiteSchema(panel.VegaLiteSpec))
            {
                unmet.Add($"Chart panel \"{PanelLabel(panel)}\" must declare a Vega-Lite spec.");
            }
        }

        return new StudioDashboardPublishReadiness(unmet.Count == 0, unmet);
    }

    private static string PanelLabel(StudioDashboardPanelEditor panel) =>
        string.IsNullOrWhiteSpace(panel.Title) ? $"untitled {panel.Kind}" : panel.Title;
}
