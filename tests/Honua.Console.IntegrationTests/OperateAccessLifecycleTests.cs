using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class OperateAccessLifecycleTests
{
    [Fact]
    public void RolesNavigationPreservesEnvironment()
    {
        using var context = Context(true);
        Navigate(context, "env west");
        var links = context.Render<OperateAccessRolesPage>().FindAll(".rbac-navigation a");

        Assert.Equal(new[] { "operate/access?environment=env%20west", "operate/access/members?environment=env%20west" },
            links.Select(link => link.GetAttribute("href")));
        Assert.Contains("env west", context.Services.GetRequiredService<DeferredRbac>().OverviewRequests);
    }

    [Fact]
    public void MembersNavigationPreservesEnvironment()
    {
        using var context = Context(true);
        Navigate(context, "env west");
        var links = context.Render<OperateAccessMembersPage>().FindAll(".rbac-navigation a");

        Assert.Equal(new[] { "operate/access?environment=env%20west", "operate/access/members?environment=env%20west" },
            links.Select(link => link.GetAttribute("href")));
        Assert.Contains("env west", context.Services.GetRequiredService<DeferredRbac>().OverviewRequests);
    }

    [Fact]
    public void RolesRetiredWorkspaceQueryRefusesTheDefaultEnvironment()
    {
        using var context = Context(true);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/operate/access?Workspace=prod");
        var markup = context.Render<OperateAccessRolesPage>().Markup;

        Assert.Contains("out of date", markup, StringComparison.Ordinal);
        var data = context.Services.GetRequiredService<DeferredRbac>();
        Assert.Empty(data.OverviewRequests);
        Assert.Empty(data.MembershipRequests);
    }

    [Fact]
    public void MembersRetiredWorkspaceQueryRefusesTheDefaultEnvironment()
    {
        using var context = Context(true);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/operate/access/members?workspace=prod");
        var markup = context.Render<OperateAccessMembersPage>().Markup;

        Assert.Contains("out of date", markup, StringComparison.Ordinal);
        var data = context.Services.GetRequiredService<DeferredRbac>();
        Assert.Empty(data.OverviewRequests);
        Assert.Empty(data.MembershipRequests);
    }

    [Fact]
    public async Task Roles_RetiredLinkDropsAuditAndAnInFlightCreate()
    {
        using var context = Context(true);
        var data = context.Services.GetRequiredService<DeferredRbac>();
        data.Audit = _ => Task.FromResult(new RbacAuditLoad([new(1, "t", "ada", "create", "custom", "ok")], []));
        var created = new TaskCompletionSource<RbacRoleMutationResult>();
        data.Create = () => created.Task;
        Navigate(context, "first");
        var page = context.Render<OperateAccessRolesPage>();
        await page.Find("[data-rbac-audit]").ClickAsync();
        page.WaitForAssertion(() => Assert.Contains("data-rbac-audit-table", page.Markup, StringComparison.Ordinal));
        OpenDraft(page, "draft");
        var pending = page.Find("[data-rbac-create-form]").SubmitAsync();

        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/operate/access?workspace=prod");
        created.SetResult(new(true, null));
        await pending;

        page.WaitForAssertion(() => Assert.Contains("out of date", page.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain("data-rbac-audit-table", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Couldn't create the role", page.Markup, StringComparison.Ordinal);
        Assert.Equal(["first"], data.OverviewRequests);
    }

    [Fact]
    public async Task Members_RetiredLinkIgnoresTheInFlightRoster()
    {
        using var context = Context(true);
        var data = context.Services.GetRequiredService<DeferredRbac>();
        var pendingMembership = new TaskCompletionSource<TeamMembershipLoad>();
        data.Membership = environment => pendingMembership.Task;
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/operate/access/members?environment=first");
        var page = context.Render<OperateAccessMembersPage>();

        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/operate/access/members?Workspace=kept");
        pendingMembership.SetResult(Membership("first"));

        page.WaitForAssertion(() => Assert.Contains("out of date", page.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain("first user", page.Markup, StringComparison.Ordinal);
        Assert.Empty(data.OverviewRequests);
        Assert.Equal(["first"], data.MembershipRequests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Roles_IgnoreOverviewFromPreviousEnvironment(bool v4)
    {
        using var context = Context(v4);
        var data = context.Services.GetRequiredService<DeferredRbac>();
        var oldLoad = new TaskCompletionSource<RbacOverviewLoad>();
        data.Overview = environment => environment == "first" ? oldLoad.Task : Task.FromResult(Overview(environment));
        Navigate(context, "first");
        var page = context.Render<OperateAccessRolesPage>();
        Navigate(context, "second");
        page.WaitForAssertion(() => Assert.Contains("second role", page.Markup));

        await page.InvokeAsync(() => oldLoad.SetResult(Overview("first")));

        page.WaitForAssertion(() => Assert.Contains("second role", page.Markup));
        Assert.DoesNotContain("first role", page.Markup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Members_IgnoreRosterFromPreviousEnvironment(bool v4)
    {
        using var context = Context(v4);
        var data = context.Services.GetRequiredService<DeferredRbac>();
        var oldLoad = new TaskCompletionSource<TeamMembershipLoad>();
        data.Membership = environment => environment == "first" ? oldLoad.Task : Task.FromResult(Membership(environment));
        Navigate(context, "first");
        var page = context.Render<OperateAccessMembersPage>();
        Navigate(context, "second");
        page.WaitForAssertion(() => Assert.Contains("second user", page.Markup));

        await page.InvokeAsync(() => oldLoad.SetResult(Membership("first")));

        page.WaitForAssertion(() => Assert.Contains("second user", page.Markup));
        Assert.DoesNotContain("first user", page.Markup);
        Assert.DoesNotContain("first", data.OverviewRequests);
    }

    [Fact]
    public async Task Roles_IgnoreAuditFromPreviousEnvironment()
    {
        using var context = Context(true);
        var data = context.Services.GetRequiredService<DeferredRbac>();
        var oldAudit = new TaskCompletionSource<RbacAuditLoad>();
        data.Audit = _ => oldAudit.Task;
        Navigate(context, "first");
        var page = context.Render<OperateAccessRolesPage>();
        var pending = page.Find("[data-rbac-audit]").ClickAsync(new());
        Navigate(context, "second");
        page.WaitForAssertion(() => Assert.Contains("second role", page.Markup));
        await page.InvokeAsync(() => oldAudit.SetResult(new([new(1, "yesterday", "first actor", "create", "role", "success")], [])));
        await pending;

        Assert.Empty(page.FindAll("[data-rbac-audit-panel]"));
        Assert.DoesNotContain("first actor", page.Markup);
    }

    [Fact]
    public void Roles_ConfirmingPreviousEnvironmentRoleDoesNotDeleteInCurrentEnvironment()
    {
        using var context = Context(true);
        var data = context.Services.GetRequiredService<DeferredRbac>();
        Navigate(context, "first");
        var page = context.Render<OperateAccessRolesPage>();
        page.Find("[data-rbac-delete-role]").Click();
        Navigate(context, "second");
        page.WaitForAssertion(() => Assert.Contains("second role", page.Markup));
        foreach (var accept in page.FindAll("[data-console-confirm-accept]")) accept.Click();

        Assert.Empty(data.DeletedEnvironments);
    }

    [Fact]
    public async Task Roles_PendingCreationLocksDraftAndToggle()
    {
        using var context = Context(true);
        var data = context.Services.GetRequiredService<DeferredRbac>();
        var created = new TaskCompletionSource<RbacRoleMutationResult>();
        data.Create = () => created.Task;
        Navigate(context, "first");
        var page = context.Render<OperateAccessRolesPage>();
        OpenDraft(page, "original");
        var pending = page.Find("[data-rbac-create-form]").SubmitAsync();

        Assert.True(page.Find("[data-rbac-custom-role]").HasAttribute("disabled"));
        Assert.True(page.Find("[data-rbac-new-role-name]").HasAttribute("disabled"));
        Assert.True(page.Find(".rbac-create-grants").HasAttribute("disabled"));
        await page.InvokeAsync(() => created.SetResult(new(false, "Try again")));
        await pending;
        Assert.Equal("original", page.Find("[data-rbac-new-role-name]").GetAttribute("value"));
        Assert.False(page.Find("[data-rbac-new-role-name]").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Roles_PreviousCreationCannotCloseNewEnvironmentDraft()
    {
        using var context = Context(true);
        var data = context.Services.GetRequiredService<DeferredRbac>();
        var created = new TaskCompletionSource<RbacRoleMutationResult>();
        data.Create = () => created.Task;
        Navigate(context, "first");
        var page = context.Render<OperateAccessRolesPage>();
        OpenDraft(page, "first draft");
        var pending = page.Find("[data-rbac-create-form]").SubmitAsync();
        Navigate(context, "second");
        page.WaitForAssertion(() => Assert.Contains("second role", page.Markup));
        OpenDraft(page, "second draft");
        await page.InvokeAsync(() => created.SetResult(new(true, null)));
        await pending;

        Assert.Equal("second draft", page.Find("[data-rbac-new-role-name]").GetAttribute("value"));
        Assert.Equal(new[] { "first", "second" }, data.OverviewRequests);
    }

    [Fact]
    public async Task Members_IgnorePreviousEnvironmentOverviewAfterRosterLoaded()
    {
        using var context = Context(true);
        var data = context.Services.GetRequiredService<DeferredRbac>();
        var oldOverview = new TaskCompletionSource<RbacOverviewLoad>();
        data.Overview = environment => environment == "first" ? oldOverview.Task : Task.FromResult(Overview(environment));
        Navigate(context, "first");
        var page = context.Render<OperateAccessMembersPage>();
        Navigate(context, "second");
        page.WaitForAssertion(() => Assert.Contains("second user", page.Markup));
        page.Find("[data-rbac-invite-open]").Click();
        Assert.Equal("second role", page.Find("[data-rbac-invite-group=role] option").TextContent);
        await page.InvokeAsync(() => oldOverview.SetResult(Overview("first")));

        Assert.Contains("second user", page.Markup);
        Assert.Equal("second role", page.Find("[data-rbac-invite-group=role] option").TextContent);
    }

    [Fact]
    public async Task Roles_CurrentCreationReloadsAndClosesDraft()
    {
        using var context = Context(true);
        var data = context.Services.GetRequiredService<DeferredRbac>();
        Navigate(context, "first");
        var page = context.Render<OperateAccessRolesPage>();
        OpenDraft(page, "new custom role");

        await page.Find("[data-rbac-create-form]").SubmitAsync();

        Assert.Empty(page.FindAll("[data-rbac-create-form]"));
        Assert.Equal(new[] { "first", "first" }, data.OverviewRequests);
    }

    [Fact]
    public void Roles_CurrentConfirmationDeletesInOriginEnvironment()
    {
        using var context = Context(true);
        var data = context.Services.GetRequiredService<DeferredRbac>();
        Navigate(context, "first");
        var page = context.Render<OperateAccessRolesPage>();
        page.Find("[data-rbac-delete-role]").Click();
        page.Find("[data-console-confirm-accept]").Click();

        page.WaitForAssertion(() => Assert.Equal(new[] { "first" }, data.DeletedEnvironments));
        Assert.Equal(new[] { "first", "first" }, data.OverviewRequests);
    }

    [Fact]
    public async Task Roles_DisposedPendingCreationDoesNotReload()
    {
        using var context = Context(true);
        var data = context.Services.GetRequiredService<DeferredRbac>();
        var created = new TaskCompletionSource<RbacRoleMutationResult>();
        data.Create = () => created.Task;
        Navigate(context, "first");
        var page = context.Render<OperateAccessRolesPage>();
        OpenDraft(page, "draft");
        var pending = page.Find("[data-rbac-create-form]").SubmitAsync();
        await context.DisposeComponentsAsync();
        created.SetResult(new(true, null));
        await pending;

        Assert.Equal(new[] { "first" }, data.OverviewRequests);
    }

    [Fact]
    public async Task Roles_NewDraftSendsOnlyListedPermissionKeys()
    {
        using var context = Context(true);
        var data = context.Services.GetRequiredService<DeferredRbac>();
        data.Overview = environment => Task.FromResult(new RbacOverviewLoad(
            new(environment, environment, [], [new("view-public", "View public"), new("comment", "Comment")], [], 3, 0, 0, true), []));
        Navigate(context, "first");
        var page = context.Render<OperateAccessRolesPage>();
        OpenDraft(page, "Field crew");

        Assert.True(page.FindAll("[data-rbac-create-form] .rbac-create-grants input")[0].HasAttribute("checked"));
        await page.Find("[data-rbac-create-form]").SubmitAsync();

        Assert.Equal(new[] { "view-public" }, data.CreatedGrants);
    }

    private static void OpenDraft(IRenderedComponent<OperateAccessRolesPage> page, string name)
    {
        page.Find("[data-rbac-custom-role]").Click();
        page.Find("[data-rbac-new-role-name]").Input(name);
    }

    private static void Navigate(BunitContext context, string environment) =>
        context.Services.GetRequiredService<NavigationManager>().NavigateTo($"/operate/access?environment={environment}");

    private static BunitContext Context(bool v4)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.AddConsoleNotifications();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = v4 });
        context.Services.AddSingleton<DeferredRbac>();
        context.Services.AddSingleton<IRbacAccessDataSource>(services => services.GetRequiredService<DeferredRbac>());
        return context;
    }

    private static RbacOverviewLoad Overview(string environment) => new(
        new(environment, environment, [], [new("view", "View")],
            [new("custom", $"{environment} role", null, true, [new("view", "granted", null)])], 0, 1, 1, true), []);

    private static TeamMembershipLoad Membership(string environment) => new(
        new(environment, environment, [new("user", $"{environment} user", "user@example.gov", "custom", "Custom", true, null, null, null, null)], [], 1, 0, 0, true), []);

    private sealed class DeferredRbac : IRbacAccessDataSource
    {
        public Func<string, Task<RbacOverviewLoad>> Overview { get; set; } = environment => Task.FromResult(OperateAccessLifecycleTests.Overview(environment));
        public Func<string, Task<TeamMembershipLoad>> Membership { get; set; } = environment => Task.FromResult(OperateAccessLifecycleTests.Membership(environment));
        public Func<string, Task<RbacAuditLoad>> Audit { get; set; } = _ => Task.FromResult(new RbacAuditLoad([], []));
        public Func<Task<RbacRoleMutationResult>> Create { get; set; } = () => Task.FromResult(new RbacRoleMutationResult(true, null));
        public List<string> OverviewRequests { get; } = [];
        public List<string> MembershipRequests { get; } = [];
        public List<string> DeletedEnvironments { get; } = [];

        public Task<RbacOverviewLoad> LoadOverviewAsync(string environmentId, CancellationToken cancellationToken = default)
        {
            OverviewRequests.Add(environmentId);
            return Overview(environmentId);
        }

        public Task<TeamMembershipLoad> LoadMembershipAsync(string environmentId, CancellationToken cancellationToken = default)
        {
            MembershipRequests.Add(environmentId);
            return Membership(environmentId);
        }
        public Task<RbacAuditLoad> LoadAuditAsync(string environmentId, CancellationToken cancellationToken = default) => Audit(environmentId);
        public IReadOnlyList<string> CreatedGrants { get; private set; } = [];

        public Task<RbacRoleMutationResult> CreateRoleAsync(string environmentId, string name, string? description, IReadOnlyList<string> grantedPermissionKeys, CancellationToken cancellationToken = default)
        {
            CreatedGrants = grantedPermissionKeys;
            return Create();
        }

        public Task<RbacRoleMutationResult> DeleteRoleAsync(string environmentId, string roleId, CancellationToken cancellationToken = default)
        {
            DeletedEnvironments.Add(environmentId);
            return Task.FromResult(new RbacRoleMutationResult(true, null));
        }
    }
}
