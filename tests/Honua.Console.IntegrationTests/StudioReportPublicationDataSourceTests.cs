using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free coverage for the report-builder publication data source. The server-bound source maps the
/// content publication detail into the report view, rejects non-report artifacts as unsupported, and surfaces
/// endpoint issues as capability states; the unsupported source returns an explicit missing-binding state and
/// never fabricates publication data (Console Patterns Charter section 11).
/// </summary>
public sealed class StudioReportPublicationDataSourceTests
{
    [Fact]
    public async Task Generate_ServerProposesReport_NullCollections_MapsWithoutThrowing()
    {
        // The server omits empty collections (System.Text.Json serializes them as JSON null), so a valid
        // 'generated' result commonly arrives with null UnmappedRequests/Clarifications and a document whose
        // bindings/panels are null. The mapper must coalesce/guard rather than NRE and freeze the page
        // (regression: report MapGeneration had dropped the guards its siblings have).
        var client = new FakePublicationClient
        {
            GenerateReportResult = HonuaAdminEndpointResult<HonuaReportGenerationResult>.FromData(
                new HonuaReportGenerationResult
                {
                    Status = StudioReportGenerationStatuses.Generated,
                    RouteSlug = "quarterly-permits",
                    Rationale = "Proposed a permits report.",
                    Document = JsonSerializer.Deserialize<JsonElement>(
                        "{\"title\":\"Quarterly Permits\",\"bindings\":null,\"panels\":null}"),
                    Clarifications = null!,
                    UnmappedRequests = null!
                })
        };
        var source = new HonuaServerStudioReportPublicationDataSource(client);

        var outcome = await source.GenerateAsync(new StudioReportEditorState(), new StudioReportGenerationRequest { Prompt = "permits report" });

        Assert.True(outcome.IsGenerated);
        Assert.NotNull(outcome.State);
        Assert.Equal("Quarterly Permits", outcome.State!.Title);
        Assert.Empty(outcome.Clarifications);
        Assert.Equal("Proposed a permits report.", outcome.Rationale);
    }

    [Fact]
    public async Task Generate_NeedsClarification_NullCollections_MapsWithoutThrowing()
    {
        var client = new FakePublicationClient
        {
            GenerateReportResult = HonuaAdminEndpointResult<HonuaReportGenerationResult>.FromData(
                new HonuaReportGenerationResult
                {
                    Status = StudioReportGenerationStatuses.NeedsClarification,
                    Rationale = "Which dataset should the chart bind to?",
                    Clarifications = null!,
                    UnmappedRequests = null!
                })
        };
        var source = new HonuaServerStudioReportPublicationDataSource(client);

        var outcome = await source.GenerateAsync(new StudioReportEditorState(), new StudioReportGenerationRequest { Prompt = "a report" });

        Assert.True(outcome.NeedsClarification);
        Assert.Null(outcome.State);
        Assert.Empty(outcome.Clarifications);
    }

    [Fact]
    public async Task Generate_ServerLacksContract_404_SurfacesUnsupportedNotMissingBinding()
    {
        var client = new FakePublicationClient
        {
            GenerateReportResult = HonuaAdminEndpointResult<HonuaReportGenerationResult>.FromIssue(
                new HonuaAdminEndpointIssue("Unsupported", "POST generate", "Not found.", 404))
        };
        var source = new HonuaServerStudioReportPublicationDataSource(client);

        var outcome = await source.GenerateAsync(new StudioReportEditorState(), new StudioReportGenerationRequest { Prompt = "hi" });

        Assert.Equal(StudioReportGenerationStatuses.Unsupported, outcome.Status);
        Assert.Null(outcome.BindingState);
        Assert.Equal(1, client.GenerateReportCalls);
    }

