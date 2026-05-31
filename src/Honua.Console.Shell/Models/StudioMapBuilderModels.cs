using System.Text.Json;

namespace Honua.Console.Shell.Models;

/// <summary>
/// Mutable Studio map-builder editor state. This is Console-owned authoring state bound to the editor
/// UI; it maps to and from the server-owned <c>honua.map-package</c> lifecycle (honua-server#1180) and
/// the publication registry (honua-server#1183) through the Honua.Console.Contracts shim. It is not a
/// server wire contract and is never persisted as a standing mock data source: the only persistence
/// path is the real honua-server map package lifecycle.
/// </summary>
public sealed class StudioMapEditorState
{
    /// <summary>Stable server package id. Null until the first draft is created server-side.</summary>
    public string? MapId { get; set; }

    /// <summary>Server package version currently open in the editor.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Server lifecycle status: draft, published, or archived.</summary>
    public string Status { get; set; } = StudioMapStatuses.Draft;

    /// <summary>Optimistic-concurrency ETag for draft updates. Empty for a not-yet-saved draft.</summary>
    public string ETag { get; set; } = string.Empty;

    /// <summary>
    /// Server-owned mutable draft id (honua-server#1180 <c>package-drafts</c>). Null until the first save
    /// creates the draft. Carries the identity that subsequent generation-safe updates and the save/reopen
    /// lifecycle target; never fabricated.
    /// </summary>
    public Guid? DraftId { get; set; }

    /// <summary>Server-owned content item id once a draft has produced at least one immutable version.</summary>
    public Guid? ItemId { get; set; }

    /// <summary>The immutable content version id currently open (set after a save-version / on reopen source).</summary>
    public Guid? VersionId { get; set; }

    /// <summary>Optimistic-concurrency generation echoed back by the server draft; gates safe updates.</summary>
    public long Generation { get; set; }

