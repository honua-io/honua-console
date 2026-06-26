using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Aggregates the pending/active agent-proposed operations Console can witness and
/// authorize into one GIS-department work queue (the approval inbox, issue #193). It
/// composes the existing server-owned proposal sources — the GitOps metadata release
/// proposals (<see cref="IConsoleGitOpsReleaseClient"/>) and the deploy-control operation
/// lifecycle (<see cref="IConsoleDeployApprovalClient"/>) — without introducing a new
/// server contract: the honua-server deploy-control surface exposes no list-all endpoint,
/// so the queue is projected from the deploy operation ids discovered on the release
/// operations in view plus any operator-tracked ids, exactly as the Operate Deploy page
/// does today.
///
/// Each call returns an <see cref="OperateSectionResult{T}"/> carrying a status that
/// drives the shared missing / forbidden / unsupported / unavailable surfaces. Per the
/// Console Patterns Charter (section 11) this binds to a live server through the
/// underlying clients and never to a standing in-memory / mock source; when no
/// environment is bound the read surfaces a missing-binding result rather than seeded
/// data. The inbox is a pure projection — it neither fabricates operations nor mutates
/// them; approve / reject / rollback stay on <see cref="IConsoleDeployApprovalClient"/>
/// so the server's OperatorApprovalGate remains the only safety gate.
/// </summary>
public interface IConsoleApprovalInboxClient
{
    /// <summary>
    /// Projects the current approval inbox: every pending/active deploy-control operation
    /// derived from the release operations in view, plus any operator-tracked operation
    /// ids, classified by GIS-desk ticket type and ordered awaiting-approval-first. An
    /// unreadable proposal source surfaces its status; a partial read (some ids skipped)
    /// surfaces as an allowed result carrying the partial flag and message rather than
    /// failing the whole queue.
    /// </summary>
    Task<OperateSectionResult<ApprovalInboxSnapshot>> GetInboxAsync(
        IReadOnlyList<string> trackedOperationIds,
        CancellationToken cancellationToken = default);
}