    [Fact]
    public async Task ServerSource_OnReportDetail_MapsRouteAndVersionHistory()
    {
        var detail = new HonuaContentPublicationDetail
        {
            Route = new HonuaContentPublicationRouteState
            {
                PublicationId = "pub-report-1",
                RouteSlug = "monthly-infrastructure",
                RoutePath = "/published/monthly-infrastructure",
                Kind = HonuaContentPublicationKinds.Report,
                ActiveVersionId = "ver-2",
                ActiveRevision = 2,
                Lifecycle = HonuaContentPublicationLifecycles.Active,
                Etag = "etag-2",
                Policy = new HonuaContentPublicationPolicy
                {
                    Visibility = HonuaContentPublicationVisibilities.Organization,
                    Embed = new HonuaContentEmbedPolicy { AllowEmbedding = true }
                }
            },
            Versions =
            [
                new HonuaContentPublicationVersion
                {
                    PublicationId = "pub-report-1",
                    VersionId = "ver-1",
                    Revision = 1,
                    Kind = HonuaContentPublicationKinds.Report,
                    RouteSlug = "monthly-infrastructure",
                    RoutePath = "/published/monthly-infrastructure",
                    Title = "Initial report",
                    CreatedBy = "ops@honua.test"
                },
                new HonuaContentPublicationVersion
                {
                    PublicationId = "pub-report-1",
                    VersionId = "ver-2",
                    Revision = 2,
                    Kind = HonuaContentPublicationKinds.Report,
                    RouteSlug = "monthly-infrastructure",
                    RoutePath = "/published/monthly-infrastructure",
                    Title = "Monthly infrastructure report",
                    CreatedBy = "ops@honua.test",
                    Dependencies = [new HonuaContentPublicationDependencyRef { Kind = "service", RefId = "svc-1" }]
                }
            ]
        };
        var source = new HonuaServerStudioReportPublicationDataSource(
            new FakePublicationClient(HonuaAdminEndpointResult<HonuaContentPublicationDetail>.FromData(detail)));

        var load = await source.LoadAsync("pub-report-1");

        Assert.True(load.HasPublication);
        Assert.Empty(load.CapabilityStates);
        var view = load.Publication!;
        Assert.Equal("pub-report-1", view.PublicationId);
        Assert.Equal("report", view.Kind);
        Assert.Equal("organization", view.Visibility);
        Assert.True(view.Embeddable);
        Assert.Equal("Monthly infrastructure report", view.ActiveTitle);
        Assert.Equal(2, view.ActiveRevision);
        // Versions are projected newest-first; the active version is flagged.
        Assert.Equal(2, view.Versions[0].Revision);
        Assert.True(view.Versions[0].IsActive);
        Assert.Equal(1, view.Versions[0].DependencyCount);
        Assert.False(view.Versions[1].IsActive);
    }

