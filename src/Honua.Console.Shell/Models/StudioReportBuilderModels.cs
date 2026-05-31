using System.Text.Json;
using System.Text.Json.Serialization;

namespace Honua.Console.Shell.Models;

/// <summary>
/// Mutable Studio report-builder authoring state. This is Console-owned editor state bound to the report
/// builder UI: narrative, data bindings (with version pins), layout slots holding Vega-Lite charts, map
/// panels, tables, and filters, plus the responsive-preview breakpoint. It is serialized to the canonical
/// <c>honua.report-document.v1</c> payload and published through the server-owned content publication
/// registry (honua-server#1183) via <c>IStudioReportPublicationDataSource</c>; it is never a server wire
/// contract and is never persisted as a standing mock data source (Console Patterns Charter section 11).
/// </summary>
public sealed class StudioReportEditorState
{
    /// <summary>
    /// Server publication id once the report has been published at least once. Null while the report is a
    /// brand-new unpublished draft held only in the editor.
    /// </summary>
    public string? PublicationId { get; set; }

    /// <summary>Requested/normalized server route slug for the published report.</summary>
    public string RouteSlug { get; set; } = string.Empty;

    /// <summary>Optimistic-concurrency etag of the server route state. Empty until first read/publish.</summary>
    public string ETag { get; set; } = string.Empty;

    /// <summary>Active server revision once published (0 while unpublished).</summary>
    public long ActiveRevision { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>Narrative block rendered above the layout (intro/context copy for the report).</summary>
    public string Narrative { get; set; } = string.Empty;

    /// <summary>Server-owned visibility scope to publish under (private/organization/team/public).</summary>
    public string Visibility { get; set; } = StudioReportVisibilities.Private;

    /// <summary>Whether the published report may be embedded.</summary>
    public bool Embeddable { get; set; }

    /// <summary>
    /// Data bindings: source content refs (with version pins) every panel can read. The version pin is a
    /// deliberate authoring decision so a published report reads a known content version.
    /// </summary>
    public List<StudioReportBindingEditor> Bindings { get; } = [];

    /// <summary>Layout slots holding chart, map, table, and filter panels (top-to-bottom order).</summary>
    public List<StudioReportPanelEditor> Panels { get; } = [];

    /// <summary>Responsive preview breakpoint currently selected in the editor.</summary>
    public string PreviewBreakpoint { get; set; } = StudioReportBreakpoints.Desktop;

    /// <summary>True once the report has a server publication id (it has been published before).</summary>
    public bool IsPublished => !string.IsNullOrWhiteSpace(PublicationId);
}

/// <summary>A source content binding for report panels, with an explicit version pin.</summary>
public sealed class StudioReportBindingEditor
{
    /// <summary>Author-facing alias panels reference (for example <c>incidents</c>).</summary>
    public string Alias { get; set; } = string.Empty;

    /// <summary>Server content item id the alias resolves to.</summary>
    public string ContentRef { get; set; } = string.Empty;

    /// <summary>
    /// Pinned content version. Blank pins to the latest published version at publish time; an explicit
    /// value (for example <c>v7</c>) pins the report to a known version.
    /// </summary>
    public string VersionPin { get; set; } = string.Empty;
}

/// <summary>A layout panel. Charts use Vega-Lite specs; map/table/filter are layout slots.</summary>
public sealed class StudioReportPanelEditor
{
    public string Title { get; set; } = string.Empty;

    /// <summary>One of <see cref="StudioReportPanelKinds"/>.</summary>
    public string Kind { get; set; } = StudioReportPanelKinds.Chart;

    /// <summary>Binding alias this panel reads from.</summary>
    public string BindingAlias { get; set; } = string.Empty;

    /// <summary>
    /// Vega-Lite chart spec (JSON). Required for <see cref="StudioReportPanelKinds.Chart"/> panels;
    /// Vega-Lite is the chart standard for all report charts (issue AC).
    /// </summary>
    public string VegaLiteSpec { get; set; } = string.Empty;

    public bool IsChart =>
        string.Equals(Kind, StudioReportPanelKinds.Chart, StringComparison.OrdinalIgnoreCase);
}

public static class StudioReportPanelKinds
{
    public const string Chart = "chart";
    public const string Map = "map";
    public const string Table = "table";
    public const string Filter = "filter";

    public static IReadOnlyList<string> All { get; } = [Chart, Map, Table, Filter];
}

public static class StudioReportBreakpoints
{
    public const string Desktop = "desktop";
    public const string Narrow = "narrow";

    public static IReadOnlyList<string> All { get; } = [Desktop, Narrow];
}

public static class StudioReportVisibilities
{
    public const string Private = "private";
    public const string Organization = "organization";
    public const string Team = "team";
    public const string Public = "public";

