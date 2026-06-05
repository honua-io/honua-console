using System.Text.Json;
using System.Text.Json.Nodes;

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
    /// <summary>
    /// Stable server package id (the honua-server content-item id). Null until the first draft is saved
    /// server-side and a content version is created. Exposed as a string for the editor/list surface.
    /// </summary>
    public string? DashboardId { get; set; }

    /// <summary>Server-owned draft id for the open draft. Null until the first save creates a draft.</summary>
    public Guid? DraftId { get; set; }

    /// <summary>Server-owned content-item id. Null until the first content version is saved.</summary>
    public Guid? ItemId { get; set; }

    /// <summary>Server-owned content-version id for the most recently saved version.</summary>
    public Guid? CurrentVersionId { get; set; }

    /// <summary>Optimistic-concurrency generation token for the open server draft.</summary>
    public long Generation { get; set; }

    /// <summary>Server package version currently open in the editor.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Published version number, when the dashboard has been published.</summary>
    public int? PublishedVersion { get; set; }

    /// <summary>Server lifecycle status: draft, published, or archived.</summary>
    public string Status { get; set; } = StudioDashboardStatuses.Draft;

    /// <summary>Optimistic-concurrency ETag for draft updates. Empty for a not-yet-saved draft.</summary>
    public string ETag { get; set; } = string.Empty;

    /// <summary>Published version a reopened draft was created from, when applicable.</summary>
    public int? ReopenedFromVersion { get; set; }

    /// <summary>True once a server draft has been created (a save has happened).</summary>
    public bool IsExistingDraft => DraftId is not null;

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
    public const string Tablet = "tablet";
    public const string Mobile = "mobile";

    /// <summary>
    /// Legacy narrow breakpoint alias retained for round-trip compatibility with packages saved before the
    /// desktop/tablet/mobile responsive-preview toggle landed. New drafts pick from <see cref="All"/>.
    /// </summary>
    public const string Narrow = "narrow";

    /// <summary>
    /// The responsive-preview breakpoints offered in the editor toggle, matching the design handoff's
    /// Desktop / Tablet / Mobile responsive preview control.
    /// </summary>
    public static IReadOnlyList<string> All { get; } = [Desktop, Tablet, Mobile];
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
/// <param name="Succeeded">Whether the command succeeded.</param>
/// <param name="Message">Operator-facing status message.</param>
/// <param name="State">The post-command editor state, when the command returned one.</param>
/// <param name="Issue">A capability state when the command failed at the binding/permission/transport level.</param>
/// <param name="Diagnostics">
/// Field-addressable server validation diagnostics (the Studio package validate
/// <c>{code,severity,path,message}</c> shape) returned by a validate command. Empty unless the server
/// reported field-level findings; the dashboard builder binds these onto the offending inputs via
/// <see cref="Honua.Console.Shell.Validation.StudioDashboardServerErrorBinder"/>.
/// </param>
public sealed record StudioDashboardCommandResult(
    bool Succeeded,
    string Message,
    StudioDashboardEditorState? State = null,
    StudioDashboardCapabilityState? Issue = null,
    IReadOnlyList<Honua.Console.Contracts.StudioValidationDiagnostic>? Diagnostics = null);

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

/// <summary>
/// Pure projection between the dashboard editor state and the honua-server
/// <c>studio_dashboard_package.v1</c> envelope body. Kept free of UI and HTTP concerns so it is
/// unit-testable and reused by the server-bound data source. The server validates the envelope (a
/// non-empty schemaVersion, an object body, valid bindings, valid publication intent) — the body shape
/// below carries the dashboard's bindings, layout panels, Vega-Lite chart specs, narrative, and the
/// responsive preview breakpoint so a published dashboard is reproducible from the saved version.
/// </summary>
public static class StudioDashboardPackageMapper
{
    /// <summary>
    /// The honua-server Studio package envelope schema version. The server requires the envelope
    /// <c>schemaVersion</c> to equal the family descriptor's current schema version ("1.0").
    /// </summary>
    public const string SchemaVersion = "1.0";

