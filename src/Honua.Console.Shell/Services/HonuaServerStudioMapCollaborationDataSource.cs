using System.Globalization;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Studio Map collaboration data source bound to the honua-server durable collaboration API
/// (honua-server#1278, slice 1) through the <see cref="IHonuaStudioMapCollaborationClient"/> shim. The
/// durable slice — feature-pinned comment threads (list/reply/resolve) and the collaboration activity feed —
/// is bound live: <see cref="GetSessionAsync"/> reads the real comment threads + activity for the open map
/// draft and renders them in the comment drawer + activity sidebar. There is no in-memory collaboration data
/// in the merged result (Console Patterns Charter section 11).
///
/// The real-time presence/cursor/markup transport is the still-OPEN follow-up slice (honua-server#1290), so
/// presence participants, named cursors, and follow-mode stay empty here and the chrome renders those slots
/// disabled-pending. The session is reported as bound (durable comments + activity are live); a read failure
/// (missing permission, unsupported, transport) surfaces the explicit capability state on the surface instead
/// of throwing or fabricating comments/activity.
/// </summary>
public sealed class HonuaServerStudioMapCollaborationDataSource : IStudioMapCollaborationDataSource
{
    private const string Surface = "Collaboration · comments + activity";
    private const int ActivityLimit = 50;

    private readonly IHonuaStudioMapCollaborationClient _client;

    public HonuaServerStudioMapCollaborationDataSource(IHonuaStudioMapCollaborationClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<StudioMapCollaborationSession> GetSessionAsync(
        string? mapId,
        CancellationToken cancellationToken = default)
    {
        // A not-yet-saved draft has no server identity, so it cannot have durable threads/activity. Report
        // the durable slot unbound (no fabricated comments) rather than calling the server with an empty id.
        if (string.IsNullOrWhiteSpace(mapId))
        {
            return StudioMapCollaborationSession.Unbound(new StudioMapCollaborationState(
                Surface,
                StudioMapCollaborationState.MissingBinding,
                "honua-server#1278",
                "Save the map draft before collaboration comments and activity can bind to the server "
                + "(honua-server#1278). A not-yet-saved draft has no durable collaboration thread."));
        }

        var threadsResult = await _client.ListThreadsAsync(mapId, cancellationToken).ConfigureAwait(false);
        if (threadsResult.Issue is { } threadsIssue)
        {
            return StudioMapCollaborationSession.Unbound(ToCollaborationState(threadsIssue));
        }

        var activityResult = await _client.ListActivityAsync(mapId, ActivityLimit, cancellationToken).ConfigureAwait(false);
        if (activityResult.Issue is { } activityIssue)
        {
            return StudioMapCollaborationSession.Unbound(ToCollaborationState(activityIssue));
        }

        // Server omits empty collections as JSON null; coalesce before LINQ.
        var commentPins = (threadsResult.Data!.Threads ?? []).Select(ToCommentPin).ToArray();
        var activity = (activityResult.Data!.Activity ?? []).Select(ToActivityEntry).ToArray();

        // Durable comments + activity are bound (BindingState null => IsBound). Presence/cursors/follow stay
        // empty: those live slots remain deferred until honua-server#1290 ships the real-time transport.
        return new StudioMapCollaborationSession(
            Participants: [],
            Cursors: [],
            CommentPins: commentPins,
            Activity: activity,
            Following: null,
            BindingState: null);
    }

    public async Task<StudioMapCommentPin?> GetThreadAsync(
        string? mapId,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        if (string.IsNullOrWhiteSpace(mapId))
        {
            return null;
        }

        // The durable contract exposes a thread list (the create/reply/resolve verbs return the single
        // mutated thread); a thread is resolved by reading the list and selecting the requested id, so the
        // drawer always reflects the live message set.
        var threadsResult = await _client.ListThreadsAsync(mapId, cancellationToken).ConfigureAwait(false);
        if (threadsResult.Issue is not null)
        {
            return null;
        }

        var thread = threadsResult.Data!.Threads
            .FirstOrDefault(candidate => string.Equals(candidate.ThreadId, threadId, StringComparison.Ordinal));
        return thread is null ? null : ToCommentPin(thread);
    }

    private static StudioMapCommentPin ToCommentPin(HonuaStudioMapCommentThread thread) =>
        new(
            ThreadId: thread.ThreadId,
            FeatureLabel: thread.FeatureLabel,
            LayerRef: thread.LayerRef,
            CommentCount: thread.CommentCount,
            Resolved: thread.Resolved,
            XFraction: thread.XFraction,
            YFraction: thread.YFraction,
            Messages: thread.Messages
                .Select(message => new StudioMapCommentMessage(
                    message.MessageId,
                    message.AuthorName,
                    message.AuthorInitials,
                    message.AuthorColor,
                    message.RelativeTime,
                    message.Body))
                .ToArray());

    private static StudioMapActivityEntry ToActivityEntry(HonuaStudioMapActivityEntry entry) =>
        new(
            ParticipantName: entry.ParticipantName,
            Initials: entry.Initials,
            Color: entry.Color,
            RelativeTime: entry.RelativeTime,
            Action: entry.Action);

    private static StudioMapCollaborationState ToCollaborationState(HonuaAdminEndpointIssue issue) =>
        new(
            Surface,
            issue.State,
            issue.Contract,
            issue.StatusCode is null
                ? issue.Detail
                : $"{issue.Detail} HTTP {issue.StatusCode.Value.ToString(CultureInfo.InvariantCulture)}.");
}
