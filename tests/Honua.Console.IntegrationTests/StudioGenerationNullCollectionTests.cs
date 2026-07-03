using Honua.Sdk.Studio.Packages;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free coverage for the Studio AI-generation mappers across the workflow, map, analysis, dashboard,
/// and app families (the report and form families already have their own equivalents). The server emits empty
/// collections as JSON null (System.Text.Json overrides the non-null property initializer on an explicit
/// null), so a perfectly valid 'generated'/'needs-clarification' result commonly arrives with
/// null clarifications/unmappedRequests. Each MapGeneration must coalesce before LINQ rather than NRE and
/// freeze the Blazor circuit. These tests feed a result whose collections are explicitly null and assert the
/// outcome maps to a sane shape without throwing. The analysis nested-object test additionally proves the
/// version mapper survives a null Item/Version (the nested `= new()` explicit-null-override hazard).
/// </summary>
public sealed class StudioGenerationNullCollectionTests
{
    [Fact]
    public async Task Workflow_Generate_ServerOmitsEmptyCollections_MapsWithoutThrowing()
    {
        // A 'needs-clarification' turn with null collections and no graph: MapGeneration must not call the
        // node registry (only a generated graph does) and must coalesce the null clarifications/unmapped.
        var api = new FakeWorkflowApiClient
        {
            GenerateResult = WorkflowEndpointResult<WorkflowGenerationResult>.FromData(
                new WorkflowGenerationResult
                {
                    Status = StudioWorkflowGenerationStatuses.NeedsClarification,
                    Rationale = "Which source layer feeds the ETL?",
                    Clarifications = null!,
                    UnmappedRequests = null!
                })
        };
        var client = new ServerStudioWorkflowPackageClient(api);

        var outcome = await client.GenerateAsync(
            new StudioWorkflowPackageDraft(),
            new StudioWorkflowGenerationRequest { Prompt = "load parcels then buffer" });

        Assert.True(outcome.NeedsClarification);
        Assert.Null(outcome.Draft);
        Assert.Empty(outcome.Clarifications);
        Assert.Empty(outcome.Warnings);
        Assert.Equal("Which source layer feeds the ETL?", outcome.Rationale);
        Assert.Equal(0, api.NodeRegistryCalls);
    }

    [Fact]
    public async Task Map_Generate_ServerOmitsEmptyCollections_MapsWithoutThrowing()
    {
        var generation = new FakeMapGenerationClient
        {
            Result = StudioEndpointResult<MapGenerationResult>.FromData(
                new MapGenerationResult
                {
                    Status = StudioMapGenerationStatuses.NeedsClarification,
                    Rationale = "Which basemap should the map use?",
                    Clarifications = null!,
                    UnmappedRequests = null!
                })
        };
        var source = new HonuaServerStudioMapPackageDataSource(new ThrowingPackageLifecycleClient(), generation, new UnsupportedOperateTransitionDataSource());

        var outcome = await source.GenerateAsync(
            new StudioMapEditorState(),
            new StudioMapGenerationRequest { Prompt = "a flood-risk map" });

        Assert.True(outcome.NeedsClarification);
        Assert.Null(outcome.State);
        Assert.Empty(outcome.Clarifications);
        Assert.Empty(outcome.Warnings);
        Assert.Equal("Which basemap should the map use?", outcome.Rationale);
    }

    [Fact]
    public async Task Analysis_Generate_ServerOmitsEmptyCollections_MapsWithoutThrowing()
    {
        var client = new FakeAnalysisContentClient
        {
            GenerateResult = HonuaAdminEndpointResult<HonuaAnalysisGenerationResult>.FromData(
                new HonuaAnalysisGenerationResult
                {
                    Status = StudioAnalysisGenerationStatuses.NeedsClarification,
                    Rationale = "Which compute profile?",
                    Clarifications = null!,
                    UnmappedRequests = null!
                })
        };
        var source = new HonuaServerStudioAnalysisContentDataSource(client);

        var outcome = await source.GenerateAsync(
            new StudioAnalysisPlanEditor(),
            new StudioAnalysisGenerationRequest { Prompt = "hotspot analysis of incidents" });

        Assert.True(outcome.NeedsClarification);
        Assert.Null(outcome.Plan);
        Assert.Empty(outcome.Clarifications);
        Assert.Empty(outcome.Warnings);
        Assert.Equal("Which compute profile?", outcome.Rationale);
    }

