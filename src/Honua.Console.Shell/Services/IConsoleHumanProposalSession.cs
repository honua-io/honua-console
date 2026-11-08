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

public sealed record ConsoleHumanProposalTarget(
    ConsoleEnvironmentProfile Profile, ConsoleServerAuthority Authority, string ProposalId);

public sealed record ConsoleHumanProposalApprovalRequest(
    ConsoleEnvironmentProfile Profile, ConsoleServerAuthority Authority, ConsoleProposalDecision Decision);

public interface IConsoleHumanProposalSession
{
    Task<OperateSectionResult<ConsoleHumanProposalReview>> OpenReviewAsync(
        ConsoleHumanProposalTarget request,
        CancellationToken cancellationToken = default);

    Task<OperateSectionResult<ProposalDetailWire>> ReadStatusAsync(
        ConsoleHumanProposalTarget request,
        CancellationToken cancellationToken = default);

    Task<OperateSectionResult<ProposalDetailWire>> ApproveAsync(
        ConsoleHumanProposalApprovalRequest request,
        CancellationToken cancellationToken = default);
}
