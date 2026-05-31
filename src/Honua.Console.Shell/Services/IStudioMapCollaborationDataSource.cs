using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Studio Map multiplayer collaboration data source (honua-console#124). Streams the live session for an
/// open map draft: presence participants, named cursors, the per-draft markup layer, feature-pinned comment
/// threads, follow-mode, and the activity feed (mockup: <c>StudioMapCollab</c> + <c>MapCommentThread</c>).
///
/// There is no honua-server collaboration/presence/comments contract yet, so the merged build registers
/// only <see cref="UnsupportedStudioMapCollaborationDataSource"/>, which returns an explicit
/// missing-binding state from every operation. Console renders the full collaboration chrome but never
/// fabricates presence, cursors, comments, or activity from a standing mock (Console Patterns Charter
/// section 11). When the server contract lands, a live HTTP-bound implementation slots in here behind the
/// Honua.Console.Contracts shim, gated on a configured server base URL exactly like the other Studio
/// bindings.
/// </summary>
public interface IStudioMapCollaborationDataSource
{
    /// <summary>
    /// Loads the live collaboration session for an open map draft, or the explicit missing-binding state
    /// when no collaboration contract is bound. <paramref name="mapId"/> may be null for a not-yet-saved
    /// draft, which still cannot have a live session until the server contract exists.
    /// </summary>
    Task<StudioMapCollaborationSession> GetSessionAsync(
        string? mapId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the full message list for one comment thread (drawer body). Returns the missing-binding state
    /// inside an unbound session when the contract is not bound.
    /// </summary>
    Task<StudioMapCommentPin?> GetThreadAsync(
        string? mapId,
        string threadId,
        CancellationToken cancellationToken = default);
}