    /// <summary>Published version a reopened draft was created from, when applicable.</summary>
    public int? ReopenedFromVersion { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>Basemap reference stored in the map package (e.g. <c>basemap:streets</c>).</summary>
    public string Basemap { get; set; } = string.Empty;

    /// <summary>Initial extent expressed as a bounding box <c>minX,minY,maxX,maxY</c>.</summary>
    public string InitialExtent { get; set; } = string.Empty;

    /// <summary>Ordered operational layers (top of the list draws on top).</summary>
    public List<StudioMapLayerEditor> Layers { get; } = [];

    /// <summary>Whether the published map should expose a legend control.</summary>
    public bool ShowLegend { get; set; } = true;

    /// <summary>Whether identify/popups are enabled for the published map.</summary>
    public bool PopupsEnabled { get; set; } = true;

    /// <summary>Whether the published map allows pan/zoom interactions.</summary>
    public bool InteractionsEnabled { get; set; } = true;

    /// <summary>Sharing tier reviewed before publish (e.g. <c>private</c>, <c>workspace</c>, <c>public</c>).</summary>
    public string ShareTier { get; set; } = "private";

    /// <summary>Whether the published map may be embedded; reviewed before publish.</summary>
    public bool EmbedAllowed { get; set; }

    public bool IsExistingPackage => !string.IsNullOrWhiteSpace(MapId);

    public bool IsPublished =>
        string.Equals(Status, StudioMapStatuses.Published, StringComparison.OrdinalIgnoreCase);
}

/// <summary>A single operational layer binding within the map package.</summary>
public sealed class StudioMapLayerEditor
{
    /// <summary>Content/source reference this layer renders, e.g. <c>content:hydrants@v12</c>.</summary>
    public string SourceRef { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public bool Visible { get; set; } = true;

    /// <summary>Optional attribute filter applied to the layer (server-validated before publish).</summary>
    public string Filter { get; set; } = string.Empty;

    /// <summary>Optional style reference stored in the package, e.g. a named style or symbol set.</summary>
    public string Style { get; set; } = string.Empty;

    /// <summary>Comma-separated popup field names exposed by identify; empty disables the popup.</summary>
    public string PopupFields { get; set; } = string.Empty;
}

/// <summary>Server lifecycle status vocabulary for map packages, mirroring the form builder.</summary>
public static class StudioMapStatuses
{
    public const string Draft = "draft";
    public const string Published = "published";
    public const string Archived = "archived";
}

/// <summary>
/// The server's map packages plus any binding/permission capability states. Mirrors
/// <see cref="StudioFormWorkspace"/> so missing bindings render through the shared capability surface.
/// </summary>
public sealed record StudioMapWorkspace(
    IReadOnlyList<StudioMapPackageListItem> Packages,
    IReadOnlyList<StudioMapCapabilityState> CapabilityStates);

public sealed record StudioMapPackageListItem(
    string MapId,
    string Title,
    int LayerCount,
    int? DraftVersion,
    int? PublishedVersion,
    DateTimeOffset UpdatedAt);

/// <summary>
/// A binding/permission/empty surface for the map builder, mirroring the Operate capability-state
/// pattern so missing bindings, missing permissions, and unsupported contracts render consistently.
/// </summary>
public sealed record StudioMapCapabilityState(
    string Surface,
    string State,
    string Contract,
    string Detail);

public sealed record StudioMapEditorLoad(
    StudioMapEditorState? State,
    IReadOnlyList<StudioMapCapabilityState> CapabilityStates)
{
    public bool HasEditor => State is not null;
}

public sealed record StudioMapCommandResult(
    bool Succeeded,
    string Message,
    StudioMapEditorState? State = null,
    StudioMapCapabilityState? Issue = null,
    IReadOnlyList<ConsoleFieldError>? FieldErrors = null);

/// <summary>
/// Pre-publish gate result for the map builder. Publish stays blocked while
/// <see cref="UnmetRequirements"/> is non-empty so that layers, popups, share tier, and embed policy
/// are an explicit reviewed decision before a map is published (AC#2).
/// </summary>
public sealed record StudioMapPublishReadiness(IReadOnlyList<string> UnmetRequirements)
{
    public bool CanPublish => UnmetRequirements.Count == 0;
}

/// <summary>
/// Pure pre-publish evaluation for the map builder. Kept side-effect free so it can be unit tested
/// without a server and reused by the publish-review surface.
/// </summary>
public static class StudioMapPublishEvaluator
{
    public static StudioMapPublishReadiness Evaluate(StudioMapEditorState state)
    {
        var unmet = new List<string>();

        if (string.IsNullOrWhiteSpace(state.Title))
        {
            unmet.Add("Give the map a title.");
        }

        if (state.Layers.Count == 0)
        {
            unmet.Add("Add at least one layer.");
        }

        if (state.Layers.Any(layer => string.IsNullOrWhiteSpace(layer.SourceRef)))
        {
            unmet.Add("Every layer must bind a source reference.");
        }

        if (string.IsNullOrWhiteSpace(state.Basemap))
        {
            unmet.Add("Select a basemap.");
        }

        if (string.IsNullOrWhiteSpace(state.InitialExtent))
        {
            unmet.Add("Set the initial extent.");
        }

        return new StudioMapPublishReadiness(unmet);
    }
}

/// <summary>
/// Pure projection between the Console-owned <see cref="StudioMapEditorState"/> and the server map-package
/// envelope body (honua-server#1180 <c>body</c> JsonElement). Kept side-effect free so it can be unit
/// tested without a server and reused by the server-bound data source. The body is the canonical authoring
/// content that a save freezes into an immutable content version and that a reopen rehydrates, so this is
/// the single round-trip surface — never a standing mock.
/// </summary>
public static class StudioMapPackageMapper
{
    public const string SchemaVersion = "studio-map/v1";

    /// <summary>A fresh, server-unbound draft scaffold. Holds no fabricated package identity.</summary>
    public static StudioMapEditorState CreateTemplate() => new();

    /// <summary>Stable, slug-style package key derived from the title so server drafts are recognisable.</summary>
    public static string BuildPackageKey(StudioMapEditorState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var slug = new string((state.Title ?? string.Empty)
                .ToLowerInvariant()
                .Select(c => char.IsLetterOrDigit(c) ? c : '-')
                .ToArray())
            .Trim('-');

        if (slug.Length > 48)
        {
            slug = slug[..48].Trim('-');
        }

        return string.IsNullOrEmpty(slug) ? "studio-map" : $"studio-map-{slug}";
    }

