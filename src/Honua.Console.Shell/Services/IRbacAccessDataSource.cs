using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IRbacAccessDataSource
{
    Task<RbacOverviewLoad> LoadOverviewAsync(string workspaceId, CancellationToken cancellationToken = default);

    Task<TeamMembershipLoad> LoadMembershipAsync(string workspaceId, CancellationToken cancellationToken = default);

    Task<RbacRoleMutationResult> CreateRoleAsync(
        string workspaceId,
        string name,
        string? description,
        IReadOnlyList<string> grantedPermissionKeys,
        CancellationToken cancellationToken = default);

    Task<RbacRoleMutationResult> DeleteRoleAsync(
        string workspaceId,
        string roleId,
        CancellationToken cancellationToken = default);

    Task<RbacAuditLoad> LoadAuditAsync(string workspaceId, CancellationToken cancellationToken = default);
}
