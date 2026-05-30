using System.Globalization;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Console Access (RBAC) data source bound to the real honua-server Console metadata/RBAC API
/// (honua-server#1162) through the <see cref="IHonuaConsoleRbacClient"/> shim. Every read speaks the live
/// Console Access surface (<c>/api/v1/console/access/{workspaceId}/...</c>); there is no in-memory RBAC
/// data in the merged result (Console Patterns Charter section 11). Endpoint issues (missing permission,
/// not found, unsupported, transport) surface as explicit capability states instead of throwing or
/// fabricating role/member data.
/// </summary>
public sealed class HonuaServerRbacAccessDataSource : IRbacAccessDataSource
{
    private const string Surface = "Access (RBAC)";

    private readonly IHonuaConsoleRbacClient _client;

    public HonuaServerRbacAccessDataSource(IHonuaConsoleRbacClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<RbacOverviewLoad> LoadOverviewAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);

        var result = await _client.GetOverviewAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            return new RbacOverviewLoad(null, [ToCapabilityState(issue)]);
        }

        return new RbacOverviewLoad(RbacAccessMapper.ToView(result.Data!), []);
    }

    public async Task<TeamMembershipLoad> LoadMembershipAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);

        var result = await _client.GetMembershipAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            return new TeamMembershipLoad(null, [ToCapabilityState(issue)]);
        }

        return new TeamMembershipLoad(RbacAccessMapper.ToView(result.Data!), []);
    }

    private static RbacCapabilityState ToCapabilityState(HonuaAdminEndpointIssue issue) =>
        new(
            Surface,
            issue.State,
            issue.Contract,
            issue.StatusCode is null
                ? issue.Detail
                : $"{issue.Detail} HTTP {issue.StatusCode.Value.ToString(CultureInfo.InvariantCulture)}.");
}
