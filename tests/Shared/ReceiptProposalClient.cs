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
        CancellationToken cancellationToken = default) => Task.FromResult(Allowed(initialStatus));

    public Task<OperateSectionResult<ConsoleProposalDetail>> ApproveAsync(
        string proposalId,
        CancellationToken cancellationToken = default) => Task.FromResult(Allowed(ConsoleProposalStatus.Submitted));

    public Task<OperateSectionResult<ConsoleProposalDetail>> RejectAsync(
        string proposalId,
        string reason,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(OperateSectionResult<ConsoleProposalDetail>.Denied(OperateSectionStatus.Unsupported, "not used"));

    private static OperateSectionResult<ConsoleProposalDetail> Allowed(ConsoleProposalStatus status) =>
        OperateSectionResult<ConsoleProposalDetail>.Allowed(new(
            "proposal-1",
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
            status == ConsoleProposalStatus.AwaitingApproval ? null : DateTimeOffset.UtcNow));
}
