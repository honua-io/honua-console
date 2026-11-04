using Honua.Console.Contracts;
using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Models;

public sealed record DeployOperationListView(
    IReadOnlyList<DeployOperationProposal> Items,
    int Page,
    int PageSize,
    int TotalCount,
    bool HasMore)
{
    public static DeployOperationListView Empty { get; } = new([], 1, 0, 0, false);
}

public sealed record DeployOperationListQuery(
    string? Status = null,
    string? Kind = null,
    int Page = 1,
    int PageSize = 25);

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
