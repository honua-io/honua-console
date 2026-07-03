using Honua.Sdk.Studio.Packages;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using StudioPackageFamily = Honua.Sdk.Studio.Packages.StudioPackageFamily;

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
    public async Task GetWorkspace_WithNoDrafts_ReturnsEmptyListWithoutCapabilityState()
    {
        var dataSource = new HonuaServerStudioDashboardPackageDataSource(new RecordingLifecycleClient());

        var workspace = await dataSource.GetWorkspaceAsync();

        Assert.Empty(workspace.Packages);
        Assert.Empty(workspace.CapabilityStates);
    }

    [Fact]
    public async Task GetWorkspace_EnumeratesLiveDashboardDrafts()
    {
        var draftId = Guid.NewGuid();
        var client = new RecordingLifecycleClient
        {
            ListResult =
            [
                new StudioPackageDraftSummary
                {
                    DraftId = draftId,
                    ItemId = Guid.NewGuid(),
                    PackageKey = "studio-dashboard-sales",
                    Family = StudioPackageFamily.Dashboard,
                    ValidationStatus = StudioPackageValidationStatus.Valid,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    CreatedAt = DateTimeOffset.UtcNow,
                },
            ],
        };
        var dataSource = new HonuaServerStudioDashboardPackageDataSource(client);

        var workspace = await dataSource.GetWorkspaceAsync();

        Assert.Empty(workspace.CapabilityStates);
        var item = Assert.Single(workspace.Packages);
        Assert.Equal(draftId.ToString(), item.DashboardId);
        Assert.Equal("studio-dashboard-sales", item.Title);
        Assert.Equal(StudioPackageFamily.Dashboard, client.LastListFamily);
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

    [Fact]
    public void DocumentMapper_LiftsPanelsBindingsAndNarrative_AndMapsMetricToTable()
    {
        var document = JsonSerializer.Deserialize<JsonElement>(
            """
            {
              "format": "honua.dashboard-document.v1",
              "title": "Service requests",
              "description": "Open requests by district.",
              "narrative": "Operational context.",
              "breakpoint": "tablet",
              "bindings": [ { "alias": "requests", "contentRef": "content:service-requests", "versionPin": "v5" } ],
              "panels": [
                { "id": "p1", "kind": "chart", "title": "By district", "bindingAlias": "requests",
                  "chartSpec": { "$schema": "https://vega.github.io/schema/vega-lite/v5.json", "mark": "bar" } },
                { "id": "p2", "kind": "map", "title": "Map", "bindingAlias": "requests" },
                { "id": "p3", "kind": "metric", "title": "Total open", "field": "open_count", "bindingAlias": "requests" }
              ]
            }
            """);

        var state = StudioDashboardPackageMapper.CreateTemplate();
        var notes = StudioDashboardDocument.ApplyGeneratedDocument(state, document);

        Assert.Equal("Service requests", state.Title);
        Assert.Equal("Operational context.", state.Narrative);
        Assert.Equal("tablet", state.PreviewBreakpoint);

        var binding = Assert.Single(state.Bindings);
        Assert.Equal("requests", binding.Alias);
        Assert.Equal("content:service-requests", binding.ContentRef);
        Assert.Equal("v5", binding.VersionPin);

        Assert.Equal(3, state.Panels.Count);
        Assert.Equal(StudioDashboardPanelKinds.Chart, state.Panels[0].Kind);
        Assert.Contains("vega-lite", state.Panels[0].VegaLiteSpec, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(StudioDashboardPanelKinds.Map, state.Panels[1].Kind);
        // metric has no editor kind: it is downgraded to the nearest existing slot (table) and reported.
        Assert.Equal(StudioDashboardDocument.MetricMappedToKind, state.Panels[2].Kind);
        Assert.Equal("requests", state.Panels[2].BindingAlias);
        Assert.Contains(notes, note => note.Contains("metric", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Generate_GeneratedDocument_HydratesFreshDraftFromDocument()
    {
        var document = JsonSerializer.Deserialize<JsonElement>(
            """
            { "title": "Ops", "panels": [ { "kind": "table", "title": "Rows", "bindingAlias": "requests" } ],
              "bindings": [ { "alias": "requests", "contentRef": "content:requests" } ] }
            """);
        var client = new RecordingPublicationClient
        {
            Result = HonuaAdminEndpointResult<HonuaReportGenerationResult>.FromData(new HonuaReportGenerationResult
            {
                Status = "generated",
                Document = document,
                Rationale = "Proposed a single-table dashboard."
            })
        };
        var dataSource = new HonuaServerStudioDashboardPackageDataSource(new RecordingLifecycleClient(), client);

        var outcome = await dataSource.GenerateAsync(
            new StudioDashboardEditorState(),
            new StudioDashboardGenerationRequest { Prompt = "show open requests" });

        Assert.True(outcome.IsGenerated);
        Assert.NotNull(outcome.State);
        Assert.Equal("Ops", outcome.State!.Title);
        Assert.Single(outcome.State.Panels);
        // A first turn (blank editor) ships no document; the server is asked to generate fresh.
        Assert.Null(client.LastRequest!.Document);
        Assert.Equal("dashboard", client.LastRequest.Kind);
    }

    [Fact]
    public async Task Generate_NeedsClarification_SurfacesQuestions()
    {
        var client = new RecordingPublicationClient
        {
            Result = HonuaAdminEndpointResult<HonuaReportGenerationResult>.FromData(new HonuaReportGenerationResult
            {
                Status = "needs-clarification",
                Clarifications =
                [
                    new HonuaReportGenerationClarification
                    {
                        Id = "binding",
                        Prompt = "Which dataset?",
                        Choices = [ new HonuaReportGenerationClarificationChoice { Id = "a", Label = "Requests" } ]
                    }
                ]
            })
        };
        var dataSource = new HonuaServerStudioDashboardPackageDataSource(new RecordingLifecycleClient(), client);

        var outcome = await dataSource.GenerateAsync(
            new StudioDashboardEditorState(),
            new StudioDashboardGenerationRequest { Prompt = "build a dashboard" });

        Assert.True(outcome.NeedsClarification);
        var question = Assert.Single(outcome.Clarifications);
        Assert.Equal("Which dataset?", question.Label);
    }

    [Fact]
    public async Task Generate_404_SurfacesUnsupportedNotMissingBinding()
    {
        var client = new RecordingPublicationClient
        {
            Result = HonuaAdminEndpointResult<HonuaReportGenerationResult>.FromIssue(
                new HonuaAdminEndpointIssue("Unsupported", "POST generate", "not found", 404))
        };
        var dataSource = new HonuaServerStudioDashboardPackageDataSource(new RecordingLifecycleClient(), client);

        var outcome = await dataSource.GenerateAsync(
            new StudioDashboardEditorState(),
            new StudioDashboardGenerationRequest { Prompt = "x" });

        Assert.Null(outcome.BindingState);
        Assert.Equal(StudioDashboardGenerationStatuses.Unsupported, outcome.Status);
    }

    [Fact]
    public async Task Generate_RefineTurn_ShipsCurrentDocument()
    {
        var client = new RecordingPublicationClient
        {
            Result = HonuaAdminEndpointResult<HonuaReportGenerationResult>.FromData(new HonuaReportGenerationResult { Status = "generated" })
        };
        var dataSource = new HonuaServerStudioDashboardPackageDataSource(new RecordingLifecycleClient(), client);

        // A populated editor (panels present) is a refine turn -> the current document is shipped.
        await dataSource.GenerateAsync(ReadyDashboard(), new StudioDashboardGenerationRequest { Prompt = "add a map" });

        Assert.NotNull(client.LastRequest!.Document);
    }

    [Fact]
    public async Task Generate_WithoutPublicationClient_SurfacesMissingBinding()
    {
        var dataSource = new HonuaServerStudioDashboardPackageDataSource(new RecordingLifecycleClient());

        var outcome = await dataSource.GenerateAsync(
            new StudioDashboardEditorState(),
            new StudioDashboardGenerationRequest { Prompt = "x" });

        Assert.NotNull(outcome.BindingState);
        Assert.Equal("Missing binding", outcome.BindingState!.State);
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
            Task.FromResult(StudioEndpointResult<StudioPackageFamilyCapabilities>.FromData(new StudioPackageFamilyCapabilities { PersistenceMode = StudioPackagePersistenceMode.Durable, Durable = true }));

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

        public IReadOnlyList<StudioPackageDraftSummary> ListResult { get; init; } = [];

        public StudioPackageFamily? LastListFamily { get; private set; }

        public Task<StudioEndpointResult<StudioPackageDraftListResponse>> ListPackageDraftsAsync(
            StudioPackageFamily? family = null,
            StudioPackageValidationStatus? status = null,
            CancellationToken cancellationToken = default)
        {
            LastListFamily = family;
            return Task.FromResult(StudioEndpointResult<StudioPackageDraftListResponse>.FromData(
                new StudioPackageDraftListResponse { Drafts = ListResult }));
        }

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
            Task.FromResult(StudioEndpointResult<StudioPreviewPlan>.FromData(new StudioPreviewPlan
            {
                DraftId = draftId,
                Family = StudioPackageFamily.Dashboard,
                Synchronous = false,
                RequiresJob = false,
                Validation = StudioValidationSummary.NotValidated
            }));

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
                ContentHash = string.Empty,
                Envelope = _draft.Envelope,
                Validation = StudioValidationSummary.NotValidated,
                SourceDraftId = _draft.DraftId,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _versions.Add(version);
            return Task.FromResult(StudioEndpointResult<StudioContentVersion>.FromData(version));
        }

        public Task<StudioEndpointResult<StudioContentVersionList>> ListContentVersionsAsync(
            Guid itemId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(StudioEndpointResult<StudioContentVersionList>.FromData(new StudioContentVersionList
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

    /// <summary>
    /// In-memory recording content-publication client. Only the dashboard generation verb is exercised; the
    /// publication lifecycle verbs throw because the dashboard data source routes those through the Studio
    /// package lifecycle client, never this one.
    /// </summary>
    private sealed class RecordingPublicationClient : IHonuaContentPublicationClient
    {
        public Uri BaseUri { get; } = new("https://honua.test");

        public HonuaAdminEndpointResult<HonuaReportGenerationResult> Result { get; init; } =
            HonuaAdminEndpointResult<HonuaReportGenerationResult>.FromData(new HonuaReportGenerationResult());

        public GenerateDashboardContentRequest? LastRequest { get; private set; }

        public Task<HonuaAdminEndpointResult<HonuaReportGenerationResult>> GenerateDashboardAsync(
            GenerateDashboardContentRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(Result);
        }

        public Task<HonuaAdminEndpointResult<HonuaReportGenerationResult>> GenerateReportAsync(
            GenerateReportContentRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<HonuaAdminEndpointResult<HonuaReportGenerationProviders>> ListReportGenerationProvidersAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<HonuaAdminEndpointResult<HonuaContentPublicationDetail>> GetAsync(
            string publicationId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<HonuaAdminEndpointResult<HonuaContentPublicationVersion>> GetVersionAsync(
            string publicationId, string versionSelector, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<HonuaAdminEndpointResult<HonuaContentPublicationDetail>> PublishAsync(
            HonuaPublishContentRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<HonuaAdminEndpointResult<HonuaContentPublicationDetail>> RepublishAsync(
            string publicationId, HonuaRepublishContentRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<HonuaAdminEndpointResult<HonuaContentPublicationDetail>> RollbackAsync(
            string publicationId, HonuaRollbackContentRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<HonuaAdminEndpointResult<HonuaContentPublicationPolicyUpdateResponse>> UpdatePolicyAsync(
            string publicationId, HonuaUpdatePublicationPolicyRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
