using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Collaboration data source used in the merged build today. There is no honua-server
/// collaboration/presence/comments API yet (filed: honua-server#1278 — Studio Map multiplayer
/// presence + collaborative markup + feature-pinned comments + live activity), so EVERY surface renders
/// an explicit missing-binding state instead of fabricating presence, cursors, comments, or activity.
/// This keeps the merged runtime free of a standing in-memory collaboration client (Console Patterns
/// Charter section 11). Unlike the other Studio data sources, this one is unsupported even when a server
/// base URL is configured, because the contract has not landed; when honua-server#1278 ships, register a
/// live HTTP-bound implementation here gated on the configured base URL.
/// </summary>
public sealed class UnsupportedStudioMapCollaborationDataSource : IStudioMapCollaborationDataSource
{
    private static readonly StudioMapCollaborationState MissingBinding = new(
        "Collaboration · live",
        StudioMapCollaborationState.MissingBinding,
        "honua-server#1278",
        "Live multiplayer collaboration is not bound. Presence, named cursors, the shared markup layer, "
            + "feature-pinned comments, and the activity feed activate once honua-server exposes the "
            + "collaboration/presence + comments API (honua-server#1278). Until then this draft is edited "
            + "single-player and no presence, cursors, or comments are fabricated.");

    public Task<StudioMapCollaborationSession> GetSessionAsync(
        string? mapId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(StudioMapCollaborationSession.Unbound(MissingBinding));

    public Task<StudioMapCommentPin?> GetThreadAsync(
        string? mapId,
        string threadId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<StudioMapCommentPin?>(null);
}