    [Fact]
    public async Task ServerSource_WhenPublicationIsNotAReport_RejectsAsUnsupported()
    {
        var detail = new HonuaContentPublicationDetail
        {
            Route = new HonuaContentPublicationRouteState
            {
                PublicationId = "pub-map-1",
                Kind = HonuaContentPublicationKinds.Map,
                ActiveVersionId = "ver-1",
                ActiveRevision = 1
            }
        };
        var source = new HonuaServerStudioReportPublicationDataSource(
            new FakePublicationClient(HonuaAdminEndpointResult<HonuaContentPublicationDetail>.FromData(detail)));

        var load = await source.LoadAsync("pub-map-1");

        Assert.False(load.HasPublication);
        var state = Assert.Single(load.CapabilityStates);
        Assert.Equal("Unsupported", state.State);
        Assert.Contains("not a report", state.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServerSource_OnEndpointIssue_SurfacesCapabilityState()
    {
        var issue = new HonuaAdminEndpointIssue("Missing permission", "GET ...", "No access.", 403);
        var source = new HonuaServerStudioReportPublicationDataSource(
            new FakePublicationClient(HonuaAdminEndpointResult<HonuaContentPublicationDetail>.FromIssue(issue)));

        var load = await source.LoadAsync("pub-1");

        Assert.False(load.HasPublication);
        var state = Assert.Single(load.CapabilityStates);
        Assert.Equal("Missing permission", state.State);
        Assert.Contains("403", state.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnsupportedSource_ReturnsMissingBinding()
    {
        var source = new UnsupportedStudioReportPublicationDataSource();

        var load = await source.LoadAsync("pub-1");

        Assert.False(load.HasPublication);
        var state = Assert.Single(load.CapabilityStates);
        Assert.Equal("Missing binding", state.State);
        Assert.Equal("Honua:Server:BaseUrl", state.Contract);
    }

    [Fact]
    public async Task UnsupportedSource_PublishReturnsMissingBindingFailure()
    {
        var source = new UnsupportedStudioReportPublicationDataSource();

        var result = await source.PublishAsync(AuthoredReport());

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Issue);
        Assert.Equal("Missing binding", result.Issue!.State);
    }

    [Fact]
    public async Task ServerSource_PublishWhenGateUnmet_DoesNotCallServer()
    {
        var client = new FakePublicationClient();
        var source = new HonuaServerStudioReportPublicationDataSource(client);

        // An empty report fails the pre-publish gate (no title, no panels), so the server is never hit.
        var result = await source.PublishAsync(new StudioReportEditorState());

        Assert.False(result.Succeeded);
        Assert.Contains("Resolve before publish", result.Message, StringComparison.Ordinal);
        Assert.Equal(0, client.PublishCalls);
    }

    [Fact]
    public async Task ServerSource_PublishNewReport_PostsCreateAndMapsView()
    {
        var client = new FakePublicationClient
        {
            PublishResult = HonuaAdminEndpointResult<HonuaContentPublicationDetail>.FromData(
                ReportDetail("pub-report-1", revision: 1))
        };
        var source = new HonuaServerStudioReportPublicationDataSource(client);

        var result = await source.PublishAsync(AuthoredReport());

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(1, client.PublishCalls);
        Assert.Equal(0, client.RepublishCalls);
        Assert.NotNull(client.LastPublishRequest);
        Assert.Equal(HonuaContentPublicationKinds.Report, client.LastPublishRequest!.Kind);
        Assert.False(string.IsNullOrWhiteSpace(client.LastPublishRequest.ContentPayload));
        Assert.Equal("organization", client.LastPublishRequest.Policy!.Visibility);
        Assert.NotNull(result.Publication);
        Assert.Equal("pub-report-1", result.Publication!.PublicationId);
    }

    [Fact]
    public async Task ServerSource_PublishExistingReport_Republishes()
    {
        var client = new FakePublicationClient
        {
            RepublishResult = HonuaAdminEndpointResult<HonuaContentPublicationDetail>.FromData(
                ReportDetail("pub-report-1", revision: 3))
        };
        var source = new HonuaServerStudioReportPublicationDataSource(client);
        var state = AuthoredReport();
        state.PublicationId = "pub-report-1";
        state.ETag = "etag-2";

        var result = await source.PublishAsync(state);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(0, client.PublishCalls);
        Assert.Equal(1, client.RepublishCalls);
        Assert.Equal("etag-2", client.LastRepublishRequest!.ExpectedEtag);
        Assert.Equal(3, result.Publication!.ActiveRevision);
    }

    [Fact]
    public async Task ServerSource_Rollback_PostsTargetVersion()
    {
        var client = new FakePublicationClient
        {
            RollbackResult = HonuaAdminEndpointResult<HonuaContentPublicationDetail>.FromData(
                ReportDetail("pub-report-1", revision: 1))
        };
        var source = new HonuaServerStudioReportPublicationDataSource(client);

        var result = await source.RollbackAsync("pub-report-1", "ver-1");

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("ver-1", client.LastRollbackRequest!.TargetVersionId);
        Assert.Equal(1, result.Publication!.ActiveRevision);
    }

    [Fact]
    public async Task ServerSource_Rollback_OnConflict_SurfacesIssue()
    {
        var client = new FakePublicationClient
        {
            RollbackResult = HonuaAdminEndpointResult<HonuaContentPublicationDetail>.FromIssue(
                new HonuaAdminEndpointIssue("Conflict", "POST ...", "Route changed; reload.", 409))
        };
        var source = new HonuaServerStudioReportPublicationDataSource(client);

        var result = await source.RollbackAsync("pub-report-1", "ver-1");

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Issue);
        Assert.Equal("Conflict", result.Issue!.State);
    }

    [Fact]
    public async Task ServerSource_UpdatePolicy_PatchesThenReloadsDetail()
    {
        var route = ReportDetail("pub-report-1", revision: 2).Route with
        {
            Policy = new HonuaContentPublicationPolicy
            {
                Visibility = HonuaContentPublicationVisibilities.Public,
                Embed = new HonuaContentEmbedPolicy { AllowEmbedding = true }
            }
        };
        var client = new FakePublicationClient
        {
            PolicyResult = HonuaAdminEndpointResult<HonuaContentPublicationPolicyUpdateResponse>.FromData(
                new HonuaContentPublicationPolicyUpdateResponse { Route = route }),
            // The data source re-reads the detail after the policy patch so the version panel stays in sync.
            GetResult = HonuaAdminEndpointResult<HonuaContentPublicationDetail>.FromData(
                ReportDetail("pub-report-1", revision: 2, visibility: HonuaContentPublicationVisibilities.Public, embeddable: true))
        };
        var source = new HonuaServerStudioReportPublicationDataSource(client);

        var result = await source.UpdatePolicyAsync("pub-report-1", "public", embeddable: true);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("public", client.LastPolicyRequest!.Visibility);
        Assert.True(client.LastPolicyRequest.Embed!.AllowEmbedding);
        Assert.Equal(1, client.GetCalls);
        Assert.Equal("public", result.Publication!.Visibility);
        Assert.True(result.Publication.Embeddable);
    }

    private static StudioReportEditorState AuthoredReport()
    {
        var state = new StudioReportEditorState
        {
            Title = "Monthly infrastructure report",
            RouteSlug = "monthly-infrastructure",
            Visibility = StudioReportVisibilities.Organization,
            Embeddable = true
        };
        state.Bindings.Add(new StudioReportBindingEditor { Alias = "incidents", ContentRef = "content:incidents" });
        state.Panels.Add(new StudioReportPanelEditor
        {
            Title = "Incidents by district",
            Kind = StudioReportPanelKinds.Chart,
            BindingAlias = "incidents",
            VegaLiteSpec = StudioReportChartSpec.DefaultBarChart()
        });
        return state;
    }

    private static HonuaContentPublicationDetail ReportDetail(
        string publicationId,
        long revision,
        string visibility = HonuaContentPublicationVisibilities.Organization,
        bool embeddable = false) =>
        new()
        {
            Route = new HonuaContentPublicationRouteState
            {
                PublicationId = publicationId,
                RouteSlug = "monthly-infrastructure",
                RoutePath = "/published/monthly-infrastructure",
                Kind = HonuaContentPublicationKinds.Report,
                ActiveVersionId = $"ver-{revision}",
                ActiveRevision = revision,
                Lifecycle = HonuaContentPublicationLifecycles.Active,
                Etag = $"etag-{revision}",
                Policy = new HonuaContentPublicationPolicy
                {
                    Visibility = visibility,
                    Embed = new HonuaContentEmbedPolicy { AllowEmbedding = embeddable }
                }
            },
            Versions =
            [
                new HonuaContentPublicationVersion
                {
                    PublicationId = publicationId,
                    VersionId = $"ver-{revision}",
                    Revision = revision,
                    Kind = HonuaContentPublicationKinds.Report,
                    RouteSlug = "monthly-infrastructure",
                    RoutePath = "/published/monthly-infrastructure",
                    Title = "Monthly infrastructure report",
                    CreatedBy = "ops@honua.test"
                }
            ]
        };

    private sealed class FakePublicationClient : IHonuaContentPublicationClient
    {
        public Task<HonuaAdminEndpointResult<HonuaReportGenerationProviders>> ListReportGenerationProvidersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(HonuaAdminEndpointResult<HonuaReportGenerationProviders>.FromIssue(
                new HonuaAdminEndpointIssue("Unsupported", "GET providers", "Not exercised by this fake.")));

        public HonuaAdminEndpointResult<HonuaReportGenerationResult>? GenerateReportResult { get; set; }

        public int GenerateReportCalls { get; private set; }

        public GenerateReportContentRequest? LastGenerateReportRequest { get; private set; }

        public Task<HonuaAdminEndpointResult<HonuaReportGenerationResult>> GenerateReportAsync(GenerateReportContentRequest request, CancellationToken cancellationToken = default)
        {
            GenerateReportCalls++;
            LastGenerateReportRequest = request;
            return Task.FromResult(GenerateReportResult ?? HonuaAdminEndpointResult<HonuaReportGenerationResult>.FromIssue(
                new HonuaAdminEndpointIssue("Unsupported", "POST generate", "Not exercised by this fake.")));
        }

        public Task<HonuaAdminEndpointResult<HonuaReportGenerationResult>> GenerateDashboardAsync(GenerateDashboardContentRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(HonuaAdminEndpointResult<HonuaReportGenerationResult>.FromIssue(
                new HonuaAdminEndpointIssue("Unsupported", "POST generate", "Not exercised by this fake.")));

        public FakePublicationClient(HonuaAdminEndpointResult<HonuaContentPublicationDetail>? getResult = null)
        {
            GetResult = getResult;
        }

        public Uri BaseUri { get; } = new("https://honua.test");

        public HonuaAdminEndpointResult<HonuaContentPublicationDetail>? GetResult { get; set; }

        public HonuaAdminEndpointResult<HonuaContentPublicationDetail>? PublishResult { get; set; }

        public HonuaAdminEndpointResult<HonuaContentPublicationDetail>? RepublishResult { get; set; }

        public HonuaAdminEndpointResult<HonuaContentPublicationDetail>? RollbackResult { get; set; }

        public HonuaAdminEndpointResult<HonuaContentPublicationPolicyUpdateResponse>? PolicyResult { get; set; }

        public int GetCalls { get; private set; }

        public int PublishCalls { get; private set; }

        public int RepublishCalls { get; private set; }

        public HonuaPublishContentRequest? LastPublishRequest { get; private set; }

        public HonuaRepublishContentRequest? LastRepublishRequest { get; private set; }

        public HonuaRollbackContentRequest? LastRollbackRequest { get; private set; }

        public HonuaUpdatePublicationPolicyRequest? LastPolicyRequest { get; private set; }

        public Task<HonuaAdminEndpointResult<HonuaContentPublicationDetail>> GetAsync(
            string publicationId,
            CancellationToken cancellationToken = default)
        {
            GetCalls++;
            return Task.FromResult(GetResult ?? throw new InvalidOperationException("GetResult not configured."));
        }

        public Task<HonuaAdminEndpointResult<HonuaContentPublicationVersion>> GetVersionAsync(
            string publicationId,
            string versionSelector,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<HonuaAdminEndpointResult<HonuaContentPublicationDetail>> PublishAsync(
            HonuaPublishContentRequest request,
            CancellationToken cancellationToken = default)
        {
            PublishCalls++;
            LastPublishRequest = request;
            return Task.FromResult(PublishResult ?? throw new InvalidOperationException("PublishResult not configured."));
        }

        public Task<HonuaAdminEndpointResult<HonuaContentPublicationDetail>> RepublishAsync(
            string publicationId,
            HonuaRepublishContentRequest request,
            CancellationToken cancellationToken = default)
        {
            RepublishCalls++;
            LastRepublishRequest = request;
            return Task.FromResult(RepublishResult ?? throw new InvalidOperationException("RepublishResult not configured."));
        }

        public Task<HonuaAdminEndpointResult<HonuaContentPublicationDetail>> RollbackAsync(
            string publicationId,
            HonuaRollbackContentRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRollbackRequest = request;
            return Task.FromResult(RollbackResult ?? throw new InvalidOperationException("RollbackResult not configured."));
        }

        public Task<HonuaAdminEndpointResult<HonuaContentPublicationPolicyUpdateResponse>> UpdatePolicyAsync(
            string publicationId,
            HonuaUpdatePublicationPolicyRequest request,
            CancellationToken cancellationToken = default)
        {
            LastPolicyRequest = request;
            return Task.FromResult(PolicyResult ?? throw new InvalidOperationException("PolicyResult not configured."));
        }
    }
}
