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
    private static readonly Guid NextVersionId = Guid.Parse("44444444-4444-4444-4444-444444444444");
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
        var oldProposal = state.PendingPublication;
        var nextDraft = await source.SaveDraftAsync(state);
        Assert.True(nextDraft.Succeeded);
        Assert.Null(nextDraft.State!.PendingPublication);
        Assert.Equal(oldProposal, nextDraft.State.PreviousPublication);
        var nextSubmission = await source.PublishAsync(nextDraft.State);
        Assert.True(nextSubmission.Succeeded);
        Assert.Equal(NextVersionId, nextSubmission.State!.PendingPublication!.VersionId);
        Assert.Equal(2, handler.SaveCount);
        Assert.Equal(2, handler.PublishCount);

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
        var oldProposal = state.PendingPublication;
        var nextDraft = await source.SaveDraftAsync(state);
        Assert.True(nextDraft.Succeeded);
        Assert.Null(nextDraft.State!.PendingPublication);
        Assert.Equal(oldProposal, nextDraft.State.PreviousPublication);
        var nextSubmission = await source.PublishAsync(nextDraft.State);
        Assert.True(nextSubmission.Succeeded);
        Assert.Equal(NextVersionId, nextSubmission.State!.PendingPublication!.VersionId);
        Assert.Equal(2, handler.SaveCount);
        Assert.Equal(2, handler.PublishCount);

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
        Assert.False(state.IsPublished);
        Assert.Equal(VersionId, state.CurrentVersionId);
        AssertPending(state.PendingPublication, result.Message);
        Assert.Equal(1, handler.SaveCount);
        Assert.Equal(1, handler.PublishCount);
        Assert.All(history.Versions, version => Assert.False(version.IsPublished));
        var oldProposal = state.PendingPublication;
        var nextDraft = await source.SaveDraftAsync(state);
        Assert.True(nextDraft.Succeeded);
        Assert.Null(nextDraft.State!.PendingPublication);
        Assert.Equal(oldProposal, nextDraft.State.PreviousPublication);
        nextDraft.State.ShareEmbedPolicyReviewed = true;
        var nextSubmission = await source.PublishAsync(nextDraft.State);
        Assert.True(nextSubmission.Succeeded);
        Assert.Equal(NextVersionId, nextSubmission.State!.PendingPublication!.VersionId);
        Assert.Equal(2, handler.SaveCount);
        Assert.Equal(2, handler.PublishCount);

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
        Assert.Contains("submission did not change the published version", rendered.Markup, StringComparison.Ordinal);
        Assert.Equal("/approvals", rendered.Find("a").GetAttribute("href"));
        Assert.Equal("Refresh publication", Assert.Single(rendered.FindAll("button")).TextContent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Refresh_UsesActualPointerRatherThanSuccessfulProposal(bool published)
    {
        using var handler = new PublicationHandler { ObservedPublishedId = published ? VersionId : null };
        using var http = Client(handler);
        using var lifecycle = new HttpStudioPackageLifecycleClient(http, new StudioPackageLifecycleClientOptions(http.BaseAddress!));
        var submission = await lifecycle.SubmitPublishRequestAsync(ItemId, VersionId, new());
        var pending = new StudioPendingPublication(ItemId, VersionId, submission.Data!.Operation!);
        var reader = new StudioPublicationStatusReader(lifecycle, new ReadOnlyProposalClient());

        var refreshed = await reader.RefreshAsync(pending);
        var map = new StudioMapEditorState { ItemId = ItemId, VersionId = VersionId, PendingPublication = pending };
        var dashboard = new StudioDashboardEditorState { ItemId = ItemId, CurrentVersionId = VersionId, PendingPublication = pending };
        var app = new StudioAppEditorState { ItemId = ItemId, CurrentVersionId = VersionId, PendingPublication = pending };
        var session = StudioAuthoringSession.Empty with
        {
            Draft = new StudioDraftHandle(Guid.NewGuid().ToString(), ItemId.ToString(), "package", 1, VersionId.ToString()),
            PendingPublication = pending
        };
        refreshed.Apply(map);
        refreshed.Apply(dashboard);
        refreshed.Apply(app);
        session = refreshed.Apply(session);

        Assert.True(refreshed.Succeeded);
        Assert.Equal(published, map.IsPublished);
        Assert.Equal(published, dashboard.IsPublished);
        Assert.Equal(published ? (int?)3 : null, app.PublishedVersion);
        Assert.Equal(published, app.IsPublished);
        Assert.Equal(published ? StudioPackageLifecycleState.Published : StudioPackageLifecycleState.SavedVersion,
            session.ActivePackage.LifecycleState);
        Assert.Null(map.PendingPublication);
        Assert.Null(dashboard.PendingPublication);
        Assert.Null(app.PendingPublication);
        Assert.Null(session.PendingPublication);
        Assert.Equal(ConsoleProposalStatus.Succeeded, map.PreviousPublication!.ProposalStatus);
        Assert.Equal(1, handler.PublishCount); // Refresh never submits/approves another operation.
    }

    private sealed class ReadOnlyProposalClient : IConsoleProposalsClient
    {
        public Task<OperateSectionResult<ConsoleProposalDetail>> GetAsync(string proposalId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperateSectionResult<ConsoleProposalDetail>.Allowed(new ConsoleProposalDetail(
                proposalId, ConsoleProposalKind.Map, ConsoleProposalStatus.Succeeded, "proposer", null, "Publish map",
                [], [], ConsoleProposalRisk.Low, [], [], "RequiresApproval", "reviewer", null, "execution-1",
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)));

        public Task<OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>> ListAsync(
            string? status = null, string? kind = null, string? requestedBy = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<OperateSectionResult<ConsoleProposalDetail>> ApproveAsync(string proposalId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Publication refresh must not approve.");

        public Task<OperateSectionResult<ConsoleProposalDetail>> RejectAsync(string proposalId, string reason, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Publication refresh must not reject.");
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
        public Guid? ObservedPublishedId { get; init; }
        public int SaveCount { get; private set; }
        public int PublishCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return CreateResponse(request, body);
        }

        private HttpResponseMessage CreateResponse(HttpRequestMessage request, string? body)
        {
            object data;
            var status = HttpStatusCode.OK;
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Put)
            {
                using var document = JsonDocument.Parse(body!);
                data = new
                {
                    draftId = Guid.Parse(path.Split('/')[^1]), itemId = ItemId, packageKey = "studio-test",
                    family = document.RootElement.GetProperty("envelope").GetProperty("family").GetString(),
                    generation = 2, envelope = document.RootElement.GetProperty("envelope").Clone(),
                    createdAt = "2026-09-25T12:00:00Z", updatedAt = "2026-09-25T12:00:00Z"
                };
            }
            else if (path.EndsWith("/publish-requests", StringComparison.Ordinal))
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
            else if (path.EndsWith("/content-items", StringComparison.Ordinal))
            {
                data = new
                {
                    total = 1,
                    items = new[] { new { itemId = ItemId, currentVersionId = VersionId, publishedVersionId = ObservedPublishedId } }
                };
            }
            else if (path.Contains("/versions/", StringComparison.Ordinal))
            {
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

        private object Version() => new
        {
            itemId = ItemId, versionId = SaveCount > 1 ? NextVersionId : VersionId, packageKey = "studio-test", versionNumber = SaveCount > 1 ? 4 : 3,
            contentHash = "abc", envelope = new { family = "map", schemaVersion = "honua_map_package.v1" },
            validation = new { status = "valid" }, createdAt = "2026-09-25T12:00:00Z"
        };
    }
}
