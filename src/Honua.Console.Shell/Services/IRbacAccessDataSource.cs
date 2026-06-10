using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Console Access (RBAC) data source. Binds the authenticated Settings &gt; Access surfaces — the role ×
/// permission overview and the workspace team-members roster with pending invitations — to the real
/// honua-server Console metadata/RBAC API (honua-server#1162) through the Honua.Console.Contracts shim.
/// There is no standing in-memory RBAC client in the merged result (Console Patterns Charter section 11):
/// when no server base URL is configured, the unsupported implementation surfaces an explicit
/// missing-binding state rather than fabricating role/member data.
/// </summary>
public interface IRbacAccessDataSource
{
    /// <summary>Loads the server-owned RBAC overview for a workspace, or capability states when it cannot be read.</summary>
    Task<RbacOverviewLoad> LoadOverviewAsync(string workspaceId, CancellationToken cancellationToken = default);

    /// <summary>Loads the server-owned membership roster for a workspace, or capability states when it cannot be read.</summary>
    Task<TeamMembershipLoad> LoadMembershipAsync(string workspaceId, CancellationToken cancellationToken = default);

    /// <summary>Creates a custom role granting the given console permission columns. Returns the outcome.</summary>
    Task<RbacRoleMutationResult> CreateRoleAsync(
        string workspaceId,
        string name,
        string? description,
        IReadOnlyList<string> grantedPermissionKeys,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a custom role (built-in roles are rejected server-side). Returns the outcome.</summary>
    Task<RbacRoleMutationResult> DeleteRoleAsync(
        string workspaceId,
        string roleId,
        CancellationToken cancellationToken = default);

    /// <summary>Loads the role-change audit trail for a workspace, or capability states when it cannot be read.</summary>
    Task<RbacAuditLoad> LoadAuditAsync(string workspaceId, CancellationToken cancellationToken = default);
}
