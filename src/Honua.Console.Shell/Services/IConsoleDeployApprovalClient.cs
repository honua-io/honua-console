using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Reads and actuates the honua-server deploy-control operation lifecycle for the
/// human-in-the-loop approval surface (Phase 1 actuation spine). An agent
/// creates-and-pauses an operation on <c>pr-first</c>; this client lets an operator
/// review it in Console and approve (submit), reject, or roll back through the
/// server's governed deploy-control endpoints — the server's
/// <c>OperatorApprovalGate</c> remains the real safety gate and is never bypassed.
///
/// Each call returns an <see cref="OperateSectionResult{T}"/> carrying a status that
/// drives the shared missing / forbidden / unsupported / unavailable surfaces. Per
/// the Console Patterns Charter (section 11), this binds to a live server and never
/// to a standing in-memory/mock source; when no environment is bound the read returns
/// a missing-binding result rather than seeded data.
///
/// The server now has a real paged list endpoint (<see cref="IConsoleDeployOperationsClient"/>,
/// console#290, honua-server PR #2577); <see cref="ListPendingAsync"/> remains as the tracked-id
/// fallback for an older server where that list is unsupported, or for a caller that already
/// knows the specific ids it cares about (e.g. the deployOperationId of release operations in
/// view). With an empty id set it returns an empty allowed result; it never fabricates
/// operations.
/// </summary>
public interface IConsoleDeployApprovalClient
{
    /// <summary>
    /// Reads a single deploy-control operation by its durable id and projects it onto
    /// the approval-surface proposal contract.
    /// </summary>
    Task<OperateSectionResult<DeployOperationProposal>> GetOperationAsync(
        string operationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Projects the supplied deploy-control operation ids into proposals, filtered to
    /// those whose lifecycle is still pending/actionable (e.g. AwaitingApproval). Reads
    /// are independent; an unreadable id surfaces as a skipped entry rather than failing
    /// the whole list.
    /// </summary>
    Task<OperateSectionResult<IReadOnlyList<DeployOperationProposal>>> ListPendingAsync(
        IReadOnlyList<string> operationIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Approves and submits a paused (AwaitingApproval) deploy-control operation through
    /// <c>POST /operations/{id}/submit</c>, with an optional operator reason. The server
    /// advances the operation only if its approval gate is satisfied.
    /// </summary>
    Task<OperateSectionResult<DeployOperationProposal>> SubmitAsync(
        string operationId,
        string? reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back a deploy-control operation through <c>POST /operations/{id}/rollback</c>,
    /// with an optional operator reason. A data-affecting rollback is gated server-side by
    /// the OperatorApprovalGate (the UI must require explicit confirmation first); a denied
    /// rollback surfaces as a <see cref="OperateSectionStatus.Forbidden"/> result.
    /// </summary>
    Task<OperateSectionResult<DeployOperationProposal>> RollbackAsync(
        string operationId,
        string? reason,
        CancellationToken cancellationToken = default);
}
