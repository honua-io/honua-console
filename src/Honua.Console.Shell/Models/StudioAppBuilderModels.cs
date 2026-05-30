using System.Text.Json;

namespace Honua.Console.Shell.Models;

// Console-side authoring state for the Studio app builder (/studio/app, honua-console#58). These are
// editor-owned projections; the durable contract is the honua-server app.package envelope reached
// through the Honua.Console.Contracts Studio package lifecycle shim (#1180/#1181/#1183). No standing
// in-memory app client exists in the merged runtime — when no server is bound the data source returns
// a missing-binding capability state (Console Patterns Charter section 11).

/// <summary>An app page: a stable route composed of one or more component bindings.</summary>
public sealed class StudioAppPageState
{
    public string Route { get; set; } = "/";

    public string Title { get; set; } = string.Empty;

    /// <summary>Component kind rendered on the page (map, dashboard, form, report, table).</summary>
    public string ComponentKind { get; set; } = "map";

    /// <summary>The saved content item or version the component binds to (e.g. <c>content:permits@v3</c>).</summary>
    public string ContentBinding { get; set; } = string.Empty;
}

/// <summary>A bound action exposed by the generated app, gated by a permission requirement.</summary>
public sealed class StudioAppActionState
{
    public string Name { get; set; } = string.Empty;

    /// <summary>The page route the action is wired to.</summary>
    public string PageRoute { get; set; } = "/";

    /// <summary>The permission required to invoke the action in the generated-app runtime.</summary>
    public string RequiredPermission { get; set; } = "viewer";
}

public static class StudioAppComponentKinds
{
    public static IReadOnlyList<string> All { get; } = ["map", "dashboard", "form", "report", "table"];
}

public static class StudioAppPermissions
{
    public static IReadOnlyList<string> All { get; } = ["viewer", "editor", "operator"];
}

public static class StudioAppVisibilityModes
{
    public static IReadOnlyList<string> All { get; } = ["private", "workspace", "organization", "public"];
}

/// <summary>
/// Editor state for a Studio app package. Mutated client-side, then mapped into the app.package
/// envelope body on save/validate/publish through the server lifecycle contract. <see cref="DraftId"/>
/// and <see cref="ItemId"/> track the server-owned draft/content-item identity once a draft is created.
/// </summary>
public sealed class StudioAppEditorState
{
    public Guid? DraftId { get; set; }

    public Guid? ItemId { get; set; }

    public Guid? CurrentVersionId { get; set; }

    public int? PublishedVersion { get; set; }

    /// <summary>Server optimistic-concurrency generation token for the open draft.</summary>
    public long Generation { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string Visibility { get; set; } = "workspace";

    public bool EmbedEnabled { get; set; }

    /// <summary>Operator must explicitly review share/embed policy before publish (AC: explicit before publish).</summary>
    public bool ShareEmbedPolicyReviewed { get; set; }

    public List<StudioAppPageState> Pages { get; set; } = [];

    public List<StudioAppActionState> Actions { get; set; } = [];

    public bool IsExistingDraft => DraftId is not null;

    public bool IsPublished => PublishedVersion is > 0;
}

/// <summary>
/// A binding/permission/empty surface for the app builder, mirroring
/// <see cref="StudioFormCapabilityState"/> so missing bindings, missing permissions, and unsupported
/// contracts render consistently across Studio editors.
/// </summary>
public sealed record StudioAppCapabilityState(
    string Surface,
    string State,
    string Contract,
    string Detail);

/// <summary>Load result for the app builder: either an editor scaffold or a capability state.</summary>
public sealed record StudioAppEditorLoad(
    StudioAppEditorState? State,
    IReadOnlyList<StudioAppCapabilityState> CapabilityStates)
{
    public bool HasEditor => State is not null;
}

/// <summary>Outcome of a mutating app lifecycle command (save, validate, publish, reopen).</summary>
public sealed record StudioAppCommandResult(
    bool Succeeded,
    string Message,
    StudioAppEditorState? State = null,
    StudioAppValidationView? Validation = null,
    StudioAppCapabilityState? Issue = null);

public sealed record StudioAppValidationView(
    bool IsValid,
    IReadOnlyList<StudioAppValidationItem> Issues);

public sealed record StudioAppValidationItem(
    string Severity,
    string Code,
    string? Path,
    string Message);

/// <summary>
/// Pure pre-publish gate for the app builder. Publish requires at least one page bound to content,
/// every action declaring a permission requirement, and an explicit reviewed share/embed policy.
/// Server-side publish validation still applies; this gate keeps the Console from offering publish
/// before those operator decisions are made.
/// </summary>
public sealed record StudioAppPublishReadiness(bool CanPublish, IReadOnlyList<string> UnmetRequirements);

/// <summary>
/// Pure evaluation of app publish readiness and a stable app.package envelope body projection. Kept
/// free of UI and HTTP concerns so it is unit-testable and reused by the server data source.
/// </summary>
public static class StudioAppPackageMapper
{
    public const string SchemaVersion = "studio-app/v1";

