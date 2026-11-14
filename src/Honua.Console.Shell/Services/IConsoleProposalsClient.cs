using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

// Approve and reject both require the server approve grant; approval also requires a live human browser session and a review capability. This client does not bypass that gate.
public interface IConsoleProposalsClient
{
    Task<OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>> ListAsync(
        string? status = null,
        string? kind = null,
        string? requestedBy = null,
        CancellationToken cancellationToken = default);

    Task<OperateSectionResult<ConsoleProposalDetail>> GetAsync(
        string proposalId,
        CancellationToken cancellationToken = default);

    Task<OperateSectionResult<ConsoleProposalDetail>> ApproveAsync(
        string proposalId,
        CancellationToken cancellationToken = default);

    // Approves only the plan the operator reviewed: a changed version or plan hash is refused before submission.
    Task<OperateSectionResult<ConsoleProposalDetail>> ApproveAsync(
        string proposalId,
        ConsoleProposalReviewedPlan? reviewed,
        CancellationToken cancellationToken = default) =>
        ApproveAsync(proposalId, cancellationToken);

    Task<OperateSectionResult<ConsoleProposalDetail>> RejectAsync(
        string proposalId,
        string reason,
        CancellationToken cancellationToken = default);
}

// The version and plan hash the operator saw when deciding.
public sealed record ConsoleProposalReviewedPlan(long Version, string? PlanHash)
{
    public static ConsoleProposalReviewedPlan? From(ConsoleProposalDetail? detail) =>
        detail is { Version: > 0 } ? new ConsoleProposalReviewedPlan(detail.Version, detail.PlanHash) : null;
}