    [Fact]
    public async Task Dashboard_Generate_ServerOmitsEmptyCollections_MapsWithoutThrowing()
    {
        var publications = new FakeDashboardPublicationClient
        {
            GenerateDashboardResult = HonuaAdminEndpointResult<HonuaReportGenerationResult>.FromData(
                new HonuaReportGenerationResult
                {
                    Status = StudioDashboardGenerationStatuses.NeedsClarification,
                    Rationale = "Which datasets should the panels bind to?",
                    Clarifications = null!,
                    UnmappedRequests = null!
                })
        };
        var source = new HonuaServerStudioDashboardPackageDataSource(new ThrowingPackageLifecycleClient(), publications);

        var outcome = await source.GenerateAsync(
            new StudioDashboardEditorState(),
            new StudioDashboardGenerationRequest { Prompt = "an operations dashboard" });

        Assert.True(outcome.NeedsClarification);
        Assert.Null(outcome.State);
        Assert.Empty(outcome.Clarifications);
        Assert.Empty(outcome.Warnings);
        Assert.Equal("Which datasets should the panels bind to?", outcome.Rationale);
    }

    [Fact]
    public async Task App_Generate_ServerOmitsEmptyCollections_MapsWithoutThrowing()
    {
        var generation = new FakeAppGenerationClient
        {
            Result = StudioEndpointResult<AppGenerationResult>.FromData(
                new AppGenerationResult
                {
                    Status = StudioAppGenerationStatuses.NeedsClarification,
                    Rationale = "Which pages should the app expose?",
                    Clarifications = null!,
                    UnmappedRequests = null!
                })
        };
        var source = new HonuaServerStudioAppPackageDataSource(new ThrowingPackageLifecycleClient(), generation);

        var outcome = await source.GenerateAsync(
            new StudioAppEditorState(),
            new StudioAppGenerationRequest { Prompt = "a field-inspection app" });

        Assert.True(outcome.NeedsClarification);
        Assert.Null(outcome.State);
        Assert.Empty(outcome.Clarifications);
        Assert.Empty(outcome.Warnings);
        Assert.Equal("Which pages should the app expose?", outcome.Rationale);
    }

    [Fact]
    public void AnalysisMapper_OnVersionResponse_WithNullItemAndVersion_LiftsTemplateWithoutThrowing()
    {
        // Item/Version are declared non-null with new() initializers on HonuaAnalysisContentVersionResponse,
        // but System.Text.Json overrides them with null on an explicit JSON null for the key. ToEditorState
        // must coalesce both before deref (ResolveTitle reads item.Title/item.Name; version.Version/SavedQuery
        // are read directly) rather than NRE while rehydrating a loaded version.
        var response = new HonuaAnalysisContentVersionResponse { Item = null!, Version = null! };

        var plan = StudioAnalysisPackageMapper.ToEditorState(response);

        // A null item/version yields empty server identity and a default method/profile rather than a crash.
        Assert.Equal(string.Empty, plan.AnalysisId);
        Assert.Equal(0, plan.Version);
        Assert.Equal(StudioAnalysisMethods.All[0], plan.Method);
        Assert.Equal(StudioAnalysisComputeProfiles.All[0], plan.ComputeProfile);
        Assert.Empty(plan.Inputs);
        Assert.Empty(plan.Parameters);
    }

    [Fact]
    public void QueryMapper_OnVersionResponse_WithNullItemAndVersion_LiftsTemplateWithoutThrowing()
    {
        // Same nested explicit-null hazard as the analysis mapper: the saved-query version mapper shares the
        // HonuaAnalysisContentVersionResponse and reads item.ItemId/version.Version/version.SavedQuery.
        var response = new HonuaAnalysisContentVersionResponse { Item = null!, Version = null! };

        var query = StudioQueryPackageMapper.ToEditorState(response);

        Assert.Equal(string.Empty, query.QueryId);
        Assert.Equal(0, query.Version);
        Assert.Empty(query.Predicates);
        Assert.Empty(query.OutFields);
        Assert.Empty(query.Parameters);
    }

