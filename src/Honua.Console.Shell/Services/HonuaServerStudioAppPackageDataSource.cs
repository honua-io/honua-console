using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
// The server wire enum, not the editor-catalog enum Honua.Console.Shell.Models.StudioPackageFamily.
using StudioPackageFamily = Honua.Console.Contracts.StudioPackageFamily;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Studio app-builder data source bound to the real honua-server Studio package lifecycle and the app
/// publication registry (honua-server#1180/#1181/#1183) through the
/// <see cref="IStudioPackageLifecycleClient"/> shim. Drafts are created/updated as
/// <c>app.package</c> envelopes, validated server-side, saved as immutable content versions, and routed
/// to the publication contract; there is no in-memory app data in the merged result (Console Patterns
/// Charter section 11). Endpoint issues (missing permission, unsupported verb, conflict, transport)
/// surface as explicit capability states instead of throwing or fabricating data. Mirrors
/// <see cref="HonuaServerStudioFormPackageDataSource"/>.
/// </summary>
public sealed class HonuaServerStudioAppPackageDataSource : IStudioAppPackageDataSource
{
    private const string Surface = "App builder";
    private const string LoadContract = "GET /api/v1/studio/package-drafts/{draftId}";
    private const string CreateContract = "POST /api/v1/studio/package-drafts";
    private const string UpdateContract = "PUT /api/v1/studio/package-drafts/{draftId}";
    private const string ValidateContract = "POST /api/v1/studio/package-drafts/{draftId}/validate";
    private const string SaveVersionContract = "POST /api/v1/studio/package-drafts/{draftId}/content-versions";
    private const string PublishContract =
        "POST /api/v1/studio/content-items/{itemId}/versions/{versionId}/publish-requests";
    private const string PreviewContract = "POST /api/v1/studio/package-drafts/{draftId}/preview-plan";
    private const string VersionsContract = "GET /api/v1/studio/content-items/{itemId}/versions";
    private const string ReopenContract =
        "POST /api/v1/studio/content-items/{itemId}/versions/{versionId}/reopen";
    private const string RollbackContract = "POST /api/v1/studio/content-items/{itemId}/rollback-requests";

    private readonly IStudioPackageLifecycleClient _client;

    public HonuaServerStudioAppPackageDataSource(IStudioPackageLifecycleClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<StudioAppEditorLoad> LoadAsync(Guid? draftId, CancellationToken cancellationToken = default)
    {
        // A brand-new app opens a blank Console-owned authoring scaffold; the server draft is created
        // on first save. Existing drafts always load their live state from the server.
        if (draftId is null)
        {
            return new StudioAppEditorLoad(StudioAppPackageMapper.CreateTemplate(), []);
        }

        var result = await _client.GetPackageDraftAsync(draftId.Value, cancellationToken).ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            return new StudioAppEditorLoad(null, [ToCapabilityState(LoadContract, issue)]);
        }

        return new StudioAppEditorLoad(ToEditorState(result.Data!), []);
    }

    public async Task<StudioAppCommandResult> SaveDraftAsync(
        StudioAppEditorState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.IsPublished && !state.IsExistingDraft)
        {
            return Failure("This app version is published. Reopen it as a draft before editing.");
        }

        var envelope = BuildEnvelope(state);
        StudioEndpointResult<StudioPackageDraft> result;

        if (state.IsExistingDraft)
        {
            var request = new UpdateStudioPackageDraftRequest
            {
                PackageKey = BuildPackageKey(state),
                Envelope = envelope,
                Generation = state.Generation
            };
            result = await _client
                .UpdatePackageDraftAsync(state.DraftId!.Value, request, cancellationToken)
                .ConfigureAwait(false);

            if (result.Issue is { } updateIssue)
            {
                return Failure(updateIssue.Detail, ToCapabilityState(UpdateContract, updateIssue));
            }
        }
        else
        {
            var request = new CreateStudioPackageDraftRequest
            {
                PackageKey = BuildPackageKey(state),
                Envelope = envelope
            };
            result = await _client.CreatePackageDraftAsync(request, cancellationToken).ConfigureAwait(false);

            if (result.Issue is { } createIssue)
            {
                return Failure(createIssue.Detail, ToCapabilityState(CreateContract, createIssue));
            }
        }

