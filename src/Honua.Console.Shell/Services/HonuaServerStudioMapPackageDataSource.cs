using System.Globalization;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Validation;
// The server wire enum, not any editor-catalog enum of the same simple name.
using StudioPackageFamily = Honua.Console.Contracts.StudioPackageFamily;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Studio map-builder data source bound to the real honua-server Studio package lifecycle
/// (honua-server#1180, closed) and the content publication registry (honua-server#1183, closed) through the
/// <see cref="IStudioPackageLifecycleClient"/> shim. A map draft is created/updated as a
/// <c>map.package</c> envelope, validated and saved as an immutable content version, then routed to the
/// publication contract; reopening a published version creates a fresh draft generation seeded from that
/// version so the published map is never mutated in place. There is no in-memory map data in the merged
/// result (Console Patterns Charter section 11). Endpoint issues (missing permission, unsupported verb,
/// conflict, transport) surface as explicit capability states instead of throwing or fabricating data.
/// Mirrors <see cref="HonuaServerStudioAppPackageDataSource"/>.
/// </summary>
public sealed class HonuaServerStudioMapPackageDataSource : IStudioMapPackageDataSource
{
    private const string Surface = "Map builder";
    private const string ListContract = "GET /api/v1/studio/package-drafts (list)";
    private const string LoadContract = "GET /api/v1/studio/package-drafts/{draftId}";
    private const string CreateContract = "POST /api/v1/studio/package-drafts";
    private const string UpdateContract = "PUT /api/v1/studio/package-drafts/{draftId}";
    private const string SaveVersionContract = "POST /api/v1/studio/package-drafts/{draftId}/content-versions";
    private const string PublishContract =
        "POST /api/v1/studio/content-items/{itemId}/versions/{versionId}/publish-requests";
    private const string ReopenContract =
        "POST /api/v1/studio/content-items/{itemId}/versions/{versionId}/reopen";

    private readonly IStudioPackageLifecycleClient _client;

    public HonuaServerStudioMapPackageDataSource(IStudioPackageLifecycleClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public Task<StudioMapWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default)
    {
        // honua-server#1180/#1183 address packages by id and expose no map-package list verb, so the
        // workspace cannot enumerate existing maps from live data without fabricating them. Surface that
        // explicitly (Console Patterns Charter section 11) — operators reach an existing map by id (deep
        // link / known id) or author a new map. This list binds automatically once a list route lands.
        var listUnsupported = new StudioMapCapabilityState(
            Surface,
            "Unsupported",
            ListContract,
            "honua-server does not yet expose a map-package list endpoint, so existing map packages cannot "
            + "be enumerated from live data. Create a new map, or open a known map by id. The package list "
            + "binds automatically once honua-server adds a Studio package list route.");

        return Task.FromResult(new StudioMapWorkspace([], [listUnsupported]));
    }

    public async Task<StudioMapEditorLoad> LoadAsync(string? mapId, CancellationToken cancellationToken = default)
    {
        // A brand-new map opens a blank Console-owned authoring scaffold; the server draft is created on the
        // first save. Existing drafts always load their live state from the server.
        if (string.IsNullOrWhiteSpace(mapId))
        {
            return new StudioMapEditorLoad(StudioMapPackageMapper.CreateTemplate(), []);
        }

        if (!Guid.TryParse(mapId, out var draftId))
        {
            return new StudioMapEditorLoad(
                null,
                [new StudioMapCapabilityState(
                    Surface,
                    "Unsupported",
                    LoadContract,
                    "Map packages are addressed by the server draft id (a GUID). "
                    + $"'{mapId}' is not a valid map draft id.")]);
        }

        var result = await _client.GetPackageDraftAsync(draftId, cancellationToken).ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            return new StudioMapEditorLoad(null, [ToCapabilityState(LoadContract, issue)]);
        }

