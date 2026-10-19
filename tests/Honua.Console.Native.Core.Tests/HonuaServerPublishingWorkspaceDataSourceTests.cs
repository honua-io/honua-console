using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Behaviour coverage for <see cref="HonuaServerPublishingWorkspaceDataSource"/> against a recording
/// <see cref="IHonuaContentPublicationClient"/>. Asserts the workspace enumerates the publication
/// list (honua-server#3357) instead of requiring configured ids, endpoint issues surface as capability
/// states rather than throwing or fabricating data, and the republish/rollback lifecycle drives the
/// live verbs and refreshes the review.
/// </summary>
public sealed class HonuaServerPublishingWorkspaceDataSourceTests
{
    [Fact]
    public async Task GetWorkspace_WhenRegistryIsEmpty_ReturnsEmptyWorkspaceWithoutConfigurationState()
    {
        var client = new RecordingPublicationClient();
        var source = new HonuaServerPublishingWorkspaceDataSource(
            client,
            PublishingWorkspaceOptions.FromConfiguredList(null));

        var workspace = await source.GetWorkspaceAsync();

        Assert.Empty(workspace.Matrix);
        Assert.Empty(workspace.Reviews);
        Assert.Empty(workspace.CapabilityStates);
        Assert.Equal(1, client.ListCalls);
        Assert.Equal(0, client.GetCalls);
        Assert.DoesNotContain(workspace.CapabilityStates, state => state.Detail.Contains("PublicationIds", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetWorkspace_ListsPublications_WithoutConfiguredIds()
    {
        var client = new RecordingPublicationClient();
        client.Details["pub-ok"] = Detail("pub-ok", "ok", 1, "ver-1");

        var source = new HonuaServerPublishingWorkspaceDataSource(
            client,
            PublishingWorkspaceOptions.FromConfiguredList(null));

        var workspace = await source.GetWorkspaceAsync();

        Assert.Single(workspace.Matrix);
        Assert.Single(workspace.Reviews);
        Assert.Empty(workspace.CapabilityStates);
        Assert.Equal("pub-ok", workspace.Matrix[0].ResourceId);
        Assert.Equal(0, client.GetCalls);
    }

    [Fact]
    public async Task GetWorkspace_WhenPublicationIdsConfigured_ReadsOnlySelectedItems()
    {
        var client = new RecordingPublicationClient();
        client.Details["pub-ok"] = Detail("pub-ok", "ok", 1, "ver-1");
        client.Details["pub-other"] = Detail("pub-other", "other", 1, "ver-other");

        var source = new HonuaServerPublishingWorkspaceDataSource(
            client,
            PublishingWorkspaceOptions.FromConfiguredList("pub-ok"));

        var workspace = await source.GetWorkspaceAsync();

        var row = Assert.Single(workspace.Matrix);
        Assert.Equal("pub-ok", row.ResourceId);
        Assert.Empty(workspace.CapabilityStates);
        Assert.Equal(0, client.ListCalls);
        Assert.Equal(1, client.GetCalls);
    }

    [Fact]
    public async Task GetWorkspace_WhenSelectedPublicationIsMissing_PreservesItsFailureState()
    {
        var client = new RecordingPublicationClient
        {
            ListIssue = new HonuaAdminEndpointIssue("Unsupported", "GET list", "Old server"),
        };
        client.Details["pub-ok"] = Detail("pub-ok", "ok", 1, "ver-1");
        client.Issues["missing"] = new HonuaAdminEndpointIssue("Unsupported", "GET", "not found", 404);
        var source = new HonuaServerPublishingWorkspaceDataSource(client, PublishingWorkspaceOptions.FromConfiguredList("pub-ok,missing"));

        var workspace = await source.GetWorkspaceAsync();

        Assert.Single(workspace.Matrix);
        Assert.Contains("missing", Assert.Single(workspace.CapabilityStates).Detail, StringComparison.Ordinal);
        Assert.Equal(0, client.ListCalls);
    }

    [Fact]
    public async Task GetWorkspace_WhenPublicationRepeatsAcrossPages_ProducesOneRow()
    {
        var client = new RecordingPublicationClient();
        client.Pages.Enqueue(HonuaAdminEndpointResult<HonuaContentPublicationListPage>.FromData(
            new HonuaContentPublicationListPage { Items = [Detail("pub-1", "one", 1, "ver-1")], NextCursor = "next" }));
        client.Pages.Enqueue(HonuaAdminEndpointResult<HonuaContentPublicationListPage>.FromData(
            new HonuaContentPublicationListPage { Items = [Detail("pub-1", "one", 1, "ver-1")] }));
        var source = new HonuaServerPublishingWorkspaceDataSource(client, PublishingWorkspaceOptions.FromConfiguredList(null));

        Assert.Single((await source.GetWorkspaceAsync()).Matrix);
    }

    [Fact]
    public async Task GetWorkspace_FollowsNextCursor()
    {
        var client = new RecordingPublicationClient();
        client.Pages.Enqueue(HonuaAdminEndpointResult<HonuaContentPublicationListPage>.FromData(
            new HonuaContentPublicationListPage
            {
                Items = [Detail("pub-1", "one", 1, "ver-1")],
                Total = 2,
                NextCursor = "page-2",
            }));
        client.Pages.Enqueue(HonuaAdminEndpointResult<HonuaContentPublicationListPage>.FromData(
            new HonuaContentPublicationListPage
            {
                Items = [Detail("pub-2", "two", 1, "ver-2")],
                Total = 2,
            }));

        var source = new HonuaServerPublishingWorkspaceDataSource(
            client,
            PublishingWorkspaceOptions.FromConfiguredList(null));

        var workspace = await source.GetWorkspaceAsync();

        Assert.Equal(2, workspace.Matrix.Count);
        Assert.Equal(new[] { "pub-1", "pub-2" }, workspace.Matrix.Select(row => row.ResourceId));
        Assert.Equal(2, client.ListCalls);
        Assert.Equal(new string?[] { null, "page-2" }, client.ListCursors);
    }

    [Fact]
    public async Task GetWorkspace_WhenCursorRepeats_SurfacesIncompletePagination()
    {
        var client = new RecordingPublicationClient();
        client.Pages.Enqueue(HonuaAdminEndpointResult<HonuaContentPublicationListPage>.FromData(
            new HonuaContentPublicationListPage
            {
                Items = [Detail("pub-1", "one", 1, "ver-1")],
                Total = 2,
                NextCursor = "again",
            }));
        client.Pages.Enqueue(HonuaAdminEndpointResult<HonuaContentPublicationListPage>.FromData(
            new HonuaContentPublicationListPage
            {
                Items = [Detail("pub-2", "two", 1, "ver-2")],
                Total = 2,
                NextCursor = "again",
            }));
        var source = new HonuaServerPublishingWorkspaceDataSource(
            client,
            PublishingWorkspaceOptions.FromConfiguredList(null));

        var workspace = await source.GetWorkspaceAsync();

        Assert.Empty(workspace.Matrix);
        var state = Assert.Single(workspace.CapabilityStates);
        Assert.Equal("Incomplete pagination", state.State);
        Assert.Equal("GET /api/v1/console/publications", state.Contract);
    }

    [Fact]
    public async Task GetWorkspace_WhenListNeverEnds_SurfacesIncompletePagination()
    {
        var client = new RecordingPublicationClient { ContinueUntilCapped = true };
        var source = new HonuaServerPublishingWorkspaceDataSource(
            client,
            PublishingWorkspaceOptions.FromConfiguredList(null));

        var workspace = await source.GetWorkspaceAsync();

        Assert.Empty(workspace.Matrix);
        var state = Assert.Single(workspace.CapabilityStates);
        Assert.Equal("Incomplete pagination", state.State);
        Assert.Contains("exceeded", state.Detail, StringComparison.Ordinal);
        Assert.Equal(100, client.ListCalls);
    }

    [Fact]
    public async Task GetWorkspace_WhenListFails_SurfacesCapabilityState()
    {
        var client = new RecordingPublicationClient
        {
            ListIssue = new HonuaAdminEndpointIssue("Missing permission", "GET /api/v1/console/publications", "no access", 403),
        };
        var source = new HonuaServerPublishingWorkspaceDataSource(
            client,
            PublishingWorkspaceOptions.FromConfiguredList(null));

        var workspace = await source.GetWorkspaceAsync();

        Assert.Empty(workspace.Matrix);
        var state = Assert.Single(workspace.CapabilityStates);
        Assert.Equal("Missing permission", state.State);
        Assert.Equal("GET /api/v1/console/publications", state.Contract);
        Assert.Contains("HTTP 403", state.Detail, StringComparison.Ordinal);
        Assert.Equal(0, client.GetCalls);
    }

    [Fact]
    public async Task Lookup_NotFound_ReturnsCapabilityStateAndNoReview()
    {
        var client = new RecordingPublicationClient();
        client.Issues["missing"] = new HonuaAdminEndpointIssue("Unsupported", "GET", "not found", 404);
        var source = new HonuaServerPublishingWorkspaceDataSource(
            client,
            PublishingWorkspaceOptions.FromConfiguredList("missing"));

        var result = await source.LookupAsync("missing");

        Assert.False(result.HasReview);
        Assert.Single(result.CapabilityStates);
    }

    [Fact]
    public async Task Republish_SendsTitleAndRefreshesReview()
    {
        var client = new RecordingPublicationClient();
        client.Details["pub-ok"] = Detail("pub-ok", "ok", 2, "ver-2");
        var source = new HonuaServerPublishingWorkspaceDataSource(
            client,
            PublishingWorkspaceOptions.FromConfiguredList("pub-ok"));

        var result = await source.RepublishAsync("pub-ok", new PublishingRepublishCommand(Title: "Refresh"));

        Assert.True(result.HasReview);
        Assert.Equal("Refresh", client.LastRepublish!.Title);
        Assert.Equal(2, result.Versions[0].Revision);
    }

    [Fact]
    public async Task Rollback_WithoutTarget_RejectsBeforeCallingServer()
    {
        var client = new RecordingPublicationClient();
        var source = new HonuaServerPublishingWorkspaceDataSource(
            client,
            PublishingWorkspaceOptions.FromConfiguredList("pub-ok"));

        var result = await source.RollbackAsync("pub-ok", new PublishingRollbackCommand());

        Assert.False(result.HasReview);
        var state = Assert.Single(result.CapabilityStates);
        Assert.Equal("Rejected", state.State);
        Assert.Null(client.LastRollback);
    }

    [Fact]
    public async Task Rollback_WithTarget_SendsTargetVersion()
    {
        var client = new RecordingPublicationClient();
        client.Details["pub-ok"] = Detail("pub-ok", "ok", 1, "ver-1");
        var source = new HonuaServerPublishingWorkspaceDataSource(
            client,
            PublishingWorkspaceOptions.FromConfiguredList("pub-ok"));

        var result = await source.RollbackAsync("pub-ok", new PublishingRollbackCommand(TargetVersionId: "ver-1"));

        Assert.True(result.HasReview);
        Assert.Equal("ver-1", client.LastRollback!.TargetVersionId);
    }

    private static HonuaContentPublicationDetail Detail(string id, string slug, long revision, string versionId) =>
        new()
        {
            Route = new HonuaContentPublicationRouteState
            {
                PublicationId = id,
                RouteSlug = slug,
                RoutePath = $"/published/{slug}",
                Kind = HonuaContentPublicationKinds.Map,
                ActiveVersionId = versionId,
                ActiveRevision = revision,
                Lifecycle = HonuaContentPublicationLifecycles.Active,
                Policy = new HonuaContentPublicationPolicy { Visibility = HonuaContentPublicationVisibilities.Public },
                Etag = $"etag-{revision}",
                UpdatedBy = "operator@honua.test",
                UpdatedAt = DateTimeOffset.Parse("2026-05-29T12:00:00Z")
            },
            Versions =
            [
                new HonuaContentPublicationVersion
                {
                    PublicationId = id,
                    VersionId = versionId,
                    Revision = revision,
                    Kind = HonuaContentPublicationKinds.Map,
                    RouteSlug = slug,
                    RoutePath = $"/published/{slug}",
                    CreatedBy = "operator@honua.test",
                    CreatedAt = DateTimeOffset.Parse("2026-05-29T12:00:00Z")
                }
            ]
        };

    private sealed class RecordingPublicationClient : IHonuaContentPublicationClient
    {
        public Task<HonuaAdminEndpointResult<HonuaReportGenerationProviders>> ListReportGenerationProvidersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(HonuaAdminEndpointResult<HonuaReportGenerationProviders>.FromIssue(
                new HonuaAdminEndpointIssue("Unsupported", "GET providers", "Not exercised by this fake.")));

        public Task<HonuaAdminEndpointResult<HonuaReportGenerationResult>> GenerateReportAsync(GenerateReportContentRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(HonuaAdminEndpointResult<HonuaReportGenerationResult>.FromIssue(
                new HonuaAdminEndpointIssue("Unsupported", "POST generate", "Not exercised by this fake.")));

        public Task<HonuaAdminEndpointResult<HonuaReportGenerationResult>> GenerateDashboardAsync(GenerateDashboardContentRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(HonuaAdminEndpointResult<HonuaReportGenerationResult>.FromIssue(
                new HonuaAdminEndpointIssue("Unsupported", "POST generate", "Not exercised by this fake.")));

        public Uri BaseUri { get; } = new("https://honua.test");

        public Dictionary<string, HonuaContentPublicationDetail> Details { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, HonuaAdminEndpointIssue> Issues { get; } = new(StringComparer.Ordinal);

        public List<string> RequestedIds { get; } = [];

        public int GetCalls { get; private set; }

        public int ListCalls { get; private set; }

        public List<string?> ListCursors { get; } = [];

        public Queue<HonuaAdminEndpointResult<HonuaContentPublicationListPage>> Pages { get; } = new();

        public HonuaAdminEndpointIssue? ListIssue { get; init; }

        public bool ContinueUntilCapped { get; init; }

        public Task<HonuaAdminEndpointResult<HonuaContentPublicationListPage>> ListAsync(
            string? cursor = null,
            int limit = 100,
            string? lifecycle = null,
            CancellationToken cancellationToken = default)
        {
            ListCalls++;
            ListCursors.Add(cursor);
            if (ListIssue is not null)
            {
                return Task.FromResult(HonuaAdminEndpointResult<HonuaContentPublicationListPage>.FromIssue(ListIssue));
            }

            if (ContinueUntilCapped)
            {
                var id = "pub-" + ListCalls.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return Task.FromResult(HonuaAdminEndpointResult<HonuaContentPublicationListPage>.FromData(
                    new HonuaContentPublicationListPage
                    {
                        Items = [Detail(id, id, 1, "ver-" + id)],
                        Total = 100_000,
                        NextCursor = "cursor-" + id,
                    }));
            }

            if (Pages.Count > 0)
            {
                return Task.FromResult(Pages.Dequeue());
            }

            var items = Details.Values.ToArray();
            return Task.FromResult(HonuaAdminEndpointResult<HonuaContentPublicationListPage>.FromData(
                new HonuaContentPublicationListPage { Items = items, Total = items.Length }));
        }

        public HonuaPublishContentRequest? LastPublish { get; private set; }

        public HonuaRepublishContentRequest? LastRepublish { get; private set; }

        public HonuaRollbackContentRequest? LastRollback { get; private set; }

        public HonuaUpdatePublicationPolicyRequest? LastPolicy { get; private set; }

        public Task<HonuaAdminEndpointResult<HonuaContentPublicationDetail>> GetAsync(
            string publicationId,
            CancellationToken cancellationToken = default)
        {
            GetCalls++;
            RequestedIds.Add(publicationId);
            return Task.FromResult(Resolve(publicationId));
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
            LastPublish = request;
            return Task.FromResult(Resolve(request.RouteSlug ?? string.Empty));
        }

        public Task<HonuaAdminEndpointResult<HonuaContentPublicationDetail>> RepublishAsync(
            string publicationId,
            HonuaRepublishContentRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRepublish = request;
            return Task.FromResult(Resolve(publicationId));
        }

        public Task<HonuaAdminEndpointResult<HonuaContentPublicationDetail>> RollbackAsync(
            string publicationId,
            HonuaRollbackContentRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRollback = request;
            return Task.FromResult(Resolve(publicationId));
        }

        public Task<HonuaAdminEndpointResult<HonuaContentPublicationPolicyUpdateResponse>> UpdatePolicyAsync(
            string publicationId,
            HonuaUpdatePublicationPolicyRequest request,
            CancellationToken cancellationToken = default)
        {
            LastPolicy = request;
            var resolved = Resolve(publicationId);
            return Task.FromResult(resolved.Issue is { } issue
                ? HonuaAdminEndpointResult<HonuaContentPublicationPolicyUpdateResponse>.FromIssue(issue)
                : HonuaAdminEndpointResult<HonuaContentPublicationPolicyUpdateResponse>.FromData(
                    new HonuaContentPublicationPolicyUpdateResponse { Route = resolved.Data!.Route }));
        }

        private HonuaAdminEndpointResult<HonuaContentPublicationDetail> Resolve(string publicationId)
        {
            if (Issues.TryGetValue(publicationId, out var issue))
            {
                return HonuaAdminEndpointResult<HonuaContentPublicationDetail>.FromIssue(issue);
            }

            return HonuaAdminEndpointResult<HonuaContentPublicationDetail>.FromData(Details[publicationId]);
        }
    }
}
