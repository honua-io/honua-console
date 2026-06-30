using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Projects the agent-proposed operations Console can witness and authorize into one
/// GIS-department work queue (the approval inbox, issue #193). It reads the first-class
/// honua-server proposals API (<see cref="IConsoleProposalsClient"/>, honua-server #1694),
/// classifies each proposal onto a GIS-desk ticket type, and orders the queue
/// awaiting-approval-first.
///
/// Each call returns an <see cref="OperateSectionResult{T}"/> carrying a status that drives
/// the shared missing / forbidden / unsupported / unavailable surfaces. Per the Console
/// Patterns Charter (section 11) this binds to a live server through the underlying client and
/// never to a standing in-memory / mock source; when no environment is bound the read surfaces
/// a missing-binding result rather than seeded data. The inbox is a pure projection — it
/// neither fabricates proposals nor mutates them; approve / reject stay on
/// <see cref="IConsoleProposalsClient"/> so the server's approval gate remains the only safety
/// gate.
/// </summary>
public interface IConsoleApprovalInboxClient
{
    /// <summary>
    /// Projects the current approval inbox from the server proposals list, classified by
    /// GIS-desk ticket type and ordered awaiting-approval-first. Optionally filtered by the
    /// server's proposal status and kind query parameters.
    /// </summary>
    Task<OperateSectionResult<ApprovalInboxSnapshot>> GetInboxAsync(
        string? status = null,
        string? kind = null,
        CancellationToken cancellationToken = default);
}
