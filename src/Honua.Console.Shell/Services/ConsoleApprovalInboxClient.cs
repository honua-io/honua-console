using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Default <see cref="IConsoleApprovalInboxClient"/>: a thin projection over the first-class
/// honua-server proposals API (<see cref="IConsoleProposalsClient"/>, honua-server #1694). It
/// reads the active proposals list, adds the GIS-desk ticket-type classification, and orders
/// the queue awaiting-approval-first. All data stays server-owned; the projection never
/// fabricates or mutates proposals.
/// </summary>
public sealed class ConsoleApprovalInboxClient : IConsoleApprovalInboxClient
{
    private readonly IConsoleProposalsClient _proposals;

    public ConsoleApprovalInboxClient(IConsoleProposalsClient proposals)
    {
        _proposals = proposals ?? throw new ArgumentNullException(nameof(proposals));
    }

    public async Task<OperateSectionResult<ApprovalInboxSnapshot>> GetInboxAsync(
        string? status = null,
        string? kind = null,
        CancellationToken cancellationToken = default)
    {
        var listed = await _proposals.ListAsync(status, kind, requestedBy: null, cancellationToken)
            .ConfigureAwait(false);

        if (!listed.IsAllowed)
        {
            // Surface the proposals read status (missing-binding / forbidden / unsupported /
            // unavailable) onto the inbox without inventing a queue.
            return OperateSectionResult<ApprovalInboxSnapshot>.Denied(listed.Status, listed.Message);
        }

        var items = (listed.Value ?? [])
            .Select(proposal => new ApprovalInboxItem(ApprovalTicketPresentation.Classify(proposal), proposal))
            // Actionable work first, then most-recently-updated, for a stable work-queue order.
            .OrderByDescending(item => item.IsAwaitingApproval)
            .ThenByDescending(item => item.Proposal.UpdatedAt)
            .ToArray();

        return OperateSectionResult<ApprovalInboxSnapshot>.Allowed(
            new ApprovalInboxSnapshot(items),
            listed.PartialResult,
            listed.Message);
    }
}
