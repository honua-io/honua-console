using Honua.Sdk.Studio.Packages;
using System.Globalization;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Validation;
// The server wire enum, not any editor-catalog enum of the same simple name.
using StudioPackageFamily = Honua.Sdk.Studio.Packages.StudioPackageFamily;

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
    private const string GenerateContract = "POST /api/v1/studio/map-packages/generate";

    private readonly IStudioPackageLifecycleClient _client;
    private readonly IStudioMapGenerationClient _generation;
    private readonly IOperateTransitionDataSource _operate;

    public HonuaServerStudioMapPackageDataSource(
        IStudioPackageLifecycleClient client,
        IStudioMapGenerationClient generation,
        IOperateTransitionDataSource operate)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _generation = generation ?? throw new ArgumentNullException(nameof(generation));
        _operate = operate ?? throw new ArgumentNullException(nameof(operate));
    }

    // Catalog grounding: the real published layers in the workspace, so map generation binds real
    // serviceId/layerId/extent instead of inventing placeholders (and the preview can render them).
    private async Task<IReadOnlyList<MapGenerationSource>> LoadAvailableSourcesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var view = await _operate.GetLayersViewAsync(cancellationToken).ConfigureAwait(false);
            var sources = new List<MapGenerationSource>();
            foreach (var service in view.Services)
            {
                foreach (var layer in service.Layers)
                {
                    sources.Add(new MapGenerationSource
                    {
                        ServiceId = service.Name,
                        LayerId = layer.LayerId.ToString(CultureInfo.InvariantCulture),
                        Name = layer.Name,
                        GeometryType = layer.Geometry,
                        Protocol = "geoservices_feature_service",
                        Bbox = layer.Extent,
                    });
                }
            }

            return sources;
        }
        catch (Exception)
        {
            // Grounding is best-effort; without it the model falls back to placeholder bindings.
            return [];
        }
    }

    public async Task<StudioMapWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default)
    {
        // honua-server now exposes a Studio package-draft list route, so the workspace enumerates existing
        // map drafts from live data (filtered to the map family). The summaries are secret-safe — they carry
        // identity/title-key/timestamps but not the package body — so layer counts are not available without
        // opening a draft; the list still lets operators reach an existing map by id, or author a new one.
        var result = await _client
            .ListPackageDraftsAsync(StudioPackageFamily.Map, status: null, cancellationToken)
            .ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            return new StudioMapWorkspace([], [ToCapabilityState(ListContract, issue)]);
        }

        var packages = (result.Data!.Drafts ?? [])
            .Select(summary => new StudioMapPackageListItem(
                summary.DraftId.ToString(),
                string.IsNullOrWhiteSpace(summary.PackageKey) ? summary.DraftId.ToString() : summary.PackageKey,
                LayerCount: 0,
                DraftVersion: null,
                PublishedVersion: null,
                summary.UpdatedAt))
            .OrderByDescending(item => item.UpdatedAt)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new StudioMapWorkspace(packages, []);
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

    public async Task<StudioMapGenerationOutcome> GenerateAsync(
        StudioMapEditorState currentState,
        StudioMapGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentState);
        ArgumentNullException.ThrowIfNull(request);

        // A refine turn ships the current map.package body so the server edits it; a first turn ships null
        // (fresh). The body is the same round-trip surface BuildEnvelopeBody/ApplyEnvelopeBody use.
        var hasMap = currentState.Layers.Count > 0
            || !string.IsNullOrWhiteSpace(currentState.Title)
            || !string.IsNullOrWhiteSpace(currentState.Basemap)
            || !string.IsNullOrWhiteSpace(currentState.InitialExtent);

        var wire = new GenerateMapPackageRequest
        {
            Prompt = request.Prompt,
            Provider = request.Provider,
            Model = request.Model,
            Package = hasMap ? StudioMapPackageMapper.BuildEnvelopeBody(currentState) : null,
            Conversation = request.Conversation
                .Select(turn => new MapGenerationTurn { Role = turn.Role, Content = turn.Content })
                .ToArray(),
            Answers = request.Answers
                .Select(answer => new MapGenerationAnswer { QuestionId = answer.QuestionId, OptionId = answer.OptionId })
                .ToArray(),
            AvailableSources = await LoadAvailableSourcesAsync(cancellationToken).ConfigureAwait(false)
        };

        var availableSources = wire.AvailableSources;
        var result = await _generation.GenerateMapAsync(wire, cancellationToken).ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            // A 404/501 means this server lacks the map generation contract: AI is simply off here (honest
            // "unsupported"), not a missing server binding. Other issues block the surface so the page shows
            // the shared blocked state.
            if (issue.StatusCode is 404 or 501)
            {
                return new StudioMapGenerationOutcome
                {
                    Status = StudioMapGenerationStatuses.Unsupported,
                    Rationale = "This server does not offer AI map generation yet."
                };
            }

            return StudioMapGenerationOutcome.Blocked(ToCapabilityState(GenerateContract, issue));
        }

        var outcome = MapGeneration(currentState, result.Data!);
        ResolveBoundLayerIds(outcome.State, availableSources);
        return outcome;
    }

    // When generation named a service but not the numeric layer id (small local models are inconsistent),
    // resolve each layer's BoundLayerId from the real catalog by matching its BoundServiceId — so the
    // builder preview can render the real layer via /map-proxy/styles/{layerId}.json every time.
    private static void ResolveBoundLayerIds(StudioMapEditorState? state, IReadOnlyList<MapGenerationSource> available)
    {
        if (state is null || available.Count == 0)
        {
            return;
        }

        foreach (var layer in state.Layers)
        {
            if (!string.IsNullOrWhiteSpace(layer.BoundLayerId))
            {
                continue;
            }

            var match = available.FirstOrDefault(source =>
                !string.IsNullOrWhiteSpace(layer.BoundServiceId)
                && string.Equals(source.ServiceId, layer.BoundServiceId, StringComparison.OrdinalIgnoreCase));

            // Single-source workspace: if the model bound nothing resolvable but there's exactly one real
            // layer, bind it so the map still renders the available data.
            match ??= available.Count == 1 ? available[0] : null;

            if (match is not null && !string.IsNullOrWhiteSpace(match.LayerId))
            {
                layer.BoundLayerId = match.LayerId;
                if (string.IsNullOrWhiteSpace(layer.BoundServiceId))
                {
                    layer.BoundServiceId = match.ServiceId;
                }
            }
        }

        // The small local model frequently returns an EMPTY map (no layers) for a prompt like "a map of the
        // E2E Source layer". When the workspace has exactly ONE real source, SEED a bound layer so the
        // workflow still produces a map that RENDERS the data the operator asked for — not an empty package
        // they must hand-fix. We only seed for a single-source workspace: with several real layers there is
        // no signal for which one the operator meant, and silently binding an arbitrary available[0] would
        // present the wrong layer as the answer. The layer is the real catalog layer, never fabricated.
        var bound = state.Layers.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l.BoundLayerId));
        if (bound is null && available.Count == 1)
        {
            var src = available[0];
            if (!string.IsNullOrWhiteSpace(src.LayerId))
            {
                bound = new StudioMapLayerEditor
                {
                    BoundLayerId = src.LayerId,
                    BoundServiceId = src.ServiceId,
                    Title = string.IsNullOrWhiteSpace(src.Name) ? src.ServiceId : src.Name!,
                    SourceRef = $"service:{src.ServiceId}/{src.LayerId}",
                    Visible = true,
                };
                state.Layers.Add(bound);
            }
        }

        // Frame + finish the map so it actually renders and is closer to publish-ready (the model usually
        // leaves the extent/basemap/title unset). Use the bound layer's real EPSG:4326 extent for the view.
        if (bound is not null)
        {
            // Only frame from the source that the bound layer actually resolves to — never from an unrelated
            // available[0], which would stamp a different layer's extent and frame the wrong area.
            var src = available.FirstOrDefault(s => string.Equals(s.LayerId, bound.BoundLayerId, StringComparison.Ordinal));
            // A real EPSG:4326 bbox must be four FINITE numbers; an unset/sentinel extent can carry NaN or
            // ±Infinity, which would serialize to "NaN"/"Infinity" and slip through bbox validation (NaN
            // comparisons are all false) — producing a degenerate extent the framing was meant to prevent.
            if (string.IsNullOrWhiteSpace(state.InitialExtent)
                && src?.Bbox is { Length: 4 } bbox
                && bbox.All(double.IsFinite))
            {
                state.InitialExtent = string.Join(",", bbox.Select(n => n.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }

            if (string.IsNullOrWhiteSpace(state.Basemap))
            {
                state.Basemap = "basemap:streets";
            }

            if (string.IsNullOrWhiteSpace(state.Title))
            {
                state.Title = bound.Title;
            }
        }
    }

    private static StudioMapGenerationOutcome MapGeneration(
        StudioMapEditorState currentState,
        MapGenerationResult result)
    {
        // Each collection declares a non-null initializer, but System.Text.Json overrides it with null when
        // the server emits an explicit JSON null for the key (an omitted key keeps the initializer), so every
        // collection on the deserialized result may be null. Guard each before LINQ — otherwise a perfectly
        // valid 'generated' result (which carries no clarifications/unmapped) throws and freezes the page
        // (mirrors the workflow client's defensive mapping).
        var warnings = new List<string>();
        if (result.Validation is not null)
        {
            warnings.AddRange(result.Validation.Warnings ?? []);
        }

        warnings.AddRange((result.UnmappedRequests ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => $"No mapping for: {item}"));

        var clarifications = (result.Clarifications ?? [])
            .Select(question => new StudioConversationClarification(
                question.Id,
                string.IsNullOrWhiteSpace(question.Prompt) ? question.Kind : question.Prompt,
                question.Reason ?? string.Empty,
                (question.Choices ?? [])
                    .Select(choice => new StudioConversationChoice(choice.Id, choice.Label, choice.Effect))
                    .ToArray()))
            .ToArray();

        StudioMapEditorState? state = null;
        var generated = string.Equals(result.Status, StudioMapGenerationStatuses.Generated, StringComparison.Ordinal);
        if (generated && result.Package is { } package)
        {
            // Hydrate the editor from the returned honua_map_package.v1 body (sourceBindings/styleRefs/
            // initialView/popupBindings/legend) — a DIFFERENT shape from the console's round-trip envelope,
            // so use the generation-specific mapper, NOT ApplyEnvelopeBody (which reads the `layers` shape and
            // would bind zero layers). Server-owned identity/generation on currentState is preserved.
            warnings.AddRange(StudioMapPackageMapper.ApplyGeneratedPackage(currentState, package));
            // The proposed map changed; it must be re-saved + re-validated before any publish.
            currentState.Status = StudioMapStatuses.Draft;
            state = currentState;

            if (result.Validation is { IsValid: false })
            {
                warnings.AddRange((result.Validation.Failures ?? []).Select(failure => failure.Message));
            }
        }

        return new StudioMapGenerationOutcome
        {
            Status = NormalizeStatus(result.Status),
            State = state,
            Rationale = result.Rationale ?? string.Empty,
            Clarifications = clarifications,
            Warnings = warnings,
            CapabilityState = result.CapabilityState is null
                ? null
                : new StudioMapGenerationCapability(
                    result.CapabilityState.Name,
                    result.CapabilityState.State,
                    result.CapabilityState.Reason),
            Provider = result.Provider,
            Model = result.Model
        };
    }

    private static string NormalizeStatus(string? status) => status switch
    {
        StudioMapGenerationStatuses.Generated => StudioMapGenerationStatuses.Generated,
        StudioMapGenerationStatuses.NeedsClarification => StudioMapGenerationStatuses.NeedsClarification,
        StudioMapGenerationStatuses.Unsupported => StudioMapGenerationStatuses.Unsupported,
        StudioMapGenerationStatuses.Refused => StudioMapGenerationStatuses.Refused,
        _ => StudioMapGenerationStatuses.Error
    };

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
        StudioMapPackageMapper.ApplyEnvelopeBody(state, draft.Envelope?.Body);
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
