namespace Honua.Console.Shell.Models;

/// <summary>
/// Console-owned view models for the Studio Map multiplayer collaboration surface (honua-console#124):
/// live presence, named cursors, a per-draft collaborative markup layer, feature-pinned comment threads,
/// a follow-mode session, and the live activity feed (mockup: <c>StudioMapCollab</c> + <c>MapCommentThread</c>
/// in <c>docs/design-handoff/console-canvas/screens-collab-rbac.jsx</c>).
///
/// These are NOT server wire contracts. There is no honua-server collaboration/presence/comments API yet
/// (tracked by the filed honua-server issue noted in <see cref="Services.UnsupportedStudioMapCollaborationDataSource"/>),
/// so the merged build registers only the unsupported implementation, which surfaces an explicit
/// missing-binding state from every operation. Console never fabricates presence, cursors, comments, or
/// activity from a standing mock (Console Patterns Charter section 11). The structural chrome (tabs,
/// presence stack, markup tool rail, comment drawer, activity sidebar) renders against these record types;
/// the live data slots stay empty behind the missing-binding state until the server contract lands.
/// </summary>
public sealed record StudioMapCollaborationSession(
    IReadOnlyList<StudioMapPresenceParticipant> Participants,
    IReadOnlyList<StudioMapLiveCursor> Cursors,
    IReadOnlyList<StudioMapCommentPin> CommentPins,
    IReadOnlyList<StudioMapActivityEntry> Activity,
    StudioMapFollowState? Following,
    StudioMapCollaborationState? BindingState)
{
    /// <summary>True when no real collaboration contract is bound; the surface must render the explanation.</summary>
    public bool IsBound => BindingState is null;

    /// <summary>An empty, explicitly-unbound session — the only state the merged build returns today.</summary>
    public static StudioMapCollaborationSession Unbound(StudioMapCollaborationState state) =>
        new([], [], [], [], null, state);
}

/// <summary>
/// A neutral binding/permission state rendered above the collaboration chrome. Mirrors the temporal and
/// Studio capability-state vocabulary so a missing collaboration contract renders a capability explanation
/// rather than an empty surface or fabricated presence.
/// </summary>
public sealed record StudioMapCollaborationState(
    string Surface,
    string State,
    string Contract,
    string Detail)
{
    /// <summary>The collaboration/presence/comments server contract is not configured / has not landed.</summary>
    public const string MissingBinding = "Missing binding";
}

/// <summary>
/// A collaborator present in the draft's live session: the presence avatar stack and the "N here" count.
/// <see cref="Activity"/> describes what they are doing (editing a layer / commenting), driving the
/// presence dot colour in the mockup.
/// </summary>
public sealed record StudioMapPresenceParticipant(
    string ParticipantId,
    string DisplayName,
    string Initials,
    string Color,
    StudioMapPresenceActivity Activity,
    string? EditingLayer = null);

/// <summary>What a presence participant is currently doing, mapped to the avatar status dot in the mockup.</summary>
public enum StudioMapPresenceActivity
{
    Viewing,
    Editing,
    Commenting,
}

/// <summary>A named live cursor for another collaborator, positioned over the map canvas (server-streamed).</summary>
public sealed record StudioMapLiveCursor(
    string ParticipantId,
    string DisplayName,
    string Color,
    double XFraction,
    double YFraction,
    string? Label = null);

/// <summary>
/// A feature-pinned comment thread anchor rendered as a pin over the map. The thread survives map edits and
/// re-anchors if the feature moves (mockup: <c>MapCommentThread</c> anchor metadata).
/// </summary>
public sealed record StudioMapCommentPin(
    string ThreadId,
    string FeatureLabel,
    string LayerRef,
    int CommentCount,
    bool Resolved,
    double XFraction,
    double YFraction,
    IReadOnlyList<StudioMapCommentMessage> Messages);

/// <summary>A single message in a comment thread.</summary>
public sealed record StudioMapCommentMessage(
    string MessageId,
    string AuthorName,
    string AuthorInitials,
    string AuthorColor,
    string RelativeTime,
    string Body);

/// <summary>A live activity-feed entry (right rail).</summary>
public sealed record StudioMapActivityEntry(
    string ParticipantName,
    string Initials,
    string Color,
    string RelativeTime,
    string Action);

/// <summary>The active follow-mode session ("Following k.tan · ⎋ to stop"); null when not following.</summary>
public sealed record StudioMapFollowState(
    string ParticipantId,
    string DisplayName,
    string Initials,
    string Color);

/// <summary>
/// The collaborative markup tools advertised in the layer rail (arrow, rect, circle, free draw, note, text).
/// Markup is per-draft and never touches the canonical style. These are static UI affordances — selecting a
/// tool only takes effect once the live markup layer is bound, so the chrome renders them disabled-pending
/// behind the missing-binding state.
/// </summary>
public static class StudioMapMarkupTools
{
    public static readonly IReadOnlyList<(string Id, string Label)> All =
    [
        ("arrow", "Arrow"),
        ("rect", "Rectangle"),
        ("circle", "Circle"),
        ("free", "Free draw"),
        ("note", "Sticky note"),
        ("text", "Text"),
    ];
}
