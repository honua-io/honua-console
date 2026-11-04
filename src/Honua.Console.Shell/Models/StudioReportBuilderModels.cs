using System.Text.Json;
using System.Text.Json.Serialization;

namespace Honua.Console.Shell.Models;

public sealed class StudioReportEditorState
{
    public string? PublicationId { get; set; }

    public string RouteSlug { get; set; } = string.Empty;

    // Empty until the first read or publish, so there is no concurrency token yet.
    public string ETag { get; set; } = string.Empty;

    public long ActiveRevision { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Narrative { get; set; } = string.Empty;

    public string Visibility { get; set; } = StudioReportVisibilities.Private;

    public bool Embeddable { get; set; }

    public List<StudioReportBindingEditor> Bindings { get; } = [];

    public List<StudioReportPanelEditor> Panels { get; } = [];

    public string PreviewBreakpoint { get; set; } = StudioReportBreakpoints.Desktop;

    public bool IsPublished => !string.IsNullOrWhiteSpace(PublicationId);
}

public sealed class StudioReportBindingEditor
{
    public string Alias { get; set; } = string.Empty;

    public string ContentRef { get; set; } = string.Empty;

    // Blank means the latest published version at publish time.
    public string VersionPin { get; set; } = string.Empty;
}

public sealed class StudioReportPanelEditor
{
    public string Title { get; set; } = string.Empty;

    public string Kind { get; set; } = StudioReportPanelKinds.Chart;

    public string BindingAlias { get; set; } = string.Empty;

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

public static class StudioReportChartSpec
{
    public const string VegaLiteSchema = "https://vega.github.io/schema/vega-lite/v6.json";

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

// The server hashes this payload and does not store the document, so serialization stays deterministic.
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

    public static void ApplyTo(StudioReportEditorState state, JsonElement document)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (document.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        state.Title = GetString(document, "title");
        state.Description = GetString(document, "description");
        state.Narrative = GetString(document, "narrative");

        state.Bindings.Clear();
        if (document.TryGetProperty("bindings", out var bindings) && bindings.ValueKind == JsonValueKind.Array)
        {
            foreach (var binding in bindings.EnumerateArray())
            {
                if (binding.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                state.Bindings.Add(new StudioReportBindingEditor
                {
                    Alias = GetString(binding, "alias"),
                    ContentRef = GetString(binding, "contentRef"),
                    VersionPin = GetString(binding, "versionPin")
                });
            }
        }

        state.Panels.Clear();
        if (document.TryGetProperty("panels", out var panels) && panels.ValueKind == JsonValueKind.Array)
        {
            foreach (var panel in panels.EnumerateArray())
            {
                if (panel.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var kind = GetString(panel, "kind");
                var editor = new StudioReportPanelEditor
                {
                    Title = GetString(panel, "title"),
                    Kind = string.IsNullOrWhiteSpace(kind) ? StudioReportPanelKinds.Chart : kind,
                    BindingAlias = GetString(panel, "bindingAlias")
                };

                if (panel.TryGetProperty("chartSpec", out var spec) &&
                    spec.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
                {
                    editor.VegaLiteSpec = spec.ValueKind == JsonValueKind.String
                        ? spec.GetString() ?? string.Empty
                        : spec.GetRawText();
                }

                state.Panels.Add(editor);
            }
        }
    }

    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

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

public sealed record StudioReportCommandResult(
    bool Succeeded,
    string Message,
    StudioReportPublicationView? Publication = null,
    StudioReportCapabilityState? Issue = null,
    IReadOnlyList<Honua.Console.Contracts.HonuaFieldValidationError>? FieldErrors = null);
