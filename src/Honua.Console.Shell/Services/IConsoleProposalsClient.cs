using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

// Approve and reject require the server's distinct approve grant and the proposer-cannot-approve rule; this client does not bypass that gate.
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

    Task<OperateSectionResult<ConsoleProposalDetail>> RejectAsync(
        string proposalId,
        string reason,
        CancellationToken cancellationToken = default);
}
