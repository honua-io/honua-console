using Honua.Console.Contracts;
using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Models;

/// <summary>
/// A page of durable deploy-control operations from the server's paged list endpoint
/// (<c>GET /api/v1/admin/deploy/operations</c>, honua-server PR #2577), newest-first. This
/// is the console#290 replacement for the old release-scrape/tracked-id projection: the
/// operations list, live-progress timeline, and manual-intervention recovery panel all bind
/// to this real, server-owned list rather than to a caller-supplied id set.
/// </summary>
public sealed record DeployOperationListView(
    IReadOnlyList<DeployOperationProposal> Items,
    int Page,
    int PageSize,
    int TotalCount,
    bool HasMore)
{
    public static DeployOperationListView Empty { get; } = new([], 1, 0, 0, false);
}

/// <summary>Filter parameters for the deploy-operations list read.</summary>
public sealed record DeployOperationListQuery(
    string? Status = null,
    string? Kind = null,
    int Page = 1,
    int PageSize = 25);

/// <summary>Maps the server's paged list response onto the Console view.</summary>
public static class DeployOperationListMapper
{
    public static DeployOperationListView Map(DeployOperationListResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return new DeployOperationListView(
            response.Items.Select(DeployApprovalMapper.MapProposal).ToArray(),
            response.Page,
            response.PageSize,
            response.TotalCount,
            response.HasMore);
    }
}