        var mapped = ToEditorState(result.Data!);
        return new StudioAppCommandResult(true, $"Saved app draft ({result.Data!.PackageKey}).", mapped);
    }

    public async Task<StudioAppCommandResult> ValidateAsync(
        StudioAppEditorState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.DraftId is null)
        {
            return Failure("Save the app draft before running server validation.");
        }

        var result = await _client.ValidatePackageDraftAsync(state.DraftId.Value, cancellationToken).ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            return Failure(issue.Detail, ToCapabilityState(ValidateContract, issue));
        }

        var validation = ToValidationView(result.Data!);
        var message = validation.IsValid
            ? "Server validation passed."
            : $"Server reported {validation.Issues.Count} validation issue(s).";
        return new StudioAppCommandResult(validation.IsValid, message, state, validation);
    }

    public async Task<StudioAppCommandResult> PublishAsync(
        StudioAppEditorState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        var readiness = StudioAppPackageMapper.EvaluatePublishReadiness(state);
        if (!readiness.CanPublish)
        {
            return Failure($"Resolve before publish: {string.Join(" ", readiness.UnmetRequirements)}");
        }

        if (state.DraftId is null)
        {
            return Failure("Save the app draft before publishing.");
        }

        // Saving a content version freezes the current draft as an immutable version. Reopened edits
        // create a new draft generation and a new version on the next save, so the published version is
        // never mutated in place.
        var versionResult = await _client
            .SaveContentVersionAsync(
                state.DraftId.Value,
                new SaveStudioContentVersionRequest { ChangeNote = $"Publish {state.Title}".Trim() },
                cancellationToken)
            .ConfigureAwait(false);

        if (versionResult.Issue is { } versionIssue)
        {
            return Failure(versionIssue.Detail, ToCapabilityState(SaveVersionContract, versionIssue));
        }

        var version = versionResult.Data!;
        var publishRequest = new CreateStudioPublicationRequest
        {
            Intent = new StudioPublicationIntent
            {
                Visibility = state.Visibility,
                Embed = state.EmbedEnabled
            }
        };

        var publishResult = await _client
            .CreatePublishRequestAsync(version.ItemId, version.VersionId, publishRequest, cancellationToken)
            .ConfigureAwait(false);

        if (publishResult.Issue is { } publishIssue)
        {
            return Failure(publishIssue.Detail, ToCapabilityState(PublishContract, publishIssue));
        }

        var published = state;
        published.ItemId = version.ItemId;
        published.CurrentVersionId = version.VersionId;
        published.PublishedVersion = version.VersionNumber;

        var status = publishResult.Data!.Status;
        return new StudioAppCommandResult(
            true,
            $"Publication request {status.ToString().ToLowerInvariant()} for v{version.VersionNumber}.",
            published);
    }

    public async Task<StudioAppCommandResult> PreviewAsync(
        StudioAppEditorState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.DraftId is null)
        {
            return Failure("Save the app draft before building a preview.");
        }

        var result = await _client.CreatePreviewPlanAsync(state.DraftId.Value, cancellationToken).ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            return Failure(issue.Detail, ToCapabilityState(PreviewContract, issue));
        }

        var plan = result.Data!;
        var mode = plan.Synchronous ? "inline" : "job-backed";
        var steps = plan.Steps.Count == 0 ? string.Empty : $" Steps: {string.Join(" -> ", plan.Steps)}.";
        return new StudioAppCommandResult(true, $"Preview plan ready ({mode}).{steps}", state);
    }

    public async Task<StudioAppVersionHistory> LoadVersionHistoryAsync(
        Guid itemId,
        CancellationToken cancellationToken = default)
    {
        var result = await _client.ListContentVersionsAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            return new StudioAppVersionHistory(itemId, [], ToCapabilityState(VersionsContract, issue));
        }

        var response = result.Data!;
        var maxVersion = response.Versions.Count == 0 ? 0 : response.Versions.Max(version => version.VersionNumber);
        var items = response.Versions
            .OrderByDescending(version => version.VersionNumber)
            .Select(version => new StudioAppVersionItem(
                version.VersionId,
                version.VersionNumber,
                version.ChangeNote,
                IsPublished: version.VersionNumber == maxVersion,
                IsCurrent: version.VersionNumber == maxVersion,
                version.CreatedAt))
            .ToArray();

        return new StudioAppVersionHistory(itemId, items);
    }

    public async Task<StudioAppCommandResult> ReopenAsync(
        Guid itemId,
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        var result = await _client.ReopenVersionAsync(itemId, versionId, cancellationToken).ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            return Failure(issue.Detail, ToCapabilityState(ReopenContract, issue));
        }

        // The reopened draft is a fresh editable generation cloned from the immutable version; it carries no
        // published pointer, so the next save creates a new content version rather than mutating the
        // published one (AC: reopened edits create new content versions).
        var reopened = ToEditorState(result.Data!);
        return new StudioAppCommandResult(
            true,
            "Reopened the published version as a new editable draft. Saving will create a new content version.",
            reopened);
    }

    public async Task<StudioAppCommandResult> RollbackAsync(
        Guid itemId,
        Guid targetVersionId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var request = new CreateStudioRollbackRequest
        {
            TargetVersionId = targetVersionId,
            Target = StudioRollbackPointer.Published,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason
        };

        var result = await _client.CreateRollbackRequestAsync(itemId, request, cancellationToken).ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            return Failure(issue.Detail, ToCapabilityState(RollbackContract, issue));
        }

        return new StudioAppCommandResult(true, "Rolled the published app pointer back to the selected version.");
    }

    private static StudioPackageEnvelope BuildEnvelope(StudioAppEditorState state) =>
        new()
        {
            Family = StudioPackageFamily.App,
            SchemaVersion = StudioAppPackageMapper.SchemaVersion,
            Format = "app.package",
            PublicationIntent = new StudioPublicationIntent
            {
                Visibility = state.Visibility,
                Embed = state.EmbedEnabled
            },
            Body = StudioAppPackageMapper.BuildEnvelopeBody(state)
        };

    private static string BuildPackageKey(StudioAppEditorState state)
    {
        var slug = new string((state.Title ?? string.Empty)
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray())
            .Trim('-');

        if (slug.Length > 48)
        {
            slug = slug[..48].Trim('-');
        }

        return string.IsNullOrEmpty(slug) ? "studio-app" : $"studio-app-{slug}";
    }

    private static StudioAppEditorState ToEditorState(StudioPackageDraft draft)
    {
        // The server is the source of truth for identity/generation. The authored body
        // (title/summary/pages/actions/share-policy) is rehydrated from the draft's app.package envelope so
        // a reloaded or reopened draft renders its real content rather than a blank scaffold. A reopened
        // draft carries a baseVersionId but no published pointer, so editing + saving creates a new content
        // version instead of mutating the published one.
        var state = StudioAppPackageMapper.CreateTemplate();
        state.DraftId = draft.DraftId;
        state.ItemId = draft.ItemId == Guid.Empty ? null : draft.ItemId;
        state.Generation = draft.Generation;

        StudioAppPackageMapper.ApplyEnvelopeBody(state, draft.Envelope.Body);

        if (draft.Envelope.PublicationIntent is { } intent)
        {
            if (!string.IsNullOrWhiteSpace(intent.Visibility))
            {
                state.Visibility = intent.Visibility!;
            }

            if (intent.Embed is { } embed)
            {
                state.EmbedEnabled = embed;
            }
        }

        return state;
    }

    private static StudioAppValidationView ToValidationView(StudioValidationSummary summary)
    {
        var isValid = summary.Status is StudioPackageValidationStatus.Valid or StudioPackageValidationStatus.Warning;
        var issues = summary.Diagnostics
            .Select(diagnostic => new StudioAppValidationItem(
                diagnostic.Severity.ToString(),
                diagnostic.Code,
                diagnostic.Path,
                diagnostic.Message))
            .ToArray();
        return new StudioAppValidationView(isValid, issues);
    }

    private static StudioAppCapabilityState ToCapabilityState(string contract, StudioEndpointIssue issue) =>
        new(Surface, issue.State, contract, issue.Detail);

    private static StudioAppCommandResult Failure(string message, StudioAppCapabilityState? issue = null) =>
        new(false, message, Issue: issue);
}
