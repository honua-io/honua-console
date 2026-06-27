using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Default <see cref="IConsoleApprovalInboxClient"/>: a thin aggregator over the
/// existing GitOps release and deploy-control approval clients. It introduces no new
/// server contract — it reuses the Operate Deploy page's proven projection (resolve the
/// deploy operation ids carried by the release operations in view, union with any
/// operator-tracked ids, then read those ids through the deploy-control client) and adds
/// the GIS-desk ticket-type classification on top. All data stays server-owned; the
/// aggregator never fabricates or mutates operations.
/// </summary>
public sealed class ConsoleApprovalInboxClient : IConsoleApprovalInboxClient
{
    private readonly IConsoleGitOpsReleaseClient _releases;
    private readonly IConsoleDeployApprovalClient _approvals;

    public ConsoleApprovalInboxClient(
        IConsoleGitOpsReleaseClient releases,
        IConsoleDeployApprovalClient approvals)
    {
        _releases = releases ?? throw new ArgumentNullException(nameof(releases));
        _approvals = approvals ?? throw new ArgumentNullException(nameof(approvals));
    }

    public async Task<OperateSectionResult<ApprovalInboxSnapshot>> GetInboxAsync(
        IReadOnlyList<string> trackedOperationIds,
        CancellationToken cancellationToken = default)
    {
        var releaseDerivedIds = await ResolveReleaseDeployOperationIdsAsync(cancellationToken)
            .ConfigureAwait(false);

        var allIds = releaseDerivedIds
            .Concat(trackedOperationIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var pending = await _approvals.ListPendingAsync(allIds, cancellationToken).ConfigureAwait(false);
        if (!pending.IsAllowed)
        {
            // Surface the deploy-control read status (missing-binding / forbidden /
            // unsupported / unavailable) onto the inbox without inventing a queue.
            return OperateSectionResult<ApprovalInboxSnapshot>.Denied(pending.Status, pending.Message);
        }

        var items = (pending.Value ?? [])
            .Select(proposal => new ApprovalInboxItem(ApprovalTicketPresentation.Classify(proposal), proposal))
            // Actionable work first, then most-recently-updated, for a stable work-queue order.
            .OrderByDescending(item => item.IsAwaitingApproval)
            .ThenByDescending(item => item.Proposal.UpdatedAt)
            .ToArray();

        return OperateSectionResult<ApprovalInboxSnapshot>.Allowed(
            new ApprovalInboxSnapshot(items),
            pending.PartialResult,
            pending.Message);
    }

    // Mirrors OperateDeployPage.ResolveReleaseDeployOperationIdsAsync: derive the
    // deploy-control operation ids from the release operations in view. A missing/unbound
    // release source simply yields no derived ids (the operator-tracked ids still apply),
    // so the inbox degrades to its honest empty/missing state rather than failing.
    private async Task<IReadOnlyList<string>> ResolveReleaseDeployOperationIdsAsync(
        CancellationToken cancellationToken)
    {
        var proposals = await _releases.GetReleaseProposalsAsync(cancellationToken).ConfigureAwait(false);
        if (proposals is not { IsAllowed: true, Value: { } list } || list.Count == 0)
        {
            return [];
        }

        var details = await Task.WhenAll(
            list.Select(p => _releases.GetReleaseDetailAsync(p.ReleasePackageId, cancellationToken)))
            .ConfigureAwait(false);

        return details
            .Where(d => d.IsAllowed && d.Value?.Operation is { DeployOperationId: { Length: > 0 } })
            .Select(d => d.Value!.Operation!.DeployOperationId!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}
