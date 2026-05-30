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

    private readonly IStudioPackageLifecycleClient _client;

    public HonuaServerStudioDashboardPackageDataSource(IStudioPackageLifecycleClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
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

        return new StudioDashboardCommandResult(isValid, message, state);
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

        var target = versions.Data!.Versions
            .FirstOrDefault(candidate => candidate.VersionNumber == version)
            ?? versions.Data!.Versions.OrderByDescending(candidate => candidate.VersionNumber).FirstOrDefault();

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
