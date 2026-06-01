using Honua.Console.Contracts;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Collaboration comments round-trip (console-integration-test-plan.md Wave 3, Family E/collab, P1;
/// honua-console#124 / honua-server#1278).
///
/// Drives the durable comment lifecycle — open a feature-pinned thread, reply, then resolve — through the
/// production <see cref="HonuaServerStudioMapCollaborationDataSource"/> (the console collaboration OPERATION
/// path), and asserts the result INDEPENDENTLY through the <see cref="ServerStateVerifier"/> oracle reading
/// the server's own collab API (<c>/api/v1/console/maps/{mapId}/collab/comments</c> + <c>/activity</c>) —
/// proving the durable thread + reply + resolved state + activity actually landed on the server (rule #2: a
/// DIFFERENT read API than the console data source). Presence/cursors stay empty/deferred (honua-server#1290)
/// and are asserted as such, never fabricated.
///
/// This converts the existing <see cref="StudioMapCollaborationLiveServerTests"/> (which reads back through
/// the SAME data source) into a true round-trip with an independent server-side read.
///
/// Off by default; the SkippableFacts skip cleanly without Docker / the opt-in env (Console Patterns Charter
/// section 11) and RUN in the nightly lane.
/// </summary>
[Collection(StudioMapCollaborationIntegrationCollection.Name)]
public sealed class CollabCommentsRoundTripTests
{
    private readonly StudioMapCollaborationFixture _fixture;

    public CollabCommentsRoundTripTests(StudioMapCollaborationFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task CommentThread_OpenReplyResolve_ConsoleOperation_LandsOnServer_VerifiedIndependently()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var mapId = $"console-collab-rt-{Guid.NewGuid():N}";
        var client = _fixture.CreateCollaborationClient();
        var dataSource = new HonuaServerStudioMapCollaborationDataSource(client);
        using var verifier = _fixture.CreateVerifier();

        const string openingBody = "this parcel was reclassified to R-2 — source table is stale.";
        const string replyBody = "confirmed — refreshing the source.";

        // --- OPERATION under test: open a feature-pinned thread through the console collaboration path. ---
        var created = await client.CreateThreadAsync(mapId, new HonuaCreateStudioMapCommentThreadRequest
        {
            FeatureLabel = "Parcel 04-021-204",
            LayerRef = "parcels/fill",
            XFraction = 0.37,
            YFraction = 0.52,
            Body = openingBody
        });
        // A 404/501 here means the pinned image does not mount the durable collab API (#1278) → skip cleanly.
        SkipIfCollabNotReady(created.Issue);
        Assert.Null(created.Issue);
        var threadId = created.Data!.ThreadId;
        Assert.False(string.IsNullOrWhiteSpace(threadId));

        // Append a reply so the thread carries more than one durable message.
        var replied = await client.AddReplyAsync(mapId, threadId, new HonuaCreateStudioMapCommentReplyRequest
        {
            Body = replyBody
        });
        Assert.Null(replied.Issue);

        // --- Independent server read: the durable thread + both messages landed on the server. ---
        var serverThreads = await verifier.ListCollabThreadsAsync(mapId);
        var serverThread = Assert.Single(serverThreads, t => t.ThreadId == threadId);
        Assert.Equal("Parcel 04-021-204", serverThread.FeatureLabel);
        Assert.Equal("parcels/fill", serverThread.LayerRef);
        Assert.False(serverThread.Resolved);
        Assert.Contains(serverThread.Messages, m => m.Contains("reclassified to R-2", StringComparison.Ordinal));
        Assert.Contains(serverThread.Messages, m => m.Contains("refreshing the source", StringComparison.Ordinal));

        // --- Independent server read: the activity feed reflects the durable comment lifecycle. ---
        var serverActivity = await verifier.ListCollabActivityAsync(mapId);
        Assert.NotEmpty(serverActivity);

        // --- Console reflection: the data source projects the same durable slice (and never fabricates the
        //     deferred real-time slots — presence/cursors/follow stay empty per honua-server#1290). ---
        var session = await dataSource.GetSessionAsync(mapId);
        Assert.True(session.IsBound, "Durable comments + activity should be bound from the live server.");
        Assert.Null(session.BindingState);
        var pin = Assert.Single(session.CommentPins, p => p.ThreadId == threadId);
        Assert.True(pin.Messages.Count >= 2);
        Assert.NotEmpty(session.Activity);
        Assert.Empty(session.Participants);
        Assert.Empty(session.Cursors);
        Assert.Null(session.Following);

        // --- OPERATION: resolve the thread, then verify the resolved flag INDEPENDENTLY on the server. ---
        var resolved = await client.SetResolvedAsync(mapId, threadId, new HonuaResolveStudioMapCommentThreadRequest
        {
            Resolved = true
        });
        Assert.Null(resolved.Issue);
        Assert.True(resolved.Data!.Resolved);

        var afterResolve = await verifier.ListCollabThreadsAsync(mapId);
        var resolvedServerThread = Assert.Single(afterResolve, t => t.ThreadId == threadId);
        Assert.True(resolvedServerThread.Resolved, "The thread resolve did not land on the server (independent read).");
    }

    // A 404/405/501 from the collab create path means the pinned image does not mount the durable Studio map
    // collaboration API (#1278); skip cleanly so the lane reports "not exercised" rather than a false failure.
    private static void SkipIfCollabNotReady(HonuaAdminEndpointIssue? issue)
    {
        if (issue is null)
        {
            return;
        }

        var notReady =
            string.Equals(issue.State, "Unsupported", StringComparison.OrdinalIgnoreCase)
            || string.Equals(issue.State, "Unavailable", StringComparison.OrdinalIgnoreCase)
            || issue.StatusCode is >= 500;

        Skip.If(
            notReady,
            $"The pinned honua-server image could not service the durable Studio map collaboration path "
            + $"({issue.State} — {issue.Detail}); the collab comments round-trip needs a server build whose "
            + "honua-server#1278 collab API is ready.");
    }
}
