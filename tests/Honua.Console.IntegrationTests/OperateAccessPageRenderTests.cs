using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for the Settings &gt; Access (RBAC) surfaces — <c>/operate/access</c>
/// (roles &amp; permissions overview) and <c>/operate/access/members</c> (team members + scoped invite).
/// Asserts the design landmarks from the screens-collab-rbac.jsx mockup (the five-level scope strip, the
/// role × permission matrix with the granted/scoped/env-scoped/custom legend, the members roster with
/// pending invitations, and the scoped-invite drawer with the "what this grants" preview) plus the
/// missing-binding surface. Drives the pages through a fake <see cref="IRbacAccessDataSource"/> rather than
/// a mock server; the live binding follows the established opt-in Testcontainers pattern.
/// </summary>
public sealed class OperateAccessPageRenderTests
{
    private static readonly RbacCapabilityState MissingBinding = new(
        "Access (RBAC)",
        "Missing binding",
        "Honua:Server:BaseUrl",
        "Configure Honua:Server:BaseUrl so the Access surface can bind the Console metadata/RBAC API.");

    [Fact]
    public void RolesOverview_WhenBindingMissing_RendersNotBoundSurface()
    {
        var data = new FakeRbacDataSource { OverviewLoad = new RbacOverviewLoad(null, [MissingBinding]) };

        var page = RenderRoles(data);

        page.WaitForAssertion(
            () => Assert.Contains("Access (RBAC) API is not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("data-rbac-matrix", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void RolesOverview_Loaded_RendersScopeStripMatrixAndLegend()
    {
        var data = new FakeRbacDataSource { OverviewLoad = new RbacOverviewLoad(SampleOverview(), []) };

        var page = RenderRoles(data);

        page.WaitForAssertion(
            () => Assert.Contains("data-rbac-matrix", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // Five-level scope hierarchy strip.
        Assert.Contains("data-rbac-scope-strip", page.Markup, StringComparison.Ordinal);
        foreach (var level in new[] { "workspace", "environment", "content", "publication", "resource-field" })
        {
            Assert.Contains($"data-rbac-scope=\"{level}\"", page.Markup, StringComparison.Ordinal);
        }

        // Permission columns and role rows.
        Assert.Contains("data-rbac-permission=\"publish\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-rbac-role=\"publisher\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-rbac-role=\"gis-team\"", page.Markup, StringComparison.Ordinal);

        // The matrix legend distinguishes the four grant kinds.
        Assert.Contains("data-rbac-grant=\"granted\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-rbac-grant=\"env-scoped\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-rbac-grant=\"scoped\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-rbac-grant=\"not-granted\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-rbac-legend", page.Markup, StringComparison.Ordinal);

        // Counts strip.
        Assert.Contains("8 built-in roles", page.Markup, StringComparison.Ordinal);
        Assert.Contains("2 custom", page.Markup, StringComparison.Ordinal);
        Assert.Contains("14 members affected", page.Markup, StringComparison.Ordinal);

        // Role authoring + audit are now bound to the server contract (#1162): '+ Custom role' is enabled
        // when the caller may manage roles (SampleOverview sets CanManageRoles true), and 'Roles audit' is
        // enabled once the overview loads.
        var customRole = page.Find("[data-rbac-custom-role]");
        Assert.False(customRole.HasAttribute("disabled"));
        Assert.NotNull(page.Find("[data-rbac-audit]"));
    }

    [Fact]
    public void RolesOverview_CustomRoleAndAudit_EnabledWhenCallerCanManageRoles()
    {
        // With the manage-roles permission, both actions are enabled and wired to the server authoring/audit
        // contract — no longer a read-only projection.
        var canManage = new FakeRbacDataSource
        {
            OverviewLoad = new RbacOverviewLoad(SampleOverview() with { CanManageRoles = true }, [])
        };

        var page = RenderRoles(canManage);

        page.WaitForAssertion(
            () => Assert.NotNull(page.Find("[data-rbac-custom-role]")),
            TimeSpan.FromSeconds(5));
        Assert.False(page.Find("[data-rbac-custom-role]").HasAttribute("disabled"));
        Assert.False(page.Find("[data-rbac-audit]").HasAttribute("disabled"));

        // Without the manage-roles permission, '+ Custom role' is honestly disabled with the reason.
        var cannotManage = new FakeRbacDataSource
        {
            OverviewLoad = new RbacOverviewLoad(SampleOverview() with { CanManageRoles = false }, [])
        };

        var page2 = RenderRoles(cannotManage);

        page2.WaitForAssertion(
            () => Assert.NotNull(page2.Find("[data-rbac-custom-role]")),
            TimeSpan.FromSeconds(5));
        Assert.True(page2.Find("[data-rbac-custom-role]").HasAttribute("disabled"));
        Assert.Contains("manage-roles", page2.Find("[data-rbac-custom-role]").GetAttribute("title"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Members_WhenBindingMissing_RendersNotBoundSurface()
    {
        var data = new FakeRbacDataSource { MembershipLoad = new TeamMembershipLoad(null, [MissingBinding]) };

        var page = RenderMembers(data);

        page.WaitForAssertion(
            () => Assert.Contains("Access (RBAC) API is not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("data-rbac-members-table", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Members_Loaded_RendersRosterCountsAndPendingInvitations()
    {
        var data = new FakeRbacDataSource
        {
            MembershipLoad = new TeamMembershipLoad(SampleMembership(), []),
            OverviewLoad = new RbacOverviewLoad(SampleOverview(), [])
        };

        var page = RenderMembers(data);

        page.WaitForAssertion(
            () => Assert.Contains("data-rbac-members-table", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        Assert.Contains("Public Works", page.Markup, StringComparison.Ordinal);
        Assert.Contains("14 active · 2 pending · 1 deactivated", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-rbac-member=\"jamie\"", page.Markup, StringComparison.Ordinal);
        // Custom-role members are badged distinctly.
        Assert.Contains("data-rbac-member-role=\"custom\"", page.Markup, StringComparison.Ordinal);
        // Pending invitations block.
        Assert.Contains("data-rbac-invitations", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Pending invitations · 2", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-rbac-invitation=\"inv-1\"", page.Markup, StringComparison.Ordinal);
        // The invite drawer is not open until the action is invoked.
        Assert.DoesNotContain("data-rbac-invite-drawer", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Members_OpenInvite_RendersScopedDrawerWithGrantsPreview()
    {
        var data = new FakeRbacDataSource
        {
            MembershipLoad = new TeamMembershipLoad(SampleMembership(), []),
            OverviewLoad = new RbacOverviewLoad(SampleOverview(), [])
        };

        var page = RenderMembers(data);

        page.WaitForAssertion(
            () => Assert.NotNull(page.Find("[data-rbac-invite-open]")),
            TimeSpan.FromSeconds(5));

        page.Find("[data-rbac-invite-open]").Click();

        page.WaitForAssertion(
            () => Assert.Contains("data-rbac-invite-drawer", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // The drawer carries the Identity / Role / Scope / Limits groups and the "what this grants" preview.
        Assert.Contains("data-rbac-invite-group=\"identity\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-rbac-invite-group=\"role\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-rbac-invite-group=\"scope\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-rbac-invite-group=\"limits\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-rbac-invite-grants", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-rbac-invite-callout", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Members_OpenInvite_SendIsHonestlyDisabled_ButClientValidationStillSurfaces()
    {
        var data = new FakeRbacDataSource
        {
            MembershipLoad = new TeamMembershipLoad(SampleMembership(), []),
            OverviewLoad = new RbacOverviewLoad(SampleOverview(), [])
        };

        var page = RenderMembers(data);

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-rbac-invite-open]")), TimeSpan.FromSeconds(5));
        page.Find("[data-rbac-invite-open]").Click();
        page.WaitForAssertion(
            () => Assert.NotNull(page.Find("[data-rbac-invite-send]")),
            TimeSpan.FromSeconds(5));

        // Membership/invitations are a READ-ONLY projection of the server RBAC contract — there is no
        // invite-send mutation surface — so Send is honestly disabled (never a clickable no-op) regardless
        // of client-side validity. (Charter §11.)
        Assert.True(page.Find("[data-rbac-invite-send]").HasAttribute("disabled"));

        // Provide a valid email + the default dev scope: Send stays disabled (no server contract) but the
        // drawer remains a live scoped-invite preview, so its client-side CIDR validation still surfaces.
        page.Find("input[placeholder='name@example.gov']").Change("r.kim@example.gov");
        Assert.True(page.Find("[data-rbac-invite-send]").HasAttribute("disabled"));

        // The @bind-wired IP allowlist input round-trips and validates as CIDR.
        page.Find("input[placeholder='(none · global)']").Change("not-a-cidr");
        page.WaitForAssertion(
            () => Assert.Contains("valid CIDR block", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Members_OpenInvite_BadEmail_ShowsInlineError()
    {
        var data = new FakeRbacDataSource
        {
            MembershipLoad = new TeamMembershipLoad(SampleMembership(), []),
            OverviewLoad = new RbacOverviewLoad(SampleOverview(), [])
        };

        var page = RenderMembers(data);

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-rbac-invite-open]")), TimeSpan.FromSeconds(5));
        page.Find("[data-rbac-invite-open]").Click();
        page.WaitForAssertion(() => Assert.NotNull(page.Find("input[placeholder='name@example.gov']")), TimeSpan.FromSeconds(5));

        page.Find("input[placeholder='name@example.gov']").Change("bad@nodot");

        page.WaitForAssertion(
            () => Assert.Contains("Enter a valid email address", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("console-validation-inline", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Members_OpenInvite_NoScope_ShowsScopeError()
    {
        var data = new FakeRbacDataSource
        {
            MembershipLoad = new TeamMembershipLoad(SampleMembership(), []),
            OverviewLoad = new RbacOverviewLoad(SampleOverview(), [])
        };

        var page = RenderMembers(data);

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-rbac-invite-open]")), TimeSpan.FromSeconds(5));
        page.Find("[data-rbac-invite-open]").Click();
        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-rbac-invite-group=\"scope\"] input[type=checkbox]")), TimeSpan.FromSeconds(5));

        // Deselect the default dev scope -> at least one environment scope rule fires.
        page.Find("[data-rbac-invite-group=\"scope\"] input[type=checkbox]").Change(false);

        page.WaitForAssertion(
            () => Assert.Contains("at least one environment scope", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Members_WhenCannotInvite_DisablesInviteAction()
    {
        var membership = SampleMembership() with { CanInvite = false };
        var data = new FakeRbacDataSource { MembershipLoad = new TeamMembershipLoad(membership, []) };

        var page = RenderMembers(data);

        page.WaitForAssertion(
            () => Assert.NotNull(page.Find("[data-rbac-invite-open]")),
            TimeSpan.FromSeconds(5));
        Assert.True(page.Find("[data-rbac-invite-open]").HasAttribute("disabled"));
    }

    private static IRenderedComponent<OperateAccessRolesPage> RenderRoles(FakeRbacDataSource data)
    {
        var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton<IRbacAccessDataSource>(data);
        return ctx.Render<OperateAccessRolesPage>();
    }

    private static IRenderedComponent<OperateAccessMembersPage> RenderMembers(FakeRbacDataSource data)
    {
        var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton<IRbacAccessDataSource>(data);
        return ctx.Render<OperateAccessMembersPage>();
    }

    private static RbacOverviewView SampleOverview() => RbacAccessMapper.ToView(new HonuaConsoleRbacOverview
    {
        WorkspaceId = "ws-public-works",
        WorkspaceName = "Public Works",
        Scopes =
        [
            new HonuaConsoleAccessScope { Level = HonuaConsoleAccessScopeLevels.Workspace, Label = "Workspace", Description = "the cohort of envs" },
            new HonuaConsoleAccessScope { Level = HonuaConsoleAccessScopeLevels.Environment, Label = "Environment", Description = "dev / staging / prod" },
            new HonuaConsoleAccessScope { Level = HonuaConsoleAccessScopeLevels.Content, Label = "Content", Description = "map / dashboard / report" },
            new HonuaConsoleAccessScope { Level = HonuaConsoleAccessScopeLevels.Publication, Label = "Publication", Description = "service slot / share link" },
            new HonuaConsoleAccessScope { Level = HonuaConsoleAccessScopeLevels.ResourceField, Label = "Resource field", Description = "PII redaction" },
        ],
        Permissions =
        [
            new HonuaConsoleRbacPermission { Key = "view-public", Label = "View public" },
            new HonuaConsoleRbacPermission { Key = "comment", Label = "Comment" },
            new HonuaConsoleRbacPermission { Key = "draft", Label = "Draft / collab" },
            new HonuaConsoleRbacPermission { Key = "publish", Label = "Publish" },
            new HonuaConsoleRbacPermission { Key = "manage-roles", Label = "Manage roles" },
        ],
        Roles =
        [
            new HonuaConsoleRbacRole
            {
                Id = "publisher",
                Name = "Publisher",
                Description = "creates + publishes content",
                IsCustom = false,
                Grants =
                [
                    new HonuaConsoleRbacGrant { Permission = "view-public", Grant = HonuaConsolePermissionGrants.Granted },
                    new HonuaConsoleRbacGrant { Permission = "comment", Grant = HonuaConsolePermissionGrants.Granted },
                    new HonuaConsoleRbacGrant { Permission = "draft", Grant = HonuaConsolePermissionGrants.Granted },
                    new HonuaConsoleRbacGrant { Permission = "publish", Grant = HonuaConsolePermissionGrants.EnvScoped },
                    new HonuaConsoleRbacGrant { Permission = "manage-roles", Grant = HonuaConsolePermissionGrants.NotGranted },
                ],
            },
            new HonuaConsoleRbacRole
            {
                Id = "draft-collaborator",
                Name = "Draft collaborator",
                Description = "scoped to specific drafts only",
                IsCustom = false,
                Grants =
                [
                    new HonuaConsoleRbacGrant { Permission = "view-public", Grant = HonuaConsolePermissionGrants.Granted },
                    new HonuaConsoleRbacGrant { Permission = "comment", Grant = HonuaConsolePermissionGrants.Granted },
                    new HonuaConsoleRbacGrant { Permission = "draft", Grant = HonuaConsolePermissionGrants.Scoped },
                    new HonuaConsoleRbacGrant { Permission = "publish", Grant = HonuaConsolePermissionGrants.NotGranted },
                    new HonuaConsoleRbacGrant { Permission = "manage-roles", Grant = HonuaConsolePermissionGrants.NotGranted },
                ],
            },
            new HonuaConsoleRbacRole
            {
                Id = "gis-team",
                Name = "GIS team",
                Description = "publisher + can edit access",
                IsCustom = true,
                Grants =
                [
                    new HonuaConsoleRbacGrant { Permission = "view-public", Grant = HonuaConsolePermissionGrants.Custom },
                    new HonuaConsoleRbacGrant { Permission = "comment", Grant = HonuaConsolePermissionGrants.Custom },
                    new HonuaConsoleRbacGrant { Permission = "draft", Grant = HonuaConsolePermissionGrants.Custom },
                    new HonuaConsoleRbacGrant { Permission = "publish", Grant = HonuaConsolePermissionGrants.Custom },
                    new HonuaConsoleRbacGrant { Permission = "manage-roles", Grant = HonuaConsolePermissionGrants.Custom },
                ],
            },
        ],
        BuiltInRoleCount = 8,
        CustomRoleCount = 2,
        MembersAffected = 14,
        CanManageRoles = true,
    });

    private static TeamMembershipView SampleMembership() => RbacAccessMapper.ToView(new HonuaConsoleTeamMembership
    {
        WorkspaceId = "ws-public-works",
        WorkspaceName = "Public Works",
        Members =
        [
            new HonuaConsoleTeamMember { Id = "jamie", DisplayName = "jamie", Identity = "jamie@example.gov", RoleName = "Workspace owner", IsCustomRole = false, Scope = "all envs", LastActive = "now", Source = "OIDC · Entra", PrincipalKind = "user" },
            new HonuaConsoleTeamMember { Id = "k.tan", DisplayName = "k.tan", Identity = "k.tan@example.gov", RoleName = "GIS team", IsCustomRole = true, Scope = "all envs", LastActive = "2m", Source = "OIDC · Entra", PrincipalKind = "user" },
            new HonuaConsoleTeamMember { Id = "partner-readonly", DisplayName = "partner-readonly", Identity = "api-key", RoleName = "Org viewer", IsCustomRole = false, Scope = "prod only", LastActive = "2m", Source = "API key", PrincipalKind = "api-key" },
            new HonuaConsoleTeamMember { Id = "field-team", DisplayName = "field-team", Identity = "group · 4 members", RoleName = "Editor", IsCustomRole = false, Scope = "dev only", LastActive = "1h", Source = "Entra group", PrincipalKind = "group" },
        ],
        Invitations =
        [
            new HonuaConsoleTeamInvitation { Id = "inv-1", Identity = "r.kim@example.gov", RoleName = "Editor", IsCustomRole = false, Scope = "all envs", Status = "sent 2d · expires in 5d" },
            new HonuaConsoleTeamInvitation { Id = "inv-2", Identity = "tile-warmer (machine)", RoleName = "Custom · tile-warm", IsCustomRole = true, Scope = "prod only · 2 services", Status = "sent 1h" },
        ],
        ActiveCount = 14,
        PendingCount = 2,
        DeactivatedCount = 1,
        CanInvite = true,
    });

    private sealed class FakeRbacDataSource : IRbacAccessDataSource
    {
        public RbacOverviewLoad OverviewLoad { get; set; } = new(null, []);
        public TeamMembershipLoad MembershipLoad { get; set; } = new(null, []);

        public Task<RbacOverviewLoad> LoadOverviewAsync(string workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OverviewLoad);

        public Task<TeamMembershipLoad> LoadMembershipAsync(string workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(MembershipLoad);

        public RbacRoleMutationResult CreateResult { get; set; } = new(true, null);
        public RbacRoleMutationResult DeleteResult { get; set; } = new(true, null);
        public RbacAuditLoad AuditLoad { get; set; } = new(Array.Empty<RbacAuditEntryView>(), Array.Empty<RbacCapabilityState>());

        public Task<RbacRoleMutationResult> CreateRoleAsync(string workspaceId, string name, string? description, IReadOnlyList<string> grantedPermissionKeys, CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateResult);

        public Task<RbacRoleMutationResult> DeleteRoleAsync(string workspaceId, string roleId, CancellationToken cancellationToken = default) =>
            Task.FromResult(DeleteResult);

        public Task<RbacAuditLoad> LoadAuditAsync(string workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(AuditLoad);
    }
}
