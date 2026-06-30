using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Binds the agent-operation approval surface (issue #193) to the honua-server console
/// approval REST API (honua-server #1694):
/// <list type="bullet">
///   <item><c>GET /api/v1/admin/proposals</c> — list/filter by status / kind / requester.</item>
///   <item><c>GET /api/v1/admin/proposals/{id}</c> — full plan + diff + dry-run + risk + blockers/warnings.</item>
///   <item><c>POST /api/v1/admin/proposals/{id}/approve</c> — approve (RBAC <c>approve</c> grant).</item>
///   <item><c>POST /api/v1/admin/proposals/{id}/reject</c> — reject (reason required).</item>
/// </list>
/// Approve/reject are gated server-side by the RBAC <c>approve</c> grant (distinct from the
/// proposer's grant) and the proposer-cannot-approve separation-of-duties rule; a denied
/// decision returns 403, surfaced here as a <see cref="OperateSectionStatus.Forbidden"/>
/// result carrying the server's message. The server is the only safety gate; the console
/// never bypasses it. Every read returns an <see cref="OperateSectionResult{T}"/> whose
/// status drives the shared missing / forbidden / unsupported / unavailable surfaces, and a
/// missing-binding read renders the honest missing state rather than seeded data (Console
/// Patterns Charter section 11).
/// </summary>
public interface IConsoleProposalsClient
{
    /// <summary>
    /// Lists the active operation proposals, optionally filtered by status, kind, and
    /// requester. Filters are forwarded verbatim to the server's query parser.
    /// </summary>
    Task<OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>> ListAsync(
        string? status = null,
        string? kind = null,
        string? requestedBy = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one proposal's full plan/diff/dry-run/risk/blockers detail.</summary>
    Task<OperateSectionResult<ConsoleProposalDetail>> GetAsync(
        string proposalId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Approves a proposal, submitting it to the underlying execution pipeline. Gated by
    /// the RBAC <c>approve</c> grant and separation of duties; a denied decision returns a
    /// <see cref="OperateSectionStatus.Forbidden"/> result.
    /// </summary>
    Task<OperateSectionResult<ConsoleProposalDetail>> ApproveAsync(
        string proposalId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rejects a proposal with a required reason. An empty reason is rejected before the
    /// request is sent (the server also requires it, returning 400).
    /// </summary>
    Task<OperateSectionResult<ConsoleProposalDetail>> RejectAsync(
        string proposalId,
        string reason,
        CancellationToken cancellationToken = default);
}
