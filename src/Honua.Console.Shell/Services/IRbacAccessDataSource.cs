using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IRbacAccessDataSource
{
    Task<RbacOverviewLoad> LoadOverviewAsync(string environmentId, CancellationToken cancellationToken = default);

    Task<TeamMembershipLoad> LoadMembershipAsync(string environmentId, CancellationToken cancellationToken = default);

    Task<RbacRoleMutationResult> CreateRoleAsync(
        string environmentId,
        string name,
        string? description,
        IReadOnlyList<string> grantedPermissionKeys,
        CancellationToken cancellationToken = default);

    Task<RbacRoleMutationResult> DeleteRoleAsync(
        string environmentId,
        string roleId,
        CancellationToken cancellationToken = default);

    Task<RbacAuditLoad> LoadAuditAsync(string environmentId, CancellationToken cancellationToken = default);
}
