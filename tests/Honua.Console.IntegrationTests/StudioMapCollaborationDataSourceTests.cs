using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free coverage for the live <see cref="HonuaServerStudioMapCollaborationDataSource"/> (#124 lit up
/// against honua-server#1278 slice 1). Drives the data source over a stub
/// <see cref="IHonuaStudioMapCollaborationClient"/> (never a real server) to assert: the durable comments +
/// activity reads project onto the Console view models and report a BOUND session; the deferred real-time
/// slots (presence/cursors/follow, honua-server#1290) stay empty; a server-rejected read surfaces the
/// explicit capability state (unbound) rather than fabricating comments; a not-yet-saved draft reports
/// unbound; and the bound session renders the live comment drawer + activity feed in the
/// <see cref="StudioMapCollaboration"/> component.
/// </summary>
public sealed class StudioMapCollaborationDataSourceTests
{
    [Fact]
    public async Task GetSession_WhenServerReturnsThreadsAndActivity_BindsDurableSliceLeavesRealtimeDeferred()
    {
        var client = new StubCollaborationClient
        {
            Threads = new HonuaStudioMapCommentThreadList
            {
                MapId = "map-1",
                Threads =
                [
                    new HonuaStudioMapCommentThread
                    {
                        ThreadId = "thread-1",
                        FeatureLabel = "Parcel 04-021-204",
                        LayerRef = "parcels/fill",
                        CommentCount = 2,
                        Resolved = false,
                        XFraction = 0.37,
                        YFraction = 0.52,
                        Messages =
                        [
                            new HonuaStudioMapCommentMessage
                            {
                                MessageId = "m1",
                                AuthorName = "a.lee",
                                AuthorInitials = "AL",
                                AuthorColor = "#1d6b3e",
                                RelativeTime = "14m ago",
                                Body = "reclassified to R-2"
                            }
                        ]
                    }
                ]
            },
            Activity = new HonuaStudioMapActivityList
            {
                MapId = "map-1",
                Activity =
                [
                    new HonuaStudioMapActivityEntry
                    {
                        ParticipantName = "k.tan",
                        Initials = "KT",
                        Color = "#2a6fdb",
                        RelativeTime = "just now",
                        Action = "opened a comment on Parcel 04-021-204"
                    }
                ]
            }
        };
        var dataSource = new HonuaServerStudioMapCollaborationDataSource(client);

        var session = await dataSource.GetSessionAsync("map-1");

        // Durable slice is bound — no missing-binding state.
        Assert.True(session.IsBound);
        Assert.Null(session.BindingState);

        // Comments + activity project from the live read.
        var pin = Assert.Single(session.CommentPins);
        Assert.Equal("thread-1", pin.ThreadId);
        Assert.Equal("Parcel 04-021-204", pin.FeatureLabel);
        Assert.Equal("reclassified to R-2", Assert.Single(pin.Messages).Body);
        var activity = Assert.Single(session.Activity);
        Assert.Equal("k.tan", activity.ParticipantName);

        // Real-time slots stay deferred (honua-server#1290) — empty, never fabricated.
        Assert.Empty(session.Participants);
        Assert.Empty(session.Cursors);
        Assert.Null(session.Following);
    }

    [Fact]
    public async Task GetSession_WhenServerRejectsRead_SurfacesCapabilityStateUnbound()
    {
        var client = new StubCollaborationClient
        {
            ThreadsIssue = new HonuaAdminEndpointIssue(
                "Missing permission",
                "GET /api/v1/console/maps/{mapId}/collab/comments",
                "The current principal lacks permission to read map collaboration comments.",
                403)
        };
        var dataSource = new HonuaServerStudioMapCollaborationDataSource(client);

        var session = await dataSource.GetSessionAsync("map-1");

        Assert.False(session.IsBound);
        Assert.NotNull(session.BindingState);
        Assert.Equal("Missing permission", session.BindingState!.State);
        Assert.Empty(session.CommentPins);
        Assert.Empty(session.Activity);
    }

    [Fact]
    public async Task GetSession_WhenDraftNotSaved_ReportsUnboundWithoutCallingServer()
    {
        var client = new StubCollaborationClient();
        var dataSource = new HonuaServerStudioMapCollaborationDataSource(client);

        var session = await dataSource.GetSessionAsync(mapId: null);

        Assert.False(session.IsBound);
        Assert.Equal(0, client.ListThreadsCalls);
        Assert.Contains("Save the map draft", session.BindingState!.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetThread_ResolvesRequestedThreadFromLiveList()
    {
        var client = new StubCollaborationClient
        {
            Threads = new HonuaStudioMapCommentThreadList
            {
                MapId = "map-1",
                Threads =
                [
                    new HonuaStudioMapCommentThread { ThreadId = "thread-1", FeatureLabel = "A", LayerRef = "l", Messages = [] },
                    new HonuaStudioMapCommentThread { ThreadId = "thread-2", FeatureLabel = "B", LayerRef = "l", Messages = [] }
                ]
            }
        };
        var dataSource = new HonuaServerStudioMapCollaborationDataSource(client);

        var thread = await dataSource.GetThreadAsync("map-1", "thread-2");

        Assert.NotNull(thread);
        Assert.Equal("B", thread!.FeatureLabel);
    }

    [Fact]
    public void BoundLiveSession_RendersCommentDrawerAndActivityFeed()
    {
        var client = new StubCollaborationClient
        {
            Threads = new HonuaStudioMapCommentThreadList
            {
                MapId = "map-1",
                Threads =
                [
                    new HonuaStudioMapCommentThread
                    {
                        ThreadId = "thread-1",
                        FeatureLabel = "Parcel 04-021-204",
                        LayerRef = "parcels/fill",
                        CommentCount = 1,
                        XFraction = 0.37,
                        YFraction = 0.52,
                        Messages =
                        [
                            new HonuaStudioMapCommentMessage
                            {
                                MessageId = "m1",
                                AuthorName = "a.lee",
                                AuthorInitials = "AL",
                                AuthorColor = "#1d6b3e",
                                RelativeTime = "14m ago",
                                Body = "reclassified to R-2"
                            }
                        ]
                    }
                ]
            },
            Activity = new HonuaStudioMapActivityList
            {
                MapId = "map-1",
                Activity =
                [
                    new HonuaStudioMapActivityEntry
                    {
                        ParticipantName = "k.tan",
                        Initials = "KT",
                        Color = "#2a6fdb",
                        RelativeTime = "just now",
                        Action = "opened a comment on Parcel 04-021-204"
                    }
                ]
            }
        };

        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioMapCollaborationDataSource>(new HonuaServerStudioMapCollaborationDataSource(client));

        var component = ctx.RenderComponent<StudioMapCollaboration>(parameters => parameters
            .Add(p => p.MapId, "map-1")
            .Add(p => p.Section, "activity"));

        // The durable slice renders live — no missing-binding state.
        component.WaitForAssertion(
            () => Assert.Equal("live", component.Find("[data-map-collab]").GetAttribute("data-map-collab")),
            TimeSpan.FromSeconds(5));
        Assert.Empty(component.FindAll("section.console-state-missing"));
        Assert.Contains("opened a comment on Parcel 04-021-204", component.Markup, StringComparison.Ordinal);

        // The comment thread (pin) is bound from live data.
        Assert.Single(component.FindAll("[data-collab-pin]"));

        // The DURABLE read surface is live, but the deferred real-time + write affordances stay
        // disabled-pending (honua-server#1290) — never enabled no-op controls in the live-server scenario.
        Assert.All(
            component.FindAll("[data-collab-markup-tool]"),
            tool => Assert.True(tool.HasAttribute("disabled"), "Markup tools must stay disabled-pending while real-time is deferred."));
        Assert.True(component.Find(".studio-map-collab-invite").HasAttribute("disabled"));
        Assert.All(
            component.FindAll(".studio-map-collab-rail-compose button"),
            button => Assert.True(button.HasAttribute("disabled"), "Compose/send must stay disabled-pending while writes are deferred."));
    }

    [Fact]
    public async Task GetSession_WhenDurableBound_LeavesRealtimeUnbound()
    {
        var client = new StubCollaborationClient();
        var dataSource = new HonuaServerStudioMapCollaborationDataSource(client);

        var session = await dataSource.GetSessionAsync("map-1");

        // Durable comments + activity are bound, but real-time presence/cursors/markup/writes are deferred.
        Assert.True(session.IsBound);
        Assert.False(session.IsRealtimeBound);
    }

    [Fact]
    public void Unbound_WhenServerRejectsRead_RendersMissingBindingState()
    {
        var client = new StubCollaborationClient
        {
            ThreadsIssue = new HonuaAdminEndpointIssue(
                "Unsupported",
                "GET /api/v1/console/maps/{mapId}/collab/comments",
                "The Honua server does not expose the durable Studio map collaboration API (honua-server#1278).",
                404)
        };

        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioMapCollaborationDataSource>(new HonuaServerStudioMapCollaborationDataSource(client));

        var component = ctx.RenderComponent<StudioMapCollaboration>(parameters => parameters
            .Add(p => p.MapId, "map-1")
            .Add(p => p.Section, "comments"));

        component.WaitForAssertion(
            () => Assert.Equal("missing-binding", component.Find("[data-map-collab]").GetAttribute("data-map-collab")),
            TimeSpan.FromSeconds(5));
        Assert.NotNull(component.Find("section.console-state-missing"));
    }

    private sealed class StubCollaborationClient : IHonuaStudioMapCollaborationClient
    {
        public Uri BaseUri { get; } = new("https://honua.test");

        public HonuaStudioMapCommentThreadList Threads { get; set; } = new() { MapId = "map-1", Threads = [] };

        public HonuaStudioMapActivityList Activity { get; set; } = new() { MapId = "map-1", Activity = [] };

        public HonuaAdminEndpointIssue? ThreadsIssue { get; set; }

        public HonuaAdminEndpointIssue? ActivityIssue { get; set; }

        public int ListThreadsCalls { get; private set; }

        public Task<HonuaAdminEndpointResult<HonuaStudioMapCommentThreadList>> ListThreadsAsync(string mapId, CancellationToken cancellationToken = default)
        {
            ListThreadsCalls++;
            return Task.FromResult(ThreadsIssue is { } issue
                ? HonuaAdminEndpointResult<HonuaStudioMapCommentThreadList>.FromIssue(issue)
                : HonuaAdminEndpointResult<HonuaStudioMapCommentThreadList>.FromData(Threads));
        }

        public Task<HonuaAdminEndpointResult<HonuaStudioMapActivityList>> ListActivityAsync(string mapId, int? limit = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(ActivityIssue is { } issue
                ? HonuaAdminEndpointResult<HonuaStudioMapActivityList>.FromIssue(issue)
                : HonuaAdminEndpointResult<HonuaStudioMapActivityList>.FromData(Activity));

        public Task<HonuaAdminEndpointResult<HonuaStudioMapCommentThread>> CreateThreadAsync(string mapId, HonuaCreateStudioMapCommentThreadRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<HonuaAdminEndpointResult<HonuaStudioMapCommentThread>> AddReplyAsync(string mapId, string threadId, HonuaCreateStudioMapCommentReplyRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<HonuaAdminEndpointResult<HonuaStudioMapCommentThread>> SetResolvedAsync(string mapId, string threadId, HonuaResolveStudioMapCommentThreadRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
