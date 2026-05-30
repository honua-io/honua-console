using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using StudioPackageFamily = Honua.Console.Contracts.StudioPackageFamily;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Host-independent coverage for the server-bound dashboard data source and its envelope projection. Uses
/// a recording <see cref="IStudioPackageLifecycleClient"/> (no DOM, no real server) to assert the
/// save/validate/publish/reopen lifecycle drives the real lifecycle contract: a fresh dashboard creates a
/// draft, an existing dashboard updates with the concurrency generation, publish saves an immutable
/// version then a publish request, reopen resolves the version id and reopens it, and endpoint issues
/// surface as capability states instead of throwing or fabricating data.
/// </summary>
public sealed class StudioDashboardPackageDataSourceTests
{
    [Fact]
    public void EnvelopeBodyProjectsBindingsPanelsNarrativeAndPreview()
    {
        var state = ReadyDashboard();
        state.Narrative = "Operational context.";
        state.PreviewBreakpoint = StudioDashboardBreakpoints.Narrow;

        var body = StudioDashboardPackageMapper.BuildEnvelopeBody(state);

        // The body self-describes with the dashboard package format; the envelope-level schemaVersion is
        // the server's "1.0" family schema version, asserted separately on the create request below.
        Assert.Equal(StudioDashboardPackageMapper.EnvelopeFormat, body.GetProperty("schemaVersion").GetString());
        Assert.Equal("Operational context.", body.GetProperty("narrative").GetString());
        Assert.Equal("narrow", body.GetProperty("responsivePreview").GetProperty("breakpoint").GetString());

        var panels = body.GetProperty("panels");
        Assert.Equal(1, panels.GetArrayLength());
        Assert.Equal("vega-lite", panels[0].GetProperty("chartSpecFormat").GetString());
        // The chart spec is stored as a structured Vega-Lite object, not an escaped string.
        Assert.Equal(JsonValueKind.Object, panels[0].GetProperty("chartSpec").ValueKind);
        Assert.Contains("vega-lite", panels[0].GetProperty("chartSpec").GetProperty("$schema").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnvelopeBindingsDropBlankAliasesAndCarryVersionPin()
    {
        var state = ReadyDashboard();
        state.Bindings.Add(new StudioDashboardBindingEditor { Alias = "", ContentRef = "content:ignored" });

        var bindings = StudioDashboardPackageMapper.BuildEnvelopeBindings(state);

        var binding = Assert.Single(bindings);
        Assert.Equal("requests", binding["key"]!.GetValue<string>());
        Assert.Equal("content:service-requests", binding["ref"]!.GetValue<string>());
        Assert.Equal("v5", binding["metadata"]!["versionPin"]!.GetValue<string>());
    }

    [Fact]
    public async Task SaveDraft_NewDashboard_CreatesServerDraftAndCapturesIdentity()
    {
        var client = new RecordingLifecycleClient();
        var dataSource = new HonuaServerStudioDashboardPackageDataSource(client);
        var state = ReadyDashboard();
        state.DashboardId = null;

        var result = await dataSource.SaveDraftAsync(state);

        Assert.True(result.Succeeded);
        Assert.Equal(1, client.CreateCount);
        Assert.Equal(0, client.UpdateCount);
        Assert.NotNull(result.State!.DraftId);
        Assert.Equal(1, result.State.Generation);
        // The created draft envelope carries the dashboard family and schema version.
        Assert.Equal(StudioPackageFamily.Dashboard, client.LastCreate!.Envelope.Family);
        Assert.Equal(StudioDashboardPackageMapper.SchemaVersion, client.LastCreate.Envelope.SchemaVersion);
        Assert.Equal(StudioDashboardPackageMapper.EnvelopeFormat, client.LastCreate.Envelope.Format);
    }

    [Fact]
    public async Task SaveDraft_ExistingDraft_UpdatesWithConcurrencyGeneration()
    {
        var client = new RecordingLifecycleClient();
        var dataSource = new HonuaServerStudioDashboardPackageDataSource(client);

        var created = await dataSource.SaveDraftAsync(ReadyDashboard());
        var reopenedState = created.State!;
        reopenedState.Title = "Operations dashboard v2";

        var updated = await dataSource.SaveDraftAsync(reopenedState);

        Assert.True(updated.Succeeded);
        Assert.Equal(1, client.UpdateCount);
        // The update carried the generation captured from the create response (optimistic concurrency).
        Assert.Equal(1, client.LastUpdate!.Generation);
        Assert.Equal(2, updated.State!.Generation);
    }

    [Fact]
    public async Task SaveDraft_ConflictResponse_SurfacesConflictCapabilityState()
    {
        var client = new RecordingLifecycleClient { ConflictOnUpdate = true };
        var dataSource = new HonuaServerStudioDashboardPackageDataSource(client);

        var created = await dataSource.SaveDraftAsync(ReadyDashboard());
        var conflict = await dataSource.SaveDraftAsync(created.State!);

        Assert.False(conflict.Succeeded);
        Assert.NotNull(conflict.Issue);
        Assert.Equal("Conflict", conflict.Issue!.State);
    }

    [Fact]
    public async Task Publish_SavesVersionThenPublishRequest_AndMarksPublished()
    {
        var client = new RecordingLifecycleClient();
        var dataSource = new HonuaServerStudioDashboardPackageDataSource(client);

        var created = await dataSource.SaveDraftAsync(ReadyDashboard());
        var published = await dataSource.PublishAsync(created.State!);

        Assert.True(published.Succeeded);
        Assert.Equal(1, client.SaveVersionCount);
        Assert.Equal(1, client.PublishCount);
        Assert.Equal(StudioDashboardStatuses.Published, published.State!.Status);
        Assert.True(published.State.IsPublished);
        Assert.Equal(1, published.State.PublishedVersion);
        Assert.NotNull(published.State.ItemId);
    }

    [Fact]
    public async Task Publish_BeforeSave_GatesWithoutCallingServer()
    {
        var client = new RecordingLifecycleClient();
        var dataSource = new HonuaServerStudioDashboardPackageDataSource(client);
        var state = ReadyDashboard(); // ready content but never saved -> no DraftId

        var result = await dataSource.PublishAsync(state);

        Assert.False(result.Succeeded);
        Assert.Equal(0, client.SaveVersionCount);
        Assert.Equal(0, client.PublishCount);
        Assert.Contains("Save the dashboard draft", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Publish_NotReady_GatesBeforeServerCalls()
    {
        var client = new RecordingLifecycleClient();
        var dataSource = new HonuaServerStudioDashboardPackageDataSource(client);
        var state = ReadyDashboard();
        state.Panels[0].VegaLiteSpec = "{\"mark\":\"bar\"}"; // no $schema -> gate

        var saved = await dataSource.SaveDraftAsync(state);
        var result = await dataSource.PublishAsync(saved.State!);

        Assert.False(result.Succeeded);
        Assert.Equal(0, client.SaveVersionCount);
        Assert.Contains("Vega-Lite", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reopen_ResolvesVersionIdAndReopensAsNewDraft()
    {
        var client = new RecordingLifecycleClient();
        var dataSource = new HonuaServerStudioDashboardPackageDataSource(client);

        var created = await dataSource.SaveDraftAsync(ReadyDashboard());
        var published = await dataSource.PublishAsync(created.State!);

        var reopened = await dataSource.ReopenAsync(published.State!.DashboardId!, published.State.PublishedVersion!.Value);

        Assert.True(reopened.Succeeded);
        Assert.Equal(1, client.ReopenCount);
        Assert.Equal(StudioDashboardStatuses.Draft, reopened.State!.Status);
        Assert.Equal(published.State.PublishedVersion, reopened.State.ReopenedFromVersion);
        // Reopen produces a fresh draft id distinct from the published draft.
        Assert.NotEqual(created.State!.DraftId, reopened.State.DraftId);
    }

    [Fact]
    public async Task GetWorkspace_ReturnsEmptyListWithUnsupportedListingState()
    {
        var dataSource = new HonuaServerStudioDashboardPackageDataSource(new RecordingLifecycleClient());

        var workspace = await dataSource.GetWorkspaceAsync();

        Assert.Empty(workspace.Packages);
        var state = Assert.Single(workspace.CapabilityStates);
        Assert.Equal("Unsupported", state.State);
    }

    [Fact]
    public async Task Load_NonGuidId_SurfacesUnsupportedInsteadOfThrowing()
    {
        var dataSource = new HonuaServerStudioDashboardPackageDataSource(new RecordingLifecycleClient());

        var load = await dataSource.LoadAsync("not-a-guid");

        Assert.False(load.HasEditor);
        var state = Assert.Single(load.CapabilityStates);
        Assert.Equal("Unsupported", state.State);
    }

    private static StudioDashboardEditorState ReadyDashboard()
    {
        var state = new StudioDashboardEditorState { DashboardId = "dashboard-1", Title = "Operations dashboard" };
        state.Bindings.Add(new StudioDashboardBindingEditor
        {
            Alias = "requests",
            ContentRef = "content:service-requests",
            VersionPin = "v5"
        });
        state.Panels.Add(new StudioDashboardPanelEditor
        {
            Title = "Requests by district",
            Kind = StudioDashboardPanelKinds.Chart,
            BindingAlias = "requests",
            VegaLiteSpec = StudioDashboardChartSpec.DefaultBarChart("district", "request_count")
        });
        return state;
    }

    /// <summary>
    /// In-memory recording lifecycle client. Models the real server's draft/version/publish/reopen state
    /// machine closely enough to exercise the data source's contract (identity capture, optimistic
    /// concurrency, version resolution) without a server.
    /// </summary>
    private sealed class RecordingLifecycleClient : IStudioPackageLifecycleClient
    {
        private StudioPackageDraft? _draft;
        private readonly List<StudioContentVersion> _versions = [];

        public Uri BaseUri { get; } = new("https://honua.test");

        public bool ConflictOnUpdate { get; init; }

        public int CreateCount { get; private set; }
        public int UpdateCount { get; private set; }
        public int SaveVersionCount { get; private set; }
        public int PublishCount { get; private set; }
        public int ReopenCount { get; private set; }

        public CreateStudioPackageDraftRequest? LastCreate { get; private set; }
        public UpdateStudioPackageDraftRequest? LastUpdate { get; private set; }

        public Task<StudioEndpointResult<StudioPackageFamilyCapabilities>> ListPackageFamiliesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(StudioEndpointResult<StudioPackageFamilyCapabilities>.FromData(new StudioPackageFamilyCapabilities()));

        public Task<StudioEndpointResult<StudioPackageDraft>> CreatePackageDraftAsync(
            CreateStudioPackageDraftRequest request,
            CancellationToken cancellationToken = default)
        {
            CreateCount++;
            LastCreate = request;
            _draft = new StudioPackageDraft
            {
                DraftId = Guid.NewGuid(),
                ItemId = Guid.NewGuid(),
                PackageKey = request.PackageKey,
                Family = request.Envelope.Family,
                Envelope = request.Envelope,
                Generation = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            return Task.FromResult(StudioEndpointResult<StudioPackageDraft>.FromData(_draft));
        }

        public Task<StudioEndpointResult<StudioPackageDraft>> GetPackageDraftAsync(
            Guid draftId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_draft is not null && _draft.DraftId == draftId
                ? StudioEndpointResult<StudioPackageDraft>.FromData(_draft)
                : NotFound<StudioPackageDraft>("GET draft"));

        public Task<StudioEndpointResult<StudioPackageDraft>> UpdatePackageDraftAsync(
            Guid draftId,
            UpdateStudioPackageDraftRequest request,
            CancellationToken cancellationToken = default)
        {
            UpdateCount++;
            LastUpdate = request;
            if (_draft is null || _draft.DraftId != draftId)
            {
                return Task.FromResult(NotFound<StudioPackageDraft>("PUT draft"));
            }

            if (ConflictOnUpdate)
            {
                return Task.FromResult(Conflict<StudioPackageDraft>("PUT draft"));
            }

            _draft = _draft with { Envelope = request.Envelope, Generation = _draft.Generation + 1 };
            return Task.FromResult(StudioEndpointResult<StudioPackageDraft>.FromData(_draft));
        }

        public Task<StudioEndpointResult<StudioValidationSummary>> ValidatePackageDraftAsync(
            Guid draftId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(StudioEndpointResult<StudioValidationSummary>.FromData(new StudioValidationSummary
            {
                Status = StudioPackageValidationStatus.Valid
            }));

        public Task<StudioEndpointResult<StudioPreviewPlan>> CreatePreviewPlanAsync(
            Guid draftId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(StudioEndpointResult<StudioPreviewPlan>.FromData(new StudioPreviewPlan { DraftId = draftId }));

        public Task<StudioEndpointResult<StudioContentVersion>> SaveContentVersionAsync(
            Guid draftId,
            SaveStudioContentVersionRequest request,
            CancellationToken cancellationToken = default)
        {
            SaveVersionCount++;
            if (_draft is null || _draft.DraftId != draftId)
            {
                return Task.FromResult(NotFound<StudioContentVersion>("POST content-versions"));
            }

            var version = new StudioContentVersion
            {
                ItemId = _draft.ItemId,
                PackageKey = _draft.PackageKey,
                VersionId = Guid.NewGuid(),
                VersionNumber = _versions.Count + 1,
                Envelope = _draft.Envelope,
                SourceDraftId = _draft.DraftId,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _versions.Add(version);
            return Task.FromResult(StudioEndpointResult<StudioContentVersion>.FromData(version));
        }

        public Task<StudioEndpointResult<StudioContentVersionListResponse>> ListContentVersionsAsync(
            Guid itemId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(StudioEndpointResult<StudioContentVersionListResponse>.FromData(new StudioContentVersionListResponse
            {
                ItemId = itemId,
                Versions = _versions.Where(version => version.ItemId == itemId).ToArray()
            }));

        public Task<StudioEndpointResult<StudioPublicationRequest>> CreatePublishRequestAsync(
            Guid itemId,
            Guid versionId,
            CreateStudioPublicationRequest request,
            CancellationToken cancellationToken = default)
        {
            PublishCount++;
            return Task.FromResult(StudioEndpointResult<StudioPublicationRequest>.FromData(new StudioPublicationRequest
            {
                RequestId = Guid.NewGuid(),
                ItemId = itemId,
                VersionId = versionId,
                Status = StudioPublicationRequestStatus.Accepted,
                CreatedAt = DateTimeOffset.UtcNow
            }));
        }

        public Task<StudioEndpointResult<StudioPackageDraft>> ReopenVersionAsync(
            Guid itemId,
            Guid versionId,
            CancellationToken cancellationToken = default)
        {
            ReopenCount++;
            var source = _versions.FirstOrDefault(version => version.ItemId == itemId && version.VersionId == versionId);
            if (source is null)
            {
                return Task.FromResult(NotFound<StudioPackageDraft>("POST reopen"));
            }

            _draft = new StudioPackageDraft
            {
                DraftId = Guid.NewGuid(),
                ItemId = itemId,
                PackageKey = source.PackageKey,
                Family = StudioPackageFamily.Dashboard,
                Envelope = source.Envelope,
                BaseVersionId = versionId,
                Generation = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            return Task.FromResult(StudioEndpointResult<StudioPackageDraft>.FromData(_draft));
        }

        public Task<StudioEndpointResult<StudioPackageDraft>> ReopenContentVersionAsync(
            Guid itemId,
            Guid versionId,
            CancellationToken cancellationToken = default) =>
            ReopenVersionAsync(itemId, versionId, cancellationToken);

        public Task<StudioEndpointResult<StudioContentVersion>> GetContentVersionAsync(
            Guid itemId,
            Guid versionId,
            CancellationToken cancellationToken = default)
        {
            var version = _versions.FirstOrDefault(v => v.ItemId == itemId && v.VersionId == versionId);
            return Task.FromResult(version is null
                ? NotFound<StudioContentVersion>("GET /api/v1/studio/content-items/{itemId}/versions/{versionId}")
                : StudioEndpointResult<StudioContentVersion>.FromData(version));
        }

        public Task<StudioEndpointResult<StudioRollbackRequest>> RollbackAsync(
            Guid itemId,
            CreateStudioRollbackRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(StudioEndpointResult<StudioRollbackRequest>.FromData(new StudioRollbackRequest
            {
                RequestId = Guid.NewGuid(),
                ItemId = itemId,
                TargetVersionId = request.TargetVersionId,
                Target = request.Target,
                Pointers = new StudioContentItemPointers { ItemId = itemId },
                CreatedAt = DateTimeOffset.UtcNow
            }));

        public Task<StudioEndpointResult<StudioRollbackRequest>> CreateRollbackRequestAsync(
            Guid itemId,
            CreateStudioRollbackRequest request,
            CancellationToken cancellationToken = default) =>
            RollbackAsync(itemId, request, cancellationToken);

        private static StudioEndpointResult<T> NotFound<T>(string contract) =>
            StudioEndpointResult<T>.FromIssue(new StudioEndpointIssue("Unsupported", contract, "not found", 404));

        private static StudioEndpointResult<T> Conflict<T>(string contract) =>
            StudioEndpointResult<T>.FromIssue(new StudioEndpointIssue("Conflict", contract, "stale generation", 409));
    }
}
