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
}
