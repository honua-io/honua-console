using System.Globalization;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class HonuaServerRbacAccessDataSource : IRbacAccessDataSource
{
    private const string Surface = "Access (RBAC)";

    private readonly IHonuaConsoleRbacClient _client;

    public HonuaServerRbacAccessDataSource(IHonuaConsoleRbacClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<RbacOverviewLoad> LoadOverviewAsync(string environmentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);

        var result = await _client.GetOverviewAsync(environmentId, cancellationToken).ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            return new RbacOverviewLoad(null, [ToCapabilityState(issue)]);
        }

        return new RbacOverviewLoad(RbacAccessMapper.ToView(result.Data!), []);
    }

    public async Task<TeamMembershipLoad> LoadMembershipAsync(string environmentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);

        var result = await _client.GetMembershipAsync(environmentId, cancellationToken).ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            return new TeamMembershipLoad(null, [ToCapabilityState(issue)]);
        }

        return new TeamMembershipLoad(RbacAccessMapper.ToView(result.Data!), []);
    }

    public async Task<RbacRoleMutationResult> CreateRoleAsync(
        string environmentId,
        string name,
        string? description,
        IReadOnlyList<string> grantedPermissionKeys,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);

        var grants = (grantedPermissionKeys ?? [])
            .Select(key => new HonuaConsoleRbacGrant { Permission = key, Grant = HonuaConsolePermissionGrants.Granted })
            .ToArray();
        var request = new HonuaConsoleRoleWriteRequest { Name = name, Description = description, Grants = grants };

        var result = await _client.CreateRoleAsync(environmentId, request, cancellationToken).ConfigureAwait(false);
        return result.Issue is { } issue
            ? new RbacRoleMutationResult(false, IssueMessage(issue))
            : new RbacRoleMutationResult(true, null);
    }

    public async Task<RbacRoleMutationResult> DeleteRoleAsync(
        string environmentId,
        string roleId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleId);

        var result = await _client.DeleteRoleAsync(environmentId, roleId, cancellationToken).ConfigureAwait(false);
        return result.Issue is { } issue
            ? new RbacRoleMutationResult(false, IssueMessage(issue))
            : new RbacRoleMutationResult(true, null);
    }

    public async Task<RbacAuditLoad> LoadAuditAsync(string environmentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);

        var result = await _client.GetRoleAuditAsync(environmentId, 50, cancellationToken).ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            return new RbacAuditLoad([], [ToCapabilityState(issue)]);
        }

        var entries = (result.Data!.Entries ?? [])
            .Select(entry => new RbacAuditEntryView(entry.Id, entry.Timestamp, entry.Actor, entry.Action, entry.RoleId, entry.Outcome))
            .ToArray();
        return new RbacAuditLoad(entries, []);
    }

    private static string IssueMessage(HonuaAdminEndpointIssue issue) => $"{issue.State}: {issue.Detail}";

    private static RbacCapabilityState ToCapabilityState(HonuaAdminEndpointIssue issue) =>
        new(
            Surface,
            issue.State,
            issue.Contract,
            issue.StatusCode is null
                ? issue.Detail
                : $"{issue.Detail} HTTP {issue.StatusCode.Value.ToString(CultureInfo.InvariantCulture)}.");
}
