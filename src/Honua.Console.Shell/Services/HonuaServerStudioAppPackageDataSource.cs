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
    private const string GenerateContract = "POST /api/v1/studio/app-packages/generate";

    private readonly IStudioPackageLifecycleClient _client;
    private readonly IStudioAppGenerationClient? _generation;

    // The generation client is optional: a lifecycle-only construction (e.g. a unit test exercising the
    // save/validate/publish lifecycle) leaves the from-prompt surface unbound rather than fabricated; the DI
    // path injects both and the from-prompt page only calls GenerateAsync, which surfaces an honest
    // missing-binding when no generation client was composed.
    public HonuaServerStudioAppPackageDataSource(
        IStudioPackageLifecycleClient client,
        IStudioAppGenerationClient? generation = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _generation = generation;
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

        // Server omits an empty version list as JSON null; coalesce before LINQ.
        var versions = result.Data!.Versions ?? [];
        var maxVersion = versions.Count == 0 ? 0 : versions.Max(version => version.VersionNumber);
        var items = versions
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

    public async Task<StudioAppGenerationOutcome> GenerateAsync(
        StudioAppEditorState currentState,
        StudioAppGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentState);
        ArgumentNullException.ThrowIfNull(request);

        // No generation client was composed (e.g. lifecycle-only construction in a unit test): the
        // generation surface is unbound, not fabricated.
        if (_generation is null)
        {
            return StudioAppGenerationOutcome.Blocked(new StudioAppCapabilityState(
                Surface,
                "Missing binding",
                GenerateContract,
                "The app generation endpoint is not bound. Configure Honua:Server:BaseUrl so the app-packages/generate endpoint is reachable."));
        }

        // A refine turn ships the current studio-app/v1 body so the server edits it; a first turn ships null
        // (fresh). The body is the same round-trip surface BuildEnvelopeBody/ApplyEnvelopeBody use. The probe
        // keys off real authored intent — a bound page, an extra/renamed page, any action, a summary, or a
        // non-default title — so the default Console scaffold (one blank "Home" page, "Untitled app") still
        // requests fresh generation rather than asking the server to refine an empty template.
        var hasApp = currentState.Pages.Any(page => !string.IsNullOrWhiteSpace(page.ContentBinding))
            || currentState.Pages.Count > 1
            || currentState.Actions.Count > 0
            || !string.IsNullOrWhiteSpace(currentState.Summary)
            || (!string.IsNullOrWhiteSpace(currentState.Title)
                && !string.Equals(currentState.Title, "Untitled app", StringComparison.Ordinal));

        var wire = new GenerateAppPackageRequest
        {
            Prompt = request.Prompt,
            Provider = request.Provider,
            Model = request.Model,
            Package = hasApp ? StudioAppPackageMapper.BuildEnvelopeBody(currentState) : null,
            Conversation = request.Conversation
                .Select(turn => new AppGenerationTurn { Role = turn.Role, Content = turn.Content })
                .ToArray(),
            Answers = request.Answers
                .Select(answer => new AppGenerationAnswer { QuestionId = answer.QuestionId, OptionId = answer.OptionId })
                .ToArray()
        };

        var result = await _generation.GenerateAppAsync(wire, cancellationToken).ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            // A 404/501 means this server lacks the app generation contract: AI is simply off here (honest
            // "unsupported"), not a missing server binding. Other issues block the surface so the page shows
            // the shared blocked state.
            if (issue.StatusCode is 404 or 501)
            {
                return new StudioAppGenerationOutcome
                {
                    Status = StudioAppGenerationStatuses.Unsupported,
                    Rationale = "This server does not offer AI app generation yet."
                };
            }

            return StudioAppGenerationOutcome.Blocked(ToCapabilityState(GenerateContract, issue));
        }

        return MapGeneration(currentState, result.Data!);
    }

    private static StudioAppGenerationOutcome MapGeneration(
        StudioAppEditorState currentState,
        AppGenerationResult result)
    {
        // The server serializes empty collections as null (System.Text.Json source-gen omits empty arrays),
        // so every collection on the deserialized result may be null. Guard each before LINQ — otherwise a
        // perfectly valid 'generated' result (which carries no clarifications/unmapped) throws and freezes the
        // page (mirrors the map/dashboard clients' defensive mapping).
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

        StudioAppEditorState? state = null;
        var generated = string.Equals(result.Status, StudioAppGenerationStatuses.Generated, StringComparison.Ordinal);
        if (generated && result.Package is { } package)
        {
            // The server returns the SAME studio-app/v1 body the console authors/round-trips, so hydrate the
            // editor with the EXISTING ApplyEnvelopeBody — no generation-specific mapper (this is the clean
            // case the map family didn't have). Server-owned identity/generation on currentState is preserved.
            StudioAppPackageMapper.ApplyEnvelopeBody(currentState, package);
            state = currentState;

            if (result.Validation is { IsValid: false })
            {
                warnings.AddRange((result.Validation.Failures ?? []).Select(failure => failure.Message));
            }
        }

        return new StudioAppGenerationOutcome
        {
            Status = NormalizeGenerationStatus(result.Status),
            State = state,
            Rationale = result.Rationale ?? string.Empty,
            Clarifications = clarifications,
            Warnings = warnings,
            CapabilityState = result.CapabilityState is null
                ? null
                : new StudioAppGenerationCapability(
                    result.CapabilityState.Name,
                    result.CapabilityState.State,
                    result.CapabilityState.Reason),
            Provider = result.Provider,
            Model = result.Model
        };
    }

    private static string NormalizeGenerationStatus(string? status) => status switch
    {
        StudioAppGenerationStatuses.Generated => StudioAppGenerationStatuses.Generated,
        StudioAppGenerationStatuses.NeedsClarification => StudioAppGenerationStatuses.NeedsClarification,
        StudioAppGenerationStatuses.Unsupported => StudioAppGenerationStatuses.Unsupported,
        StudioAppGenerationStatuses.Refused => StudioAppGenerationStatuses.Refused,
        _ => StudioAppGenerationStatuses.Error
    };

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
