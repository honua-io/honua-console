using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class UnsupportedRbacAccessDataSource : IRbacAccessDataSource
{
    private const string Surface = "Access (RBAC)";

    private static readonly RbacCapabilityState MissingBinding = new(
        Surface,
        "Missing binding",
        "Honua:Server:BaseUrl",
        "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so the Access surface can bind the server-owned Console metadata/RBAC API (honua-server#1162).");

    public Task<RbacOverviewLoad> LoadOverviewAsync(string environmentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new RbacOverviewLoad(null, [MissingBinding]));

    public Task<TeamMembershipLoad> LoadMembershipAsync(string environmentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new TeamMembershipLoad(null, [MissingBinding]));

    public Task<RbacRoleMutationResult> CreateRoleAsync(
        string environmentId,
        string name,
        string? description,
        IReadOnlyList<string> grantedPermissionKeys,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new RbacRoleMutationResult(false, MissingBinding.Detail));

    public Task<RbacRoleMutationResult> DeleteRoleAsync(
        string environmentId,
        string roleId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new RbacRoleMutationResult(false, MissingBinding.Detail));

    public Task<RbacAuditLoad> LoadAuditAsync(string environmentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new RbacAuditLoad([], [MissingBinding]));
}
