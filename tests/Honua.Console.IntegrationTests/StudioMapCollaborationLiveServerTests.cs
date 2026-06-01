using Honua.Console.Contracts;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Proves the server-bound Studio map collaboration surface renders from live honua-server data (#124 lit up
/// against honua-server#1278, slice 1). Boots a real honua-server via Testcontainers and drives the
/// production <see cref="HonuaStudioMapCollaborationHttpClient"/> + <see cref="HonuaServerStudioMapCollaborationDataSource"/>
/// over the durable collaboration lifecycle: open a feature-pinned thread, reply, then read the thread list +
/// activity feed back through the live data source and assert the durable comments + activity round-trip
/// (presence/cursors stay deferred for honua-server#1290). Docker-unavailable environments skip cleanly.
/// </summary>
[Collection(StudioMapCollaborationIntegrationCollection.Name)]
public sealed class StudioMapCollaborationLiveServerTests
{
    private readonly StudioMapCollaborationFixture _fixture;

    public StudioMapCollaborationLiveServerTests(StudioMapCollaborationFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task Collaboration_OpensThreadAndDrivesCommentsActivityFromLiveServer()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var mapId = $"console-collab-fixture-{Guid.NewGuid():N}";
        var client = _fixture.CreateCollaborationClient();

        // 1. Open a feature-pinned comment thread through the real collaboration API.
        var created = await client.CreateThreadAsync(mapId, new HonuaCreateStudioMapCommentThreadRequest
        {
            FeatureLabel = "Parcel 04-021-204",
            LayerRef = "parcels/fill",
            XFraction = 0.37,
            YFraction = 0.52,
            Body = "this parcel was reclassified to R-2 — source table is stale."
        });
        Assert.Null(created.Issue);
        Assert.NotNull(created.Data);
        var threadId = created.Data!.ThreadId;
        Assert.False(string.IsNullOrWhiteSpace(threadId));

        // 2. Append a reply so the thread carries more than one durable message.
        var replied = await client.AddReplyAsync(mapId, threadId, new HonuaCreateStudioMapCommentReplyRequest
        {
            Body = "confirmed — refreshing the source."
        });
        Assert.Null(replied.Issue);
        Assert.True(replied.Data!.CommentCount >= 2);

        // 3. Read the durable slice back through the production data source (the merged runtime path).
        var dataSource = new HonuaServerStudioMapCollaborationDataSource(client);
        var session = await dataSource.GetSessionAsync(mapId);

        Assert.True(session.IsBound, "Durable comments + activity should be bound from the live server.");
        Assert.Null(session.BindingState);

        var pin = Assert.Single(session.CommentPins, p => p.ThreadId == threadId);
        Assert.Equal("Parcel 04-021-204", pin.FeatureLabel);
        Assert.Equal("parcels/fill", pin.LayerRef);
        Assert.True(pin.Messages.Count >= 2, "The thread should carry the opening message + reply from live data.");
        Assert.Contains(pin.Messages, m => m.Body.Contains("reclassified to R-2", StringComparison.Ordinal));

        // The activity feed reflects the durable comment lifecycle events for this map.
        Assert.NotEmpty(session.Activity);

        // The deferred real-time slots (honua-server#1290) stay empty — never fabricated.
        Assert.Empty(session.Participants);
        Assert.Empty(session.Cursors);
        Assert.Null(session.Following);

        // 4. The single-thread read resolves the same live thread for the drawer.
        var thread = await dataSource.GetThreadAsync(mapId, threadId);
        Assert.NotNull(thread);
        Assert.Equal(threadId, thread!.ThreadId);

        // 5. Resolving the thread round-trips through the live API.
        var resolved = await client.SetResolvedAsync(mapId, threadId, new HonuaResolveStudioMapCommentThreadRequest { Resolved = true });
        Assert.Null(resolved.Issue);
        Assert.True(resolved.Data!.Resolved);
    }
}
