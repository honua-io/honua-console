using System.Globalization;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
// The server wire enum, not an editor-catalog enum.
using StudioPackageFamily = Honua.Console.Contracts.StudioPackageFamily;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Studio dashboard-builder data source bound to the real honua-server Studio package lifecycle and the
/// dashboard publication registry (honua-server#1180/#1181/#1183) through the
/// <see cref="IStudioPackageLifecycleClient"/> shim. Drafts are created/updated as
/// <c>studio_dashboard_package.v1</c> envelopes, validated server-side, saved as immutable content
/// versions, routed to the publication contract, and reopened/rolled back through the live lifecycle;
/// there is no in-memory dashboard data in the merged result (Console Patterns Charter section 11).
/// Endpoint issues (missing permission, unsupported verb, conflict, transport) surface as explicit
/// capability states instead of throwing or fabricating data. Mirrors
/// <see cref="HonuaServerStudioAppPackageDataSource"/>.
/// </summary>
public sealed class HonuaServerStudioDashboardPackageDataSource : IStudioDashboardPackageDataSource
{
    private const string Surface = "Dashboard builder";
    private const string LoadContract = "GET /api/v1/studio/package-drafts/{draftId}";
    private const string CreateContract = "POST /api/v1/studio/package-drafts";
    private const string UpdateContract = "PUT /api/v1/studio/package-drafts/{draftId}";
    private const string ValidateContract = "POST /api/v1/studio/package-drafts/{draftId}/validate";
    private const string SaveVersionContract = "POST /api/v1/studio/package-drafts/{draftId}/content-versions";
    private const string PublishContract =
        "POST /api/v1/studio/content-items/{itemId}/versions/{versionId}/publish-requests";
    private const string ReopenContract =
        "POST /api/v1/studio/content-items/{itemId}/versions/{versionId}/reopen";

    // honua-server validates publication visibility against {personal, team, organization, public};
    // a workspace-scoped dashboard maps to the "team" visibility.
    private const string DefaultPublicationRoute = "/share/dashboard";
    private const string DefaultPublicationVisibility = "team";

    // honua-server's Studio API exposes draft/version/publish lifecycle but no "list all dashboard
    // packages" endpoint yet, so the workspace list is empty against a live server and the editor opens
    // a fresh draft (New) or an existing draft by id. Surfaced as an informational capability state so
    // the surface is honest about the gap rather than fabricating a list.
    private static readonly StudioDashboardCapabilityState ListingUnavailable = new(
        Surface,
        "Unsupported",
        "GET /api/v1/studio/content-items",
        "The honua-server Studio API does not yet expose a dashboard package listing endpoint. Create a new dashboard or open one by id; saved versions persist on the server.");

    private const string GenerateContract = "POST /api/v1/console/publications/generate";

    private readonly IStudioPackageLifecycleClient _client;

    // Dashboard generation shares the content publications/generate endpoint with reports (dispatched on
    // kind), so it speaks the content-publication client rather than the Studio package lifecycle. Optional
    // so unit tests exercising the save/validate/publish/reopen lifecycle can construct the data source with
    // just the lifecycle client; the DI path injects both and the from-prompt page only calls GenerateAsync
    // through the server-bound composition.
    private readonly IHonuaContentPublicationClient? _publicationClient;

    public HonuaServerStudioDashboardPackageDataSource(
        IStudioPackageLifecycleClient client,
        IHonuaContentPublicationClient? publicationClient = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _publicationClient = publicationClient;
    }

    public Task<StudioDashboardWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
        // No server list contract — the editor binds live, but the package list stays empty with an
        // explicit capability state rather than a mock list.
        Task.FromResult(new StudioDashboardWorkspace([], [ListingUnavailable]));

    public async Task<StudioDashboardEditorLoad> LoadAsync(
        string? dashboardId,
        CancellationToken cancellationToken = default)
    {
        // A brand-new dashboard opens a blank Console-owned authoring scaffold; the server draft is
        // created on first save. An existing dashboard is opened by its draft id and loaded live.
        if (string.IsNullOrWhiteSpace(dashboardId))
        {
            return new StudioDashboardEditorLoad(StudioDashboardPackageMapper.CreateTemplate(), []);
        }

        if (!Guid.TryParse(dashboardId, out var draftId))
        {
            return new StudioDashboardEditorLoad(
                null,
                [
                    new StudioDashboardCapabilityState(
                        Surface,
                        "Unsupported",
                        LoadContract,
                        $"'{dashboardId}' is not a server draft id. Open a dashboard from its draft id.")
                ]);
        }

        var result = await _client.GetPackageDraftAsync(draftId, cancellationToken).ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            return new StudioDashboardEditorLoad(null, [ToCapabilityState(LoadContract, issue)]);
        }