    public static IReadOnlyList<string> All { get; } = [Private, Organization, Team, Public];
}

/// <summary>Builds and validates the Vega-Lite chart spec for report chart panels.</summary>
public static class StudioReportChartSpec
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
    /// The issue requires every report chart to declare a Vega-Lite schema before publish.
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

/// <summary>
/// Pure pre-publish gate. Publish requires a titled report with at least one panel, every panel bound to a
/// declared data binding, every data binding fully specified, and every chart panel declaring a Vega-Lite
/// spec (issue AC). Server-side publish validation still applies; this gate keeps the Console from offering
/// publish before those authoring decisions are made.
/// </summary>
public sealed record StudioReportPublishReadiness(bool CanPublish, IReadOnlyList<string> UnmetRequirements);

public static class StudioReportPublishEvaluator
{
    public static StudioReportPublishReadiness Evaluate(StudioReportEditorState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var unmet = new List<string>();

        if (string.IsNullOrWhiteSpace(state.Title))
        {
            unmet.Add("Give the report a title.");
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

            // Issue AC: Vega-Lite is the chart spec for all report charts, and every chart must declare a
            // Vega-Lite schema URL before publish.
            if (panel.IsChart && !StudioReportChartSpec.DeclaresVegaLiteSchema(panel.VegaLiteSpec))
            {
                unmet.Add($"Chart panel \"{PanelLabel(panel)}\" must declare a Vega-Lite spec.");
            }
        }

        return new StudioReportPublishReadiness(unmet.Count == 0, unmet);
    }

    private static string PanelLabel(StudioReportPanelEditor panel) =>
        string.IsNullOrWhiteSpace(panel.Title) ? $"untitled {panel.Kind}" : panel.Title;
}

/// <summary>
/// Serializes the report editor state into the canonical <c>honua.report-document.v1</c> payload that is
/// sent to the publication registry as the publish/republish content payload (the server hashes it with
/// SHA-256 and never stores it). Chart specs are embedded verbatim when valid JSON so the Vega-Lite spec
/// round-trips into the published document; otherwise the raw string is preserved for server validation to
/// reject. The serializer is deterministic so two equal editor states hash identically.
/// </summary>
public static class StudioReportDocument
{
    public const string Format = "honua.report-document.v1";

    private static readonly JsonSerializerOptions DocumentOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(StudioReportEditorState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var document = new Dictionary<string, object?>
        {
            ["format"] = Format,
            ["title"] = state.Title,
            ["description"] = state.Description,
            ["narrative"] = state.Narrative,
            ["bindings"] = state.Bindings
                .Select(binding => new Dictionary<string, object?>
                {
                    ["alias"] = binding.Alias,
                    ["contentRef"] = binding.ContentRef,
                    ["versionPin"] = string.IsNullOrWhiteSpace(binding.VersionPin) ? null : binding.VersionPin
                })
                .ToList(),
            ["panels"] = state.Panels
                .Select(panel => new Dictionary<string, object?>
                {
                    ["title"] = panel.Title,
                    ["kind"] = panel.Kind,
                    ["bindingAlias"] = panel.BindingAlias,
                    ["chartSpec"] = panel.IsChart ? ParseChartSpec(panel.VegaLiteSpec) : null
                })
                .ToList()
        };

        return JsonSerializer.Serialize(document, DocumentOptions);
    }

    private static object? ParseChartSpec(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(spec);
        }
        catch (JsonException)
        {
            // Preserve the raw (invalid) spec so server validation rejects it rather than silently dropping
            // the author's chart spec.
            return spec;
        }
    }
}

// --- Data-source-facing projections for the authoring surface (immutable). ---

/// <summary>Outcome of a mutating report lifecycle command (publish, republish, rollback, policy).</summary>
/// <param name="Succeeded">Whether the command succeeded.</param>
/// <param name="Message">Operator-facing status message.</param>
/// <param name="Publication">The resulting publication view on success.</param>
/// <param name="Issue">A capability state when the command failed at the binding/permission/transport level.</param>
/// <param name="FieldErrors">
/// Field-addressable server validation errors parsed from a 400 ProblemDetails <c>errors[]</c> (the shared
/// honua-server <c>FieldValidationError</c> contract). Empty unless the server rejected the publish with
/// field-level findings; the report builder binds these onto the offending inputs via
/// <see cref="Honua.Console.Shell.Validation.StudioReportServerErrorBinder"/>.
/// </param>
public sealed record StudioReportCommandResult(
    bool Succeeded,
    string Message,
    StudioReportPublicationView? Publication = null,
    StudioReportCapabilityState? Issue = null,
    IReadOnlyList<Honua.Console.Contracts.HonuaFieldValidationError>? FieldErrors = null);