    // --- Fakes: each implements only the generate path; unused members throw so an accidental call is loud. ---

    private sealed class FakeWorkflowApiClient : IWorkflowPackageApiClient
    {
        public Uri BaseUri { get; } = new("https://server.example");

        public WorkflowEndpointResult<WorkflowGenerationResult>? GenerateResult { get; set; }

        public int NodeRegistryCalls { get; private set; }

        public Task<WorkflowEndpointResult<WorkflowGenerationResult>> GenerateWorkflowAsync(
            GenerateWorkflowRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(GenerateResult ?? throw new InvalidOperationException("GenerateResult not configured."));

        public Task<WorkflowEndpointResult<WorkflowNodeRegistrySnapshot>> GetNodeRegistryAsync(
            CancellationToken cancellationToken = default)
        {
            NodeRegistryCalls++;
            return Task.FromResult(WorkflowEndpointResult<WorkflowNodeRegistrySnapshot>.FromData(new WorkflowNodeRegistrySnapshot()));
        }

        public Task<WorkflowEndpointResult<WorkflowPackageListResponse>> ListPackagesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowEndpointResult<WorkflowPackage>> CreatePackageAsync(SaveWorkflowPackageRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowEndpointResult<WorkflowPackage>> GetPackageAsync(string packageId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowEndpointResult<WorkflowPackage>> UpdatePackageAsync(string packageId, SaveWorkflowPackageRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowEndpointResult<WorkflowPackageVersionListResponse>> ListVersionsAsync(string packageId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowEndpointResult<WorkflowPackageVersion>> CreateVersionAsync(string packageId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowEndpointResult<WorkflowPackageVersion>> GetVersionAsync(string packageId, int version, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowEndpointResult<WorkflowPackageValidationResult>> ValidateVersionAsync(string packageId, int version, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowEndpointResult<WorkflowDryRunResult>> DryRunVersionAsync(string packageId, int version, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowEndpointResult<WorkflowPublication>> PublishVersionAsync(string packageId, int version, PublishWorkflowPackageRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowEndpointResult<WorkflowPublicationListResponse>> ListPublicationsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowEndpointResult<WorkflowPublicationRunResult>> RunPublicationAsync(string publicationId, RunWorkflowPublicationRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowEndpointResult<WorkflowGenerationProviders>> ListGenerationProvidersAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> RecordGenerationFeedbackAsync(WorkflowGenerationFeedbackRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeMapGenerationClient : IStudioMapGenerationClient
    {
        public Uri BaseUri { get; } = new("https://server.example");

        public StudioEndpointResult<MapGenerationResult>? Result { get; set; }

        public Task<StudioEndpointResult<MapGenerationResult>> GenerateMapAsync(
            GenerateMapPackageRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result ?? throw new InvalidOperationException("Result not configured."));
    }

    private sealed class FakeAppGenerationClient : IStudioAppGenerationClient
    {
        public Uri BaseUri { get; } = new("https://server.example");

        public StudioEndpointResult<AppGenerationResult>? Result { get; set; }

        public Task<StudioEndpointResult<AppGenerationResult>> GenerateAppAsync(
            GenerateAppPackageRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result ?? throw new InvalidOperationException("Result not configured."));
    }

    private sealed class FakeAnalysisContentClient : IHonuaAnalysisContentClient
    {
        public Uri BaseUri { get; } = new("https://server.example");

        public HonuaAdminEndpointResult<HonuaAnalysisGenerationResult>? GenerateResult { get; set; }

        public Task<HonuaAdminEndpointResult<HonuaAnalysisGenerationResult>> GenerateAsync(
            HonuaGenerateAnalysisRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(GenerateResult ?? throw new InvalidOperationException("GenerateResult not configured."));

        public Task<HonuaAdminEndpointResult<HonuaAnalysisContentVersionResponse>> CreateItemAsync(HonuaCreateAnalysisContentItemRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAnalysisContentItemListResponse>> ListItemsAsync(HonuaAnalysisContentListQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAnalysisContentEstimateResponse>> EstimateAsync(string itemId, int version, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAnalysisContentVersionResponse>> GetItemAsync(string itemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAnalysisContentVersionResponse>> GetVersionAsync(string itemId, int? version, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAnalysisContentVersionResponse>> CreateVersionAsync(string itemId, HonuaCreateAnalysisContentVersionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaSavedQueryPreviewResult>> PreviewSavedQueryAsync(string itemId, int version, int? limit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAnalysisContentJobResponse>> RunAsync(string itemId, int version, HonuaRunAnalysisContentVersionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAnalysisArtifactResponse>> GetArtifactAsync(string artifactId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAnalysisJobFailure>> GetJobFailureAsync(string jobId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaSavedQueryGenerationResult>> GenerateQueryAsync(HonuaGenerateSavedQueryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeDashboardPublicationClient : IHonuaContentPublicationClient
    {
        public Uri BaseUri { get; } = new("https://server.example");

        public HonuaAdminEndpointResult<HonuaReportGenerationResult>? GenerateDashboardResult { get; set; }

        public Task<HonuaAdminEndpointResult<HonuaReportGenerationResult>> GenerateDashboardAsync(
            GenerateDashboardContentRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(GenerateDashboardResult ?? throw new InvalidOperationException("GenerateDashboardResult not configured."));

        public Task<HonuaAdminEndpointResult<HonuaReportGenerationProviders>> ListReportGenerationProvidersAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaReportGenerationResult>> GenerateReportAsync(GenerateReportContentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaContentPublicationDetail>> GetAsync(string publicationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaContentPublicationVersion>> GetVersionAsync(string publicationId, string versionSelector, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaContentPublicationDetail>> PublishAsync(HonuaPublishContentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaContentPublicationDetail>> RepublishAsync(string publicationId, HonuaRepublishContentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaContentPublicationDetail>> RollbackAsync(string publicationId, HonuaRollbackContentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaContentPublicationPolicyUpdateResponse>> UpdatePolicyAsync(string publicationId, HonuaUpdatePublicationPolicyRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    // The map/dashboard/app generate paths never touch the package lifecycle client, so a throwing stub proves
    // the generate path does not depend on it while satisfying the non-null constructor argument.
    private sealed class ThrowingPackageLifecycleClient : IStudioPackageLifecycleClient
    {
        public Uri BaseUri { get; } = new("https://server.example");

        public Task<StudioEndpointResult<StudioPackageFamilyCapabilities>> ListPackageFamiliesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StudioEndpointResult<StudioPackageDraftListResponse>> ListPackageDraftsAsync(Honua.Sdk.Studio.Packages.StudioPackageFamily? family = null, Honua.Sdk.Studio.Packages.StudioPackageValidationStatus? status = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StudioEndpointResult<StudioPackageDraft>> CreatePackageDraftAsync(CreateStudioPackageDraftRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StudioEndpointResult<StudioPackageDraft>> GetPackageDraftAsync(Guid draftId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StudioEndpointResult<StudioPackageDraft>> UpdatePackageDraftAsync(Guid draftId, UpdateStudioPackageDraftRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StudioEndpointResult<StudioValidationSummary>> ValidatePackageDraftAsync(Guid draftId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StudioEndpointResult<StudioPreviewPlan>> CreatePreviewPlanAsync(Guid draftId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StudioEndpointResult<StudioContentVersion>> SaveContentVersionAsync(Guid draftId, SaveStudioContentVersionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StudioEndpointResult<StudioContentVersionList>> ListContentVersionsAsync(Guid itemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StudioEndpointResult<StudioContentVersion>> GetContentVersionAsync(Guid itemId, Guid versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StudioEndpointResult<StudioPublicationRequest>> CreatePublishRequestAsync(Guid itemId, Guid versionId, CreateStudioPublicationRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StudioEndpointResult<StudioPackageDraft>> ReopenContentVersionAsync(Guid itemId, Guid versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StudioEndpointResult<StudioPackageDraft>> ReopenVersionAsync(Guid itemId, Guid versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StudioEndpointResult<StudioRollbackRequest>> RollbackAsync(Guid itemId, CreateStudioRollbackRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StudioEndpointResult<StudioRollbackRequest>> CreateRollbackRequestAsync(Guid itemId, CreateStudioRollbackRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