        return new StudioDashboardEditorLoad(ToEditorState(result.Data!), []);
    }

    public async Task<StudioDashboardCommandResult> SaveDraftAsync(
        StudioDashboardEditorState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.IsPublished && !state.IsExistingDraft)
        {
            return Failure("This dashboard version is published. Reopen it as a draft before editing.");
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

        var mapped = ApplyDraftIdentity(state, result.Data!);
        return new StudioDashboardCommandResult(true, $"Saved dashboard draft ({result.Data!.PackageKey}).", mapped);
    }

    public async Task<StudioDashboardCommandResult> ValidateAsync(
        StudioDashboardEditorState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.DraftId is null)
        {
            return Failure("Save the dashboard draft before running server validation.");
        }

        var result = await _client.ValidatePackageDraftAsync(state.DraftId.Value, cancellationToken).ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            return Failure(issue.Detail, ToCapabilityState(ValidateContract, issue));
        }

        var summary = result.Data!;
        var isValid = summary.Status is StudioPackageValidationStatus.Valid or StudioPackageValidationStatus.Warning;
        string message;
        if (isValid)
        {
            message = "Server validation passed.";
        }
        else
        {
            var details = string.Join(
                " ",
                summary.Diagnostics
                    .Where(diagnostic => diagnostic.Severity is StudioPackageDiagnosticSeverity.Error or StudioPackageDiagnosticSeverity.Blocker)
                    .Select(diagnostic => $"{diagnostic.Path}: {diagnostic.Message}"));
            message = $"Server reported {summary.Diagnostics.Count} validation issue(s). {details}".Trim();
        }

        return new StudioDashboardCommandResult(isValid, message, state, Diagnostics: summary.Diagnostics);
    }

    public async Task<StudioDashboardCommandResult> PublishAsync(
        StudioDashboardEditorState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        var readiness = StudioDashboardPublishEvaluator.Evaluate(state);
        if (!readiness.CanPublish)
        {
            return Failure($"Resolve before publish: {string.Join(" ", readiness.UnmetRequirements)}");
        }

        if (state.DraftId is null)
        {
            return Failure("Save the dashboard draft before publishing.");
        }

        // Saving a content version freezes the current draft as an immutable version; the publish request
        // then targets that version so the published version is never mutated in place.
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
                Route = DefaultPublicationRoute,
                Visibility = DefaultPublicationVisibility
            }
        };

        var publishResult = await _client
            .CreatePublishRequestAsync(version.ItemId, version.VersionId, publishRequest, cancellationToken)
            .ConfigureAwait(false);

        if (publishResult.Issue is { } publishIssue)
        {
            return Failure(publishIssue.Detail, ToCapabilityState(PublishContract, publishIssue));
        }

        state.ItemId = version.ItemId;
        state.DashboardId = version.ItemId.ToString();
        state.CurrentVersionId = version.VersionId;
        state.PublishedVersion = version.VersionNumber;
        state.Version = version.VersionNumber;
        state.Status = StudioDashboardStatuses.Published;

        var status = publishResult.Data!.Status;
        return new StudioDashboardCommandResult(
            true,
            $"Publication request {status.ToString().ToLowerInvariant()} for v{version.VersionNumber}.",
            state);
    }

    public async Task<StudioDashboardCommandResult> ReopenAsync(
        string dashboardId,
        int version,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dashboardId);

        if (!Guid.TryParse(dashboardId, out var itemId))
        {
            return Failure($"'{dashboardId}' is not a server content-item id; cannot reopen.");
        }

        return await ReopenCoreAsync(itemId, version, cancellationToken).ConfigureAwait(false);
    }

    private async Task<StudioDashboardCommandResult> ReopenCoreAsync(
        Guid itemId,
        int version,
        CancellationToken cancellationToken)
    {
        // Resolve the version id for the published version number from the live version list, then reopen
        // it as a fresh editable draft.
        var versions = await _client.ListContentVersionsAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (versions.Issue is { } listIssue)
        {
            return Failure(listIssue.Detail, ToCapabilityState("GET /api/v1/studio/content-items/{itemId}/versions", listIssue));
        }

        // Server omits an empty version list as JSON null; coalesce before LINQ.
        var versionList = versions.Data!.Versions ?? [];
        var target = versionList
            .FirstOrDefault(candidate => candidate.VersionNumber == version)
            ?? versionList.OrderByDescending(candidate => candidate.VersionNumber).FirstOrDefault();

        if (target is null)
        {
            return Failure($"No saved versions found for dashboard {itemId} to reopen.");
        }

        var reopened = await _client.ReopenVersionAsync(itemId, target.VersionId, cancellationToken).ConfigureAwait(false);
        if (reopened.Issue is { } reopenIssue)
        {
            return Failure(reopenIssue.Detail, ToCapabilityState(ReopenContract, reopenIssue));
        }

        var state = ToEditorState(reopened.Data!);
        state.ReopenedFromVersion = version;
        state.Status = StudioDashboardStatuses.Draft;
        state.PublishedVersion = version;
        return new StudioDashboardCommandResult(true, $"Reopened v{version} as a new draft.", state);
    }

    public async Task<StudioDashboardGenerationOutcome> GenerateAsync(
        StudioDashboardEditorState currentState,
        StudioDashboardGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentState);
        ArgumentNullException.ThrowIfNull(request);

        // No content-publication client was composed (e.g. lifecycle-only construction in a unit test): the
        // generation surface is unbound, not fabricated.
        if (_publicationClient is null)
        {
            return StudioDashboardGenerationOutcome.Blocked(new StudioDashboardCapabilityState(
                Surface,
                "Missing binding",
                GenerateContract,
                "The dashboard generation endpoint is not bound. Configure Honua:Server:BaseUrl so the content publications/generate endpoint is reachable."));
        }

        // A refine turn ships the current dashboard document so the server edits it; a first turn ships null
        // (fresh). The "has content" probe keys off any authored intent so a blank scaffold requests fresh
        // generation. The document round-trips through the same dashboard package body the editor authors.
        var refine = currentState.Panels.Count > 0
            || currentState.Bindings.Count > 0
            || !string.IsNullOrWhiteSpace(currentState.Title)
            || !string.IsNullOrWhiteSpace(currentState.Narrative);

        var wire = new GenerateDashboardContentRequest
        {
            Kind = HonuaContentPublicationKinds.Dashboard,
            Prompt = request.Prompt,
            Provider = request.Provider,
            Model = request.Model,
            Document = refine ? StudioDashboardPackageMapper.BuildEnvelopeBody(currentState) : null,
            Conversation = request.Conversation
                .Select(turn => new HonuaReportGenerationTurn { Role = turn.Role, Content = turn.Content })
                .ToArray(),
            Answers = request.Answers
                .Select(answer => new HonuaReportGenerationAnswer { QuestionId = answer.QuestionId, OptionId = answer.OptionId })
                .ToArray()
        };

        var result = await _publicationClient.GenerateDashboardAsync(wire, cancellationToken).ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            // A 404/501 means this server lacks the dashboard generation contract: AI is simply off here
            // (honest "unsupported"), not a missing server binding. Other issues block the surface.
            if (issue.StatusCode is 404 or 501)
            {
                return new StudioDashboardGenerationOutcome
                {
                    Status = StudioDashboardGenerationStatuses.Unsupported,
                    Rationale = "This server does not offer AI dashboard generation yet."
                };
            }

            return StudioDashboardGenerationOutcome.Blocked(ToCapabilityState(GenerateContract, issue));
        }

        return MapGeneration(result.Data!);
    }

    private static StudioDashboardGenerationOutcome MapGeneration(HonuaReportGenerationResult result)
    {
        // The server serializes empty collections as null (source-gen omits empty arrays), so guard each
        // collection before LINQ — a valid 'generated' result carries no clarifications/unmapped.
        var warnings = (result.UnmappedRequests ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => $"No matching binding/panel for: {item}")
            .ToList();

        var clarifications = (result.Clarifications ?? [])
            .Select(question => new StudioConversationClarification(
                question.Id,
                string.IsNullOrWhiteSpace(question.Prompt) ? question.Kind : question.Prompt,
                question.Reason ?? string.Empty,
                (question.Choices ?? [])
                    .Select(choice => new StudioConversationChoice(choice.Id, choice.Label, choice.Effect))
                    .ToArray()))
            .ToArray();

        StudioDashboardEditorState? state = null;
        if (string.Equals(result.Status, StudioDashboardGenerationStatuses.Generated, StringComparison.Ordinal)
            && result.Document is { } document)
        {
            // A generated document becomes a fresh, unsaved draft the operator reviews and publishes.
            state = StudioDashboardPackageMapper.CreateTemplate();
            var mapperNotes = StudioDashboardDocument.ApplyGeneratedDocument(state, document);
            warnings.AddRange(mapperNotes);
        }

        return new StudioDashboardGenerationOutcome
        {
            Status = NormalizeGenerationStatus(result.Status),
            State = state,
            Rationale = result.Rationale ?? string.Empty,
            Clarifications = clarifications,
            Warnings = warnings,
            CapabilityState = result.CapabilityState is null
                ? null
                : new StudioDashboardGenerationCapability(
                    result.CapabilityState.Name,
                    result.CapabilityState.State,
                    result.CapabilityState.Reason),
            Provider = result.Provider,
            Model = result.Model
        };
    }

    private static string NormalizeGenerationStatus(string? status) => status switch
    {
        StudioDashboardGenerationStatuses.Generated => StudioDashboardGenerationStatuses.Generated,
        StudioDashboardGenerationStatuses.NeedsClarification => StudioDashboardGenerationStatuses.NeedsClarification,
        StudioDashboardGenerationStatuses.Unsupported => StudioDashboardGenerationStatuses.Unsupported,
        StudioDashboardGenerationStatuses.Refused => StudioDashboardGenerationStatuses.Refused,
        _ => StudioDashboardGenerationStatuses.Error
    };

    private static StudioDashboardCapabilityState ToCapabilityState(string contract, HonuaAdminEndpointIssue issue) =>
        new(
            Surface,
            issue.State,
            issue.Contract ?? contract,
            issue.StatusCode is null
                ? issue.Detail
                : $"{issue.Detail} HTTP {issue.StatusCode.Value.ToString(CultureInfo.InvariantCulture)}.");

    private static StudioPackageEnvelope BuildEnvelope(StudioDashboardEditorState state) =>
        new()
        {
            Family = StudioPackageFamily.Dashboard,
            SchemaVersion = StudioDashboardPackageMapper.SchemaVersion,
            Format = StudioDashboardPackageMapper.EnvelopeFormat,
            Bindings = StudioDashboardPackageMapper.BuildEnvelopeBindings(state)
                .Select(binding => new StudioPackageBinding
                {
                    Key = binding["key"]!.GetValue<string>(),
                    Kind = binding["kind"]!.GetValue<string>(),
                    Ref = binding["ref"]!.GetValue<string>()
                })
                .ToArray(),
            PublicationIntent = new StudioPublicationIntent
            {
                Route = DefaultPublicationRoute,
                Visibility = DefaultPublicationVisibility
            },
            Body = StudioDashboardPackageMapper.BuildEnvelopeBody(state)
        };

    private static string BuildPackageKey(StudioDashboardEditorState state)
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

        return string.IsNullOrEmpty(slug) ? "studio-dashboard" : $"studio-dashboard-{slug}";
    }

    // The server is the source of truth for identity/generation. The editor body is preserved client-side
    // (the server dashboard body projection is not read back in this binding); this maps server identity
    // onto the in-flight editor state so subsequent saves are concurrency-safe and publish targets the
    // right draft/item.
    private static StudioDashboardEditorState ApplyDraftIdentity(StudioDashboardEditorState state, StudioPackageDraft draft)
    {
        state.DraftId = draft.DraftId;
        state.ItemId = draft.ItemId == Guid.Empty ? null : draft.ItemId;
        state.DashboardId = draft.ItemId == Guid.Empty ? state.DashboardId : draft.ItemId.ToString();
        state.Generation = draft.Generation;
        state.ETag = draft.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return state;
    }

    private static StudioDashboardEditorState ToEditorState(StudioPackageDraft draft)
    {
        var state = StudioDashboardPackageMapper.CreateTemplate();
        state.DraftId = draft.DraftId;
        state.ItemId = draft.ItemId == Guid.Empty ? null : draft.ItemId;
        state.DashboardId = draft.ItemId == Guid.Empty ? null : draft.ItemId.ToString();
        state.Generation = draft.Generation;
        state.ETag = draft.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return state;
    }

    private static StudioDashboardCapabilityState ToCapabilityState(string contract, StudioEndpointIssue issue) =>
        new(Surface, issue.State, contract, issue.Detail);

    private static StudioDashboardCommandResult Failure(string message, StudioDashboardCapabilityState? issue = null) =>
        new(false, message, Issue: issue);
}
