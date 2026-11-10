using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.TestFixtures;

internal sealed class ReceiptProposalClient(ConsoleProposalStatus initialStatus = ConsoleProposalStatus.AwaitingApproval)
    : IConsoleProposalsClient
{
    public Task<OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>> ListAsync(
        string? status = null,
        string? kind = null,
        string? requestedBy = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>.Allowed([]));

    public Task<OperateSectionResult<ConsoleProposalDetail>> GetAsync(
        string proposalId,
        CancellationToken cancellationToken = default) => Task.FromResult(Allowed(initialStatus, proposalId));

    public Task<OperateSectionResult<ConsoleProposalDetail>> GetStatusAsync(
        string proposalId,
        CancellationToken cancellationToken = default)
    {
        var result = Allowed(initialStatus, proposalId);
        return Task.FromResult(result with { Value = result.Value!.AsStatusOnly() });
    }

    public Task<OperateSectionResult<ConsoleProposalDetail>> ApproveReviewedAsync(
        ConsoleProposalDecision decision,
        CancellationToken cancellationToken = default) => Task.FromResult(Allowed(ConsoleProposalStatus.Submitted, decision.ProposalId));

    public Task<OperateSectionResult<ConsoleProposalDetail>> RejectAsync(
        string proposalId,
        string reason,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(OperateSectionResult<ConsoleProposalDetail>.Denied(OperateSectionStatus.Unsupported, "not used"));

    public static OperateSectionResult<ConsoleProposalDetail> Allowed(
        ConsoleProposalStatus status, string proposalId = "proposal-1") =>
        OperateSectionResult<ConsoleProposalDetail>.Allowed(new(
            proposalId,
            ConsoleProposalKind.MetadataRelease,
            status,
            "agent",
            "agent",
            "Publish candidate map",
            ["item-1", "version-1", "draft-1", "map-route"],
            [],
            ConsoleProposalRisk.Low,
            [],
            [],
            "human",
            status == ConsoleProposalStatus.AwaitingApproval ? null : "operator",
            null,
            status == ConsoleProposalStatus.AwaitingApproval ? null : "operation-1",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            status == ConsoleProposalStatus.AwaitingApproval ? null : DateTimeOffset.UtcNow)
        {
            Version = 7,
            PlanHash = "sealed-plan",
            HumanReviewEnvironment = "environment",
            HumanReviewExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
        });
}