    /// <summary>Projects the editor's layers/frame/behaviour/share review into the package envelope body.</summary>
    public static JsonElement BuildEnvelopeBody(StudioMapEditorState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var body = new Dictionary<string, object?>
        {
            ["schemaVersion"] = SchemaVersion,
            ["title"] = state.Title,
            ["description"] = state.Description,
            ["basemap"] = state.Basemap,
            ["initialExtent"] = state.InitialExtent,
            ["layers"] = state.Layers.Select(layer => new Dictionary<string, object?>
            {
                ["sourceRef"] = layer.SourceRef,
                ["title"] = layer.Title,
                ["visible"] = layer.Visible,
                ["filter"] = layer.Filter,
                ["style"] = layer.Style,
                ["popupFields"] = SplitFields(layer.PopupFields)
            }).ToArray(),
            ["behavior"] = new Dictionary<string, object?>
            {
                ["showLegend"] = state.ShowLegend,
                ["popupsEnabled"] = state.PopupsEnabled,
                ["interactionsEnabled"] = state.InteractionsEnabled
            },
            ["sharePolicy"] = new Dictionary<string, object?>
            {
                ["tier"] = state.ShareTier,
                ["embedAllowed"] = state.EmbedAllowed
            }
        };

        return JsonSerializer.SerializeToElement(body);
    }

    /// <summary>
    /// Rehydrates editor authoring content from a previously-saved envelope body. Unknown/missing
    /// properties degrade to the scaffold defaults rather than throwing, so a partial or older body still
    /// opens. Server-owned identity (draft id / item id / generation) is applied by the caller, not here.
    /// </summary>
    public static void ApplyEnvelopeBody(StudioMapEditorState state, JsonElement? body)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (body is not { ValueKind: JsonValueKind.Object } root)
        {
            return;
        }

        state.Title = ReadString(root, "title", state.Title);
        state.Description = ReadString(root, "description", state.Description);
        state.Basemap = ReadString(root, "basemap", state.Basemap);
        state.InitialExtent = ReadString(root, "initialExtent", state.InitialExtent);

        if (root.TryGetProperty("behavior", out var behavior) && behavior.ValueKind == JsonValueKind.Object)
        {
            state.ShowLegend = ReadBool(behavior, "showLegend", state.ShowLegend);
            state.PopupsEnabled = ReadBool(behavior, "popupsEnabled", state.PopupsEnabled);
            state.InteractionsEnabled = ReadBool(behavior, "interactionsEnabled", state.InteractionsEnabled);
        }

        if (root.TryGetProperty("sharePolicy", out var share) && share.ValueKind == JsonValueKind.Object)
        {
            state.ShareTier = ReadString(share, "tier", state.ShareTier);
            state.EmbedAllowed = ReadBool(share, "embedAllowed", state.EmbedAllowed);
        }

        state.Layers.Clear();
        if (root.TryGetProperty("layers", out var layers) && layers.ValueKind == JsonValueKind.Array)
        {
            foreach (var layer in layers.EnumerateArray())
            {
                if (layer.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                state.Layers.Add(new StudioMapLayerEditor
                {
                    SourceRef = ReadString(layer, "sourceRef", string.Empty),
                    Title = ReadString(layer, "title", string.Empty),
                    Visible = ReadBool(layer, "visible", true),
                    Filter = ReadString(layer, "filter", string.Empty),
                    Style = ReadString(layer, "style", string.Empty),
                    PopupFields = JoinFields(layer)
                });
            }
        }
    }

    private static string[] SplitFields(string popupFields) =>
        string.IsNullOrWhiteSpace(popupFields)
            ? []
            : popupFields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string JoinFields(JsonElement layer)
    {
        if (!layer.TryGetProperty("popupFields", out var fields) || fields.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        return string.Join(", ", fields.EnumerateArray()
            .Where(field => field.ValueKind == JsonValueKind.String)
            .Select(field => field.GetString())
            .Where(field => !string.IsNullOrWhiteSpace(field)));
    }

    private static string ReadString(JsonElement element, string property, string fallback) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static bool ReadBool(JsonElement element, string property, bool fallback)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return fallback;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback
        };
    }
}