    /// <summary>
    /// The dashboard package envelope format identifier. The server requires the envelope <c>format</c>
    /// to equal the dashboard family descriptor format (<c>studio_dashboard_package.v1</c>).
    /// </summary>
    public const string EnvelopeFormat = "studio_dashboard_package.v1";

    public static StudioDashboardEditorState CreateTemplate() =>
        new()
        {
            Title = string.Empty,
            Status = StudioDashboardStatuses.Draft,
            Version = 1
        };

    /// <summary>
    /// Projects the editor's data bindings into the envelope's typed binding array. The server requires a
    /// unique key, a kind, and a ref per binding; the alias is the key, the content ref is the ref, and the
    /// version pin (when set) is carried in metadata so the published dashboard reads a known version.
    /// </summary>
    public static IReadOnlyList<JsonObject> BuildEnvelopeBindings(StudioDashboardEditorState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return state.Bindings
            .Where(binding => !string.IsNullOrWhiteSpace(binding.Alias) && !string.IsNullOrWhiteSpace(binding.ContentRef))
            .Select(binding =>
            {
                var node = new JsonObject
                {
                    ["key"] = binding.Alias,
                    ["kind"] = "content",
                    ["ref"] = binding.ContentRef
                };
                if (!string.IsNullOrWhiteSpace(binding.VersionPin))
                {
                    node["metadata"] = new JsonObject { ["versionPin"] = binding.VersionPin };
                }

                return node;
            })
            .ToArray();
    }

    /// <summary>
    /// Projects the editor state into the dashboard.package envelope body. The shape mirrors the
    /// bindings/panels/narrative/preview deliverable; the server persists it as the durable content version.
    /// </summary>
    public static JsonElement BuildEnvelopeBody(StudioDashboardEditorState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var body = new JsonObject
        {
            ["schemaVersion"] = EnvelopeFormat,
            ["title"] = state.Title,
            ["description"] = state.Description,
            ["narrative"] = state.Narrative,
            ["responsivePreview"] = new JsonObject { ["breakpoint"] = state.PreviewBreakpoint },
            ["bindings"] = new JsonArray(state.Bindings.Select(binding => (JsonNode)new JsonObject
            {
                ["alias"] = binding.Alias,
                ["contentRef"] = binding.ContentRef,
                ["versionPin"] = binding.VersionPin
            }).ToArray()),
            ["panels"] = new JsonArray(state.Panels.Select(panel =>
            {
                var node = new JsonObject
                {
                    ["title"] = panel.Title,
                    ["kind"] = panel.Kind,
                    ["bindingAlias"] = panel.BindingAlias
                };
                if (panel.IsChart)
                {
                    // The Vega-Lite spec is the chart standard (issue AC). Carry it as a parsed JSON object
                    // when valid so the published body stores structured Vega-Lite, falling back to the raw
                    // string for round-tripping when it does not parse.
                    node["chartSpecFormat"] = "vega-lite";
                    node["chartSpec"] = ParseChartSpec(panel.VegaLiteSpec);
                }

                return (JsonNode)node;
            }).ToArray())
        };

        return JsonSerializer.SerializeToElement(body);
    }

    private static JsonNode? ParseChartSpec(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(spec);
        }
        catch (JsonException)
        {
            return JsonValue.Create(spec);
        }
    }
}

/// <summary>
/// Maps the server's canonical <c>honua.dashboard-document.v1</c> payload onto a fresh
/// <see cref="StudioDashboardEditorState"/> for the "Dashboard from prompt" generated turn. This is the
/// inverse of the publish-side <see cref="StudioDashboardPackageMapper"/> body projection, but it consumes
/// the AI generation document (title/description/narrative/routeSlug/breakpoint + bindings[] +
/// panels[]{id,kind,title,bindingAlias,chartSpec,field}) rather than the package envelope body. The
/// generated document becomes a fresh, unsaved draft the operator reviews and publishes — nothing is
/// fabricated here; an empty/non-object document yields an empty editor and a note (Console Patterns Charter
/// section 11).
/// </summary>
public static class StudioDashboardDocument
{
    public const string Format = "honua.dashboard-document.v1";

