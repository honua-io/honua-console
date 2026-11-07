using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

// Approval requires the exact displayed review and a private human session; rejection retains its server permission check.
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

    Task<OperateSectionResult<ConsoleProposalDetail>> ApproveReviewedAsync(
        ConsoleProposalDecision decision,
        CancellationToken cancellationToken = default) => Task.FromResult(
            OperateSectionResult<ConsoleProposalDetail>.Denied(OperateSectionStatus.Forbidden,
                "Sign in to the server and open the proposal review before approving."));

    Task<OperateSectionResult<ConsoleProposalDetail>> RejectAsync(
        string proposalId,
        string reason,
        CancellationToken cancellationToken = default);
}