    public static StudioAppEditorState CreateTemplate()
    {
        return new StudioAppEditorState
        {
            Title = "Untitled app",
            Visibility = "workspace",
            Pages =
            [
                new StudioAppPageState { Route = "/", Title = "Home", ComponentKind = "map", ContentBinding = string.Empty }
            ],
            Actions = []
        };
    }

    public static StudioAppPublishReadiness EvaluatePublishReadiness(StudioAppEditorState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var unmet = new List<string>();

        if (state.Pages.Count == 0)
        {
            unmet.Add("Add at least one app page.");
        }

        if (state.Pages.Count > 0 && state.Pages.All(page => string.IsNullOrWhiteSpace(page.ContentBinding)))
        {
            unmet.Add("Bind at least one page component to a saved content version.");
        }

        if (state.Pages.Any(page => string.IsNullOrWhiteSpace(page.Route)))
        {
            unmet.Add("Every page must declare a stable route.");
        }

        if (state.Actions.Any(action => string.IsNullOrWhiteSpace(action.RequiredPermission)))
        {
            unmet.Add("Every app action must declare a required permission.");
        }

        if (!state.ShareEmbedPolicyReviewed)
        {
            unmet.Add("Review the share/embed policy before publish.");
        }

        return new StudioAppPublishReadiness(unmet.Count == 0, unmet);
    }

    /// <summary>
    /// Projects the editor state into the app.package envelope body. The shape mirrors the
    /// pages/components/navigation/actions/permissions/share-embed deliverable; the server validates
    /// and persists it as the durable content version.
    /// </summary>
    public static JsonElement BuildEnvelopeBody(StudioAppEditorState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var body = new Dictionary<string, object?>
        {
            ["schemaVersion"] = SchemaVersion,
            ["title"] = state.Title,
            ["summary"] = state.Summary,
            ["pages"] = state.Pages.Select(page => new Dictionary<string, object?>
            {
                ["route"] = page.Route,
                ["title"] = page.Title,
                ["component"] = new Dictionary<string, object?>
                {
                    ["kind"] = page.ComponentKind,
                    ["binding"] = page.ContentBinding
                }
            }).ToArray(),
            ["navigation"] = state.Pages
                .Where(page => !string.IsNullOrWhiteSpace(page.Route))
                .Select(page => new Dictionary<string, object?>
                {
                    ["route"] = page.Route,
                    ["label"] = string.IsNullOrWhiteSpace(page.Title) ? page.Route : page.Title
                })
                .ToArray(),
            ["actions"] = state.Actions.Select(action => new Dictionary<string, object?>
            {
                ["name"] = action.Name,
                ["pageRoute"] = action.PageRoute,
                ["requiredPermission"] = action.RequiredPermission
            }).ToArray(),
            ["sharePolicy"] = new Dictionary<string, object?>
            {
                ["visibility"] = state.Visibility,
                ["embed"] = state.EmbedEnabled,
                ["reviewed"] = state.ShareEmbedPolicyReviewed
            }
        };

        return JsonSerializer.SerializeToElement(body);
    }
}
