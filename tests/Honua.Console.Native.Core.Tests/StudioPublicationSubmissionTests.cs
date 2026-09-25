using System.Net;
using System.Text;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Bunit;

namespace Honua.Console.Native.Core.Tests;

public sealed class StudioPublicationSubmissionTests
{
    private static readonly Guid ItemId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid VersionId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task Map_PendingApproval_KeepsSavedIdentityAndDoesNotResubmit()
    {
        using var handler = new PublicationHandler();
        using var http = Client(handler);
        using var lifecycle = new HttpStudioPackageLifecycleClient(http, new StudioPackageLifecycleClientOptions(http.BaseAddress!));
        var source = new HonuaServerStudioMapPackageDataSource(lifecycle, new NoopStudioMapGenerationClient(), new UnsupportedOperateTransitionDataSource());
        var state = new StudioMapEditorState
        {
            DraftId = Guid.NewGuid(), Title = "Map", Basemap = "basemap:streets",
            InitialExtent = "-120,30,-110,40", ShareTier = "private", ReopenedFromVersion = 2
        };
        state.Layers.Add(new StudioMapLayerEditor { SourceRef = "content:roads@v1", Title = "Roads" });

        var result = await source.PublishAsync(state);
        var again = await source.PublishAsync(state);

        Assert.True(result.Succeeded);
        Assert.True(again.Succeeded);
        Assert.False(state.IsPublished);
        Assert.Equal(VersionId, state.VersionId);
        Assert.Equal(3, state.Version);
        Assert.Equal(2, state.ReopenedFromVersion);
        AssertPending(state.PendingPublication, result.Message);
        Assert.Equal(1, handler.SaveCount);
        Assert.Equal(1, handler.PublishCount);
    }

    [Fact]
    public async Task Dashboard_PendingApproval_PreservesPreviousPublishedVersion()
    {
        using var handler = new PublicationHandler();
        using var http = Client(handler);
        using var lifecycle = new HttpStudioPackageLifecycleClient(http, new StudioPackageLifecycleClientOptions(http.BaseAddress!));
        var source = new HonuaServerStudioDashboardPackageDataSource(lifecycle);
        var state = new StudioDashboardEditorState
        {
            DraftId = Guid.NewGuid(), Title = "Dashboard", PublishedVersion = 2
        };
        state.Bindings.Add(new StudioDashboardBindingEditor { Alias = "roads", ContentRef = "content:roads", VersionPin = "v1" });
        state.Panels.Add(new StudioDashboardPanelEditor
        {
            Title = "Road count", Kind = StudioDashboardPanelKinds.Chart, BindingAlias = "roads",
            VegaLiteSpec = StudioDashboardChartSpec.DefaultBarChart("district", "count")
        });

        var result = await source.PublishAsync(state);
        await source.PublishAsync(state);

        Assert.True(result.Succeeded);
        Assert.False(state.IsPublished);
        Assert.Equal(2, state.PublishedVersion);
        Assert.Equal(VersionId, state.CurrentVersionId);
        Assert.Equal(3, state.Version);
        AssertPending(state.PendingPublication, result.Message);
        Assert.Equal(1, handler.SaveCount);
        Assert.Equal(1, handler.PublishCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(2)]
    public async Task App_PendingApproval_PreservesPublishedPointerAndDoesNotLabelNewestHistoryPublished(int? previousPublished)
    {
        using var handler = new PublicationHandler();
        using var http = Client(handler);
        using var lifecycle = new HttpStudioPackageLifecycleClient(http, new StudioPackageLifecycleClientOptions(http.BaseAddress!));
        var source = new HonuaServerStudioAppPackageDataSource(lifecycle);
        var state = StudioAppPackageMapper.CreateTemplate();
        state.Title = "App";
        state.DraftId = Guid.NewGuid();
        state.PublishedVersion = previousPublished;
        state.Pages[0].ContentBinding = "content:roads@v1";
        state.ShareEmbedPolicyReviewed = true;

        var result = await source.PublishAsync(state);
        await source.PublishAsync(state);
        var history = await source.LoadVersionHistoryAsync(ItemId);

        Assert.True(result.Succeeded);
        Assert.Equal(previousPublished, state.PublishedVersion);
        Assert.Equal(VersionId, state.CurrentVersionId);
        AssertPending(state.PendingPublication, result.Message);
        Assert.Equal(1, handler.SaveCount);
        Assert.Equal(1, handler.PublishCount);
        Assert.All(history.Versions, version => Assert.False(version.IsPublished));
    }

    [Fact]
    public async Task ApprovalNotice_ShowsProposalAndAuthorizedReviewPath()
    {
        using var handler = new PublicationHandler();
        using var http = Client(handler);
        using var lifecycle = new HttpStudioPackageLifecycleClient(http, new StudioPackageLifecycleClientOptions(http.BaseAddress!));
        var submission = await lifecycle.SubmitPublishRequestAsync(ItemId, VersionId, new());
        using var context = new BunitContext();
        var rendered = context.Render<StudioPublicationApprovalNotice>(parameters => parameters
            .Add(component => component.Pending, new StudioPendingPublication(ItemId, VersionId, submission.Data!.Operation!)));

        Assert.Contains("Awaiting approval", rendered.Markup, StringComparison.Ordinal);
        Assert.Contains("proposal-1", rendered.Markup, StringComparison.Ordinal);
        Assert.Contains("published version is unchanged", rendered.Markup, StringComparison.Ordinal);
        Assert.Equal("/approvals", rendered.Find("a").GetAttribute("href"));
        Assert.Empty(rendered.FindAll("button"));
    }

    private static void AssertPending(StudioPendingPublication? pending, string message)
    {
        Assert.NotNull(pending);
        Assert.Equal(ItemId, pending.ItemId);
        Assert.Equal(VersionId, pending.VersionId);
        Assert.Equal("proposal-1", pending.Operation.ProposalId);
        Assert.Contains("awaiting approval", message, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpClient Client(HttpMessageHandler handler) => new(handler)
    {
        BaseAddress = new Uri("https://server.example")
    };

    private sealed class PublicationHandler : HttpMessageHandler
    {
        public int SaveCount { get; private set; }
        public int PublishCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = CreateResponse(request);
            return Task.FromResult(response);
        }

        private HttpResponseMessage CreateResponse(HttpRequestMessage request)
        {
            object data;
            var status = HttpStatusCode.OK;
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/publish-requests", StringComparison.Ordinal))
            {
                PublishCount++;
                status = HttpStatusCode.Accepted;
                data = new
                {
                    operationInstanceId = "invocation-1", operationId = "studio.content.create-publication-request",
                    status = 4, correlationId = "correlation-1", proposalId = "proposal-1",
                    createdAt = "2026-09-25T12:00:00Z", updatedAt = "2026-09-25T12:00:00Z"
                };
            }
            else if (path.EndsWith("/content-versions", StringComparison.Ordinal))
            {
                SaveCount++;
                status = HttpStatusCode.Created;
                data = Version();
            }
            else
            {
                Assert.EndsWith("/versions", path, StringComparison.Ordinal);
                data = new { itemId = ItemId, versions = new[] { Version() } };
            }

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { success = true, data }), Encoding.UTF8, "application/json")
            };
        }

        private static object Version() => new
        {
            itemId = ItemId, versionId = VersionId, packageKey = "studio-test", versionNumber = 3,
            contentHash = "abc", envelope = new { family = "map", schemaVersion = "honua_map_package.v1" },
            validation = new { status = "valid" }, createdAt = "2026-09-25T12:00:00Z"
        };
    }
}
