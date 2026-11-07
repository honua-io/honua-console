using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed record ConsoleProposalDecision(
    string ProposalId,
    long ProposalVersion,
    string PlanHash,
    string EnvironmentReference);

public sealed record ConsoleHumanProposalReview(
    ProposalDetailWire Proposal,
    string EnvironmentReference,
    DateTimeOffset ExpiresAt)
{
    public string? ApprovalMessage { get; init; }
}

public interface IConsoleHumanProposalSession
{
    Task<OperateSectionResult<ConsoleHumanProposalReview>> OpenReviewAsync(
        ConsoleEnvironmentProfile profile, ConsoleServerAuthority authority, string proposalId,
        CancellationToken cancellationToken = default);

    Task<OperateSectionResult<ProposalDetailWire>> ApproveAsync(
        ConsoleEnvironmentProfile profile, ConsoleServerAuthority authority, ConsoleProposalDecision decision,
        CancellationToken cancellationToken = default);
}