    /// <summary>
    /// The dashboard document panel kind the editor has no native slot for. The editor's
    /// <see cref="StudioDashboardPanelKinds"/> catalog is chart/map/table/filter; a server-proposed
    /// <c>metric</c> (single-value KPI) is mapped to the nearest existing kind (<c>table</c>) so the value
    /// + binding still surface in the editor and preview, and the mapping is reported as a note.
    /// </summary>
    public const string MetricPanelKind = "metric";

    /// <summary>The editor panel kind a server <c>metric</c> panel is downgraded to (nearest existing slot).</summary>
    public const string MetricMappedToKind = StudioDashboardPanelKinds.Table;

    /// <summary>
    /// Lifts a server dashboard document onto a fresh editor state. Returns human-readable notes for any
    /// lossy mapping (a <c>metric</c> panel downgraded to the nearest existing kind) so the page can surface
    /// them as warnings rather than dropping them silently.
    /// </summary>
    public static IReadOnlyList<string> ApplyGeneratedDocument(StudioDashboardEditorState state, JsonElement document)
    {
        ArgumentNullException.ThrowIfNull(state);

        var notes = new List<string>();
        if (document.ValueKind != JsonValueKind.Object)
        {
            return notes;
        }

        state.Title = GetString(document, "title");
        state.Description = GetString(document, "description");
        state.Narrative = GetString(document, "narrative");

        var breakpoint = GetString(document, "breakpoint");
        if (!string.IsNullOrWhiteSpace(breakpoint))
        {
            state.PreviewBreakpoint = breakpoint;
        }

        state.Bindings.Clear();
        if (document.TryGetProperty("bindings", out var bindings) && bindings.ValueKind == JsonValueKind.Array)
        {
            foreach (var binding in bindings.EnumerateArray())
            {
                state.Bindings.Add(new StudioDashboardBindingEditor
                {
                    Alias = FirstString(binding, "alias", "key"),
                    ContentRef = FirstString(binding, "contentRef", "ref"),
                    VersionPin = FirstString(binding, "versionPin")
                });
            }
        }

        state.Panels.Clear();
        if (document.TryGetProperty("panels", out var panels) && panels.ValueKind == JsonValueKind.Array)
        {
            var metricCount = 0;
            foreach (var panel in panels.EnumerateArray())
            {
                var rawKind = GetString(panel, "kind");
                var kind = NormalizeKind(rawKind, ref metricCount);

                var editor = new StudioDashboardPanelEditor
                {
                    Title = GetString(panel, "title"),
                    Kind = kind,
                    // The document carries the data column on "field" and the source on "bindingAlias"; the
                    // editor surfaces the binding alias (the data column is captured in the chart spec).
                    BindingAlias = FirstString(panel, "bindingAlias", "field")
                };

                if (string.Equals(kind, StudioDashboardPanelKinds.Chart, StringComparison.OrdinalIgnoreCase)
                    && panel.TryGetProperty("chartSpec", out var spec)
                    && spec.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
                {
                    editor.VegaLiteSpec = spec.ValueKind == JsonValueKind.String
                        ? spec.GetString() ?? string.Empty
                        : spec.GetRawText();
                }

                state.Panels.Add(editor);
            }

            if (metricCount > 0)
            {
                notes.Add(
                    $"{metricCount} metric panel{(metricCount == 1 ? string.Empty : "s")} mapped to the nearest existing "
                    + $"'{MetricMappedToKind}' kind — the editor has no metric panel kind yet.");
            }
        }

        return notes;
    }

    private static string NormalizeKind(string rawKind, ref int metricCount)
    {
        if (string.IsNullOrWhiteSpace(rawKind))
        {
            return StudioDashboardPanelKinds.Chart;
        }

        if (string.Equals(rawKind, MetricPanelKind, StringComparison.OrdinalIgnoreCase))
        {
            metricCount++;
            return MetricMappedToKind;
        }

        // Pass through any kind the editor catalog knows; anything else falls back to the table slot so the
        // panel still renders rather than being dropped.
        foreach (var known in StudioDashboardPanelKinds.All)
        {
            if (string.Equals(rawKind, known, StringComparison.OrdinalIgnoreCase))
            {
                return known;
            }
        }

        return StudioDashboardPanelKinds.Table;
    }

    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string FirstString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            var value = GetString(element, name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }
}
