using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Behaviour coverage for <see cref="HonuaServerPublishingWorkspaceDataSource"/> against a recording
/// <see cref="IHonuaContentPublicationClient"/>. Asserts the workspace is keyed by configured
/// publication ids (the registry has no list endpoint), endpoint issues surface as capability states
/// rather than throwing or fabricating data, and the republish/rollback lifecycle drives the live
/// verbs and refreshes the review.
/// </summary>
public sealed class HonuaServerPublishingWorkspaceDataSourceTests
{
    [Fact]
    public async Task GetWorkspace_WhenNoPublicationIdsConfigured_RendersNotConfiguredStateWithoutData()
    {
        var client = new RecordingPublicationClient();
        var source = new HonuaServerPublishingWorkspaceDataSource(
            client,
            PublishingWorkspaceOptions.FromConfiguredList(null));

        var workspace = await source.GetWorkspaceAsync();

        Assert.Empty(workspace.Matrix);
        Assert.Empty(workspace.Reviews);
        var state = Assert.Single(workspace.CapabilityStates);
        Assert.Equal("Not configured", state.State);
        Assert.Equal(0, client.GetCalls);
    }

    [Fact]
    public async Task GetWorkspace_ReadsEachConfiguredId_AndSurfacesIssuesAsCapabilityStates()
    {
        var client = new RecordingPublicationClient();
        client.Details["pub-ok"] = Detail("pub-ok", "ok", 1, "ver-1");
        client.Issues["pub-denied"] = new HonuaAdminEndpointIssue("Missing permission", "GET", "no access", 403);

        var source = new HonuaServerPublishingWorkspaceDataSource(
            client,
            PublishingWorkspaceOptions.FromConfiguredList("pub-ok, pub-denied"));

        var workspace = await source.GetWorkspaceAsync();

        Assert.Single(workspace.Matrix);
        Assert.Single(workspace.Reviews);
        var state = Assert.Single(workspace.CapabilityStates);
        Assert.Equal("Missing permission", state.State);
        Assert.Contains("pub-denied", state.Detail, StringComparison.Ordinal);
        Assert.Equal(new[] { "pub-ok", "pub-denied" }, client.RequestedIds);
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
        public Uri BaseUri { get; } = new("https://honua.test");

        public Dictionary<string, HonuaContentPublicationDetail> Details { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, HonuaAdminEndpointIssue> Issues { get; } = new(StringComparer.Ordinal);

        public List<string> RequestedIds { get; } = [];

        public int GetCalls { get; private set; }

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
