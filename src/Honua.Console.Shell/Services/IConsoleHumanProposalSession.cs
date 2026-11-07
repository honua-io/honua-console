using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public enum ConsoleProposalAuthority
{
    Serving,
    Management
}

public sealed record ConsoleProposalDecision(
    string ProposalId,
    long ProposalVersion,
    string PlanHash,
    string EnvironmentReference);

public sealed record ConsoleHumanProposalReview(
    ProposalDetailWire Proposal,
    string EnvironmentReference,
    DateTimeOffset ExpiresAt);

public interface IConsoleHumanProposalSession
{
    Task<OperateSectionResult<ConsoleHumanProposalReview>> OpenReviewAsync(
        ConsoleEnvironmentProfile profile, ConsoleProposalAuthority authority, string proposalId,
        CancellationToken cancellationToken = default);

    Task<OperateSectionResult<ProposalDetailWire>> ApproveAsync(
        ConsoleEnvironmentProfile profile, ConsoleProposalAuthority authority, ConsoleProposalDecision decision,
        CancellationToken cancellationToken = default);
}
