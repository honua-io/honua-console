using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Console Access (RBAC) data source used when no honua-server base address is configured. It renders an
/// explicit missing-binding state instead of fabricating role/member data, keeping the merged runtime free
/// of a standing in-memory Console RBAC client (Console Patterns Charter section 11). The Settings &gt;
/// Access surfaces stay bound to the real honua-server Console metadata/RBAC API (honua-server#1162) or
/// show this state.
/// </summary>
public sealed class UnsupportedRbacAccessDataSource : IRbacAccessDataSource
{
    private const string Surface = "Access (RBAC)";

    private static readonly RbacCapabilityState MissingBinding = new(
        Surface,
        "Missing binding",
        "Honua:Server:BaseUrl",
        "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so the Access surface can bind the server-owned Console metadata/RBAC API (honua-server#1162).");

    public Task<RbacOverviewLoad> LoadOverviewAsync(string workspaceId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new RbacOverviewLoad(null, [MissingBinding]));

    public Task<TeamMembershipLoad> LoadMembershipAsync(string workspaceId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new TeamMembershipLoad(null, [MissingBinding]));

    public Task<RbacRoleMutationResult> CreateRoleAsync(
        string workspaceId,
        string name,
        string? description,
        IReadOnlyList<string> grantedPermissionKeys,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new RbacRoleMutationResult(false, MissingBinding.Detail));

    public Task<RbacRoleMutationResult> DeleteRoleAsync(
        string workspaceId,
        string roleId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new RbacRoleMutationResult(false, MissingBinding.Detail));

    public Task<RbacAuditLoad> LoadAuditAsync(string workspaceId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new RbacAuditLoad([], [MissingBinding]));
}
