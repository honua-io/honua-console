using Honua.Console.Contracts;

namespace Honua.Console.Shell.Models;

// Console-side projections of the honua-server Console metadata/RBAC surface (honua-server#1162), scoped
// to the authenticated Settings > Access screens (RBAC overview + team members). These are immutable view
// records the Access pages render; they carry no local RBAC state and never fabricate role/member data —
// when the server is unbound, the workspace is missing, or a read is rejected, the surface renders a
// capability state instead (Console Patterns Charter section 11).

/// <summary>
/// A binding/permission/not-found/missing surface for the Access pages, mirroring the Share access
/// capability-state pattern so missing bindings, missing permissions, and unsupported contracts render
/// consistently across the RBAC overview and the team-members surfaces.
/// </summary>
public sealed record RbacCapabilityState(
    string Surface,
    string State,
    string Contract,
    string Detail);

/// <summary>One level of the scope-hierarchy strip (workspace > environment > content > publication > field).</summary>
public sealed record RbacScopeView(
    string Level,
    string Label,
    string? Description);

/// <summary>One permission column header in the role × permission matrix.</summary>
public sealed record RbacPermissionView(
    string Key,
    string Label);

/// <summary>How a role grants one permission, with the grant kind and an optional qualifier label.</summary>
public sealed record RbacGrantView(
    string Permission,
    string Grant,
    string? Qualifier)
{
    public bool IsGranted => string.Equals(Grant, HonuaConsolePermissionGrants.Granted, StringComparison.Ordinal);

    public bool IsNotGranted =>
        string.IsNullOrEmpty(Grant)
        || string.Equals(Grant, HonuaConsolePermissionGrants.NotGranted, StringComparison.Ordinal);
}

/// <summary>One role row in the matrix (built-in or custom) with its per-permission grants.</summary>
public sealed record RbacRoleView(
    string Id,
    string Name,
    string? Description,
    bool IsCustom,
    IReadOnlyList<RbacGrantView> Grants)
{
    public RbacGrantView GrantFor(string permissionKey) =>
        Grants.FirstOrDefault(grant => string.Equals(grant.Permission, permissionKey, StringComparison.Ordinal))
        ?? new RbacGrantView(permissionKey, HonuaConsolePermissionGrants.NotGranted, null);
}

/// <summary>The server-owned RBAC overview projection the Settings > Access "roles &amp; permissions" page renders.</summary>
public sealed record RbacOverviewView(
    string WorkspaceId,
    string? WorkspaceName,
    IReadOnlyList<RbacScopeView> Scopes,
    IReadOnlyList<RbacPermissionView> Permissions,
    IReadOnlyList<RbacRoleView> Roles,
    int BuiltInRoleCount,
    int CustomRoleCount,
    int MembersAffected,
    bool CanManageRoles)
{
    public string DisplayWorkspace => string.IsNullOrWhiteSpace(WorkspaceName) ? WorkspaceId : WorkspaceName!;
}

/// <summary>Result of loading the RBAC overview: the view, or the capability states explaining why not.</summary>
public sealed record RbacOverviewLoad(
    RbacOverviewView? Overview,
    IReadOnlyList<RbacCapabilityState> CapabilityStates)
{
    public bool HasOverview => Overview is not null;
}

/// <summary>One workspace member row: identity, role, scope, last-active, source, and principal kind.</summary>
public sealed record TeamMemberView(
    string Id,
    string DisplayName,
    string? Identity,
    string? RoleId,
    string RoleName,
    bool IsCustomRole,
    string? Scope,
    string? LastActive,
    string? Source,
    string? PrincipalKind)
{
    public string Initials
    {
        get
        {
            if (string.Equals(PrincipalKind, "api-key", StringComparison.Ordinal))
            {
                return "API";
            }

            if (string.Equals(PrincipalKind, "group", StringComparison.Ordinal))
            {
                return "GRP";
            }

            var letters = DisplayName
                .Where(ch => char.IsLetterOrDigit(ch))
                .Take(2)
                .ToArray();
            return letters.Length == 0 ? "?" : new string(letters).ToUpperInvariant();
        }
    }
}

/// <summary>One pending invitation row awaiting acceptance.</summary>
public sealed record TeamInvitationView(
    string Id,
    string Identity,
    string RoleName,
    bool IsCustomRole,
    string? Scope,
    string? Status);

/// <summary>The server-owned membership projection the Settings > Access "members" page renders.</summary>
public sealed record TeamMembershipView(
    string WorkspaceId,
    string? WorkspaceName,
    IReadOnlyList<TeamMemberView> Members,
    IReadOnlyList<TeamInvitationView> Invitations,
    int ActiveCount,
    int PendingCount,
    int DeactivatedCount,
    bool CanInvite)
{
    public string DisplayWorkspace => string.IsNullOrWhiteSpace(WorkspaceName) ? WorkspaceId : WorkspaceName!;
}

/// <summary>Result of loading the membership roster: the view, or the capability states explaining why not.</summary>
public sealed record TeamMembershipLoad(
    TeamMembershipView? Membership,
    IReadOnlyList<RbacCapabilityState> CapabilityStates)
{
    public bool HasMembership => Membership is not null;
}

/// <summary>Maps the honua-server Console Access (RBAC) wire records to the Access page view records.</summary>
public static class RbacAccessMapper
{
    public static RbacOverviewView ToView(HonuaConsoleRbacOverview overview)
    {
        ArgumentNullException.ThrowIfNull(overview);

        return new RbacOverviewView(
            WorkspaceId: overview.WorkspaceId,
            WorkspaceName: overview.WorkspaceName,
            Scopes: (overview.Scopes ?? [])
                .Select(scope => new RbacScopeView(scope.Level, scope.Label, scope.Description))
                .ToArray(),
            Permissions: (overview.Permissions ?? [])
                .Select(permission => new RbacPermissionView(permission.Key, permission.Label))
                .ToArray(),
            Roles: (overview.Roles ?? [])
                .Select(role => new RbacRoleView(
                    role.Id,
                    role.Name,
                    role.Description,
                    role.IsCustom,
                    (role.Grants ?? [])
                        .Select(grant => new RbacGrantView(grant.Permission, grant.Grant, grant.Qualifier))
                        .ToArray()))
                .ToArray(),
            BuiltInRoleCount: overview.BuiltInRoleCount,
            CustomRoleCount: overview.CustomRoleCount,
            MembersAffected: overview.MembersAffected,
            CanManageRoles: overview.CanManageRoles);
    }

    public static TeamMembershipView ToView(HonuaConsoleTeamMembership membership)
    {
        ArgumentNullException.ThrowIfNull(membership);

        return new TeamMembershipView(
            WorkspaceId: membership.WorkspaceId,
            WorkspaceName: membership.WorkspaceName,
            Members: (membership.Members ?? [])
                .Select(member => new TeamMemberView(
                    member.Id,
                    member.DisplayName,
                    member.Identity,
                    member.RoleId,
                    member.RoleName,
                    member.IsCustomRole,
                    member.Scope,
                    member.LastActive,
                    member.Source,
                    member.PrincipalKind))
                .ToArray(),
            Invitations: (membership.Invitations ?? [])
                .Select(invitation => new TeamInvitationView(
                    invitation.Id,
                    invitation.Identity,
                    invitation.RoleName,
                    invitation.IsCustomRole,
                    invitation.Scope,
                    invitation.Status))
                .ToArray(),
            ActiveCount: membership.ActiveCount,
            PendingCount: membership.PendingCount,
            DeactivatedCount: membership.DeactivatedCount,
            CanInvite: membership.CanInvite);
    }
}
