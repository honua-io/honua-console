using Honua.Console.Contracts;
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

    private sealed class FakePublicationClient : IHonuaContentPublicationClient
    {
        private readonly HonuaAdminEndpointResult<HonuaContentPublicationDetail> _detail;

        public FakePublicationClient(HonuaAdminEndpointResult<HonuaContentPublicationDetail> detail)
        {
            _detail = detail;
        }

        public Uri BaseUri { get; } = new("https://honua.test");

        public Task<HonuaAdminEndpointResult<HonuaContentPublicationDetail>> GetAsync(
            string publicationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_detail);

        public Task<HonuaAdminEndpointResult<HonuaContentPublicationVersion>> GetVersionAsync(
            string publicationId,
            string versionSelector,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