        return new StudioMapEditorLoad(ToEditorState(result.Data!), []);
    }

    public async Task<StudioMapCommandResult> SaveDraftAsync(
        StudioMapEditorState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.IsPublished)
        {
            return Failure("This map version is published. Reopen it as a draft before editing.");
        }

        var envelope = BuildEnvelope(state);
        StudioEndpointResult<StudioPackageDraft> result;

        if (state.DraftId is { } existingDraftId)
        {
            var request = new UpdateStudioPackageDraftRequest
            {
                PackageKey = StudioMapPackageMapper.BuildPackageKey(state),
                Envelope = envelope,
                Generation = state.Generation
            };
            result = await _client
                .UpdatePackageDraftAsync(existingDraftId, request, cancellationToken)
                .ConfigureAwait(false);

            if (result.Issue is { } updateIssue)
            {
                return FailureFrom(UpdateContract, updateIssue);
            }
        }
        else
        {
            var request = new CreateStudioPackageDraftRequest
            {
                PackageKey = StudioMapPackageMapper.BuildPackageKey(state),
                Envelope = envelope
            };
            result = await _client.CreatePackageDraftAsync(request, cancellationToken).ConfigureAwait(false);

            if (result.Issue is { } createIssue)
            {
                return FailureFrom(CreateContract, createIssue);
            }
        }

        // Keep the operator's current authoring edits and only stamp the server-owned identity/generation
        // from the response. Rehydrating from the echoed envelope body here would risk dropping unsaved
        // local intent if the server normalised the body; the body round-trip belongs to load/reopen.
        ApplyServerIdentity(state, result.Data!);
        return new StudioMapCommandResult(
            true,
            $"Saved map draft ({result.Data!.PackageKey}). Review before publishing.",
            state);
    }

    public async Task<StudioMapCommandResult> PublishAsync(
        StudioMapEditorState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.IsPublished)
        {
            return Failure("This map version is published. Reopen it as a draft before publishing a new version.");
        }

        // AC#2: layers/basemap/extent/title are an explicit reviewed decision before publish.
        var readiness = StudioMapPublishEvaluator.Evaluate(state);
        if (!readiness.CanPublish)
        {
            return Failure(
                $"Resolve {readiness.UnmetRequirements.Count.ToString(CultureInfo.InvariantCulture)} "
                + $"requirement(s) before publish: {string.Join(" ", readiness.UnmetRequirements)}");
        }

        if (state.DraftId is not { } draftId)
        {
            return Failure("Save the map draft before publishing.");
        }

        // Freeze the current draft as an immutable content version. Reopened edits create a new draft
        // generation and a new version on the next save, so a published version is never mutated in place.
        var versionResult = await _client
            .SaveContentVersionAsync(
                draftId,
                new SaveStudioContentVersionRequest { ChangeNote = $"Publish {state.Title}".Trim() },
                cancellationToken)
            .ConfigureAwait(false);

        if (versionResult.Issue is { } versionIssue)
        {
            return FailureFrom(SaveVersionContract, versionIssue);
        }

        var version = versionResult.Data!;
        var publishRequest = new CreateStudioPublicationRequest
        {
            Intent = new StudioPublicationIntent
            {
                Visibility = state.ShareTier,
                Embed = state.EmbedAllowed
            }
        };

        var publishResult = await _client
            .CreatePublishRequestAsync(version.ItemId, version.VersionId, publishRequest, cancellationToken)
            .ConfigureAwait(false);

        if (publishResult.Issue is { } publishIssue)
        {
            return FailureFrom(PublishContract, publishIssue);
        }

        var published = state;
        published.ItemId = version.ItemId;
        published.VersionId = version.VersionId;
        published.Version = version.VersionNumber;
        published.Status = StudioMapStatuses.Published;

        var status = publishResult.Data!.Status.ToString().ToLowerInvariant();
        return new StudioMapCommandResult(
            true,
            $"Publication request {status} for v{version.VersionNumber.ToString(CultureInfo.InvariantCulture)}.",
            published);
    }

    public async Task<StudioMapCommandResult> ReopenAsync(
        StudioMapEditorState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.ItemId is not { } itemId || state.VersionId is not { } versionId)
        {
            return Failure("This map has no published server version to reopen.");
        }

        var result = await _client
            .ReopenContentVersionAsync(itemId, versionId, cancellationToken)
            .ConfigureAwait(false);

        if (result.Issue is { } issue)
        {
            return Failure(issue.Detail, ToCapabilityState(ReopenContract, issue));
        }

        var draft = ToEditorState(result.Data!);
        draft.ReopenedFromVersion = state.Version;
        return new StudioMapCommandResult(
            true,
            $"Reopened v{state.Version.ToString(CultureInfo.InvariantCulture)} as a new draft.",
            draft);
    }

    private static StudioPackageEnvelope BuildEnvelope(StudioMapEditorState state) =>
        new()
        {
            Family = StudioPackageFamily.Map,
            SchemaVersion = StudioMapPackageMapper.SchemaVersion,
            Format = "map.package",
            PublicationIntent = new StudioPublicationIntent
            {
                Visibility = state.ShareTier,
                Embed = state.EmbedAllowed
            },
            Body = StudioMapPackageMapper.BuildEnvelopeBody(state)
        };

    private static StudioMapEditorState ToEditorState(StudioPackageDraft draft)
    {
        // The server is the source of truth for identity/generation; the authoring content is rehydrated
        // from the draft's envelope body (the same round-trip surface a save froze), so loading/reopening a
        // map restores its layers/frame/behaviour rather than a blank scaffold.
        var state = StudioMapPackageMapper.CreateTemplate();
        StudioMapPackageMapper.ApplyEnvelopeBody(state, draft.Envelope.Body);
        ApplyServerIdentity(state, draft);
        return state;
    }

    private static void ApplyServerIdentity(StudioMapEditorState state, StudioPackageDraft draft)
    {
        state.DraftId = draft.DraftId;
        state.ItemId = draft.ItemId == Guid.Empty ? null : draft.ItemId;
        state.VersionId = draft.BaseVersionId;
        state.MapId = draft.ItemId == Guid.Empty ? draft.DraftId.ToString() : draft.ItemId.ToString();
        state.Generation = draft.Generation;
        state.Status = StudioMapStatuses.Draft;
    }

    private static StudioMapCapabilityState ToCapabilityState(string contract, StudioEndpointIssue issue) =>
        new(
            Surface,
            issue.State,
            issue.Contract ?? contract,
            issue.StatusCode is null
                ? issue.Detail
                : $"{issue.Detail} HTTP {issue.StatusCode.Value.ToString(CultureInfo.InvariantCulture)}.");

    private static StudioMapCommandResult Failure(string message, StudioMapCapabilityState? issue = null) =>
        new(false, message, Issue: issue);

    /// <summary>
    /// Builds a failure result from an endpoint issue, mapping any structured Studio validation diagnostics
    /// the server returned (JSON-Pointer addressed) onto console field keys so the page can surface each one
    /// inline next to the offending layer/field. The capability state still carries the human-readable detail.
    /// </summary>
    private static StudioMapCommandResult FailureFrom(string contract, StudioEndpointIssue issue) =>
        new(
            false,
            issue.Detail,
            Issue: ToCapabilityState(contract, issue),
            FieldErrors: StudioMapServerErrorBinder.Map(issue.Diagnostics));
}
