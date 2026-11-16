using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class OperateAccessLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Roles_IgnoreOverviewFromPreviousWorkspace(bool v4)
    {
        using var context = Context(v4, out var data);
        var oldLoad = new TaskCompletionSource<RbacOverviewLoad>();
        data.Overview = workspace => workspace == "first" ? oldLoad.Task : Task.FromResult(Overview(workspace));
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
    public async Task Members_IgnoreRosterFromPreviousWorkspace(bool v4)
    {
        using var context = Context(v4, out var data);
        var oldLoad = new TaskCompletionSource<TeamMembershipLoad>();
        data.Membership = workspace => workspace == "first" ? oldLoad.Task : Task.FromResult(Membership(workspace));
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
    public async Task Roles_IgnoreAuditFromPreviousWorkspace()
    {
        using var context = Context(true, out var data);
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
    public void Roles_ConfirmingPreviousWorkspaceRoleDoesNotDeleteInCurrentWorkspace()
    {
        using var context = Context(true, out var data);
        Navigate(context, "first");
        var page = context.Render<OperateAccessRolesPage>();
        page.Find("[data-rbac-delete-role]").Click();
        Navigate(context, "second");
        page.WaitForAssertion(() => Assert.Contains("second role", page.Markup));
        foreach (var accept in page.FindAll("[data-console-confirm-accept]")) accept.Click();

        Assert.Empty(data.DeletedWorkspaces);
    }

    [Fact]
    public async Task Roles_PendingCreationLocksDraftAndToggle()
    {
        using var context = Context(true, out var data);
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
    public async Task Roles_PreviousCreationCannotCloseNewWorkspaceDraft()
    {
        using var context = Context(true, out var data);
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
    public async Task Members_IgnorePreviousWorkspaceOverviewAfterRosterLoaded()
    {
        using var context = Context(true, out var data);
        var oldOverview = new TaskCompletionSource<RbacOverviewLoad>();
        data.Overview = workspace => workspace == "first" ? oldOverview.Task : Task.FromResult(Overview(workspace));
        Navigate(context, "first");
        var page = context.Render<OperateAccessMembersPage>();
        Navigate(context, "second");
        page.WaitForAssertion(() => Assert.Contains("second user", page.Markup));
        await page.InvokeAsync(() => oldOverview.SetResult(Overview("first")));

        Assert.Contains("second user", page.Markup);
        Assert.DoesNotContain("first role", page.Markup);
    }

    [Fact]
    public async Task Roles_CurrentCreationReloadsAndClosesDraft()
    {
        using var context = Context(true, out var data);
        Navigate(context, "first");
        var page = context.Render<OperateAccessRolesPage>();
        OpenDraft(page, "new custom role");

        await page.Find("[data-rbac-create-form]").SubmitAsync();

        Assert.Empty(page.FindAll("[data-rbac-create-form]"));
        Assert.Equal(new[] { "first", "first" }, data.OverviewRequests);
    }

    [Fact]
    public void Roles_CurrentConfirmationDeletesInOriginWorkspace()
    {
        using var context = Context(true, out var data);
        Navigate(context, "first");
        var page = context.Render<OperateAccessRolesPage>();
        page.Find("[data-rbac-delete-role]").Click();
        page.Find("[data-console-confirm-accept]").Click();

        page.WaitForAssertion(() => Assert.Equal(new[] { "first" }, data.DeletedWorkspaces));
        Assert.Equal(new[] { "first", "first" }, data.OverviewRequests);
    }

    [Fact]
    public async Task Roles_DisposedPendingCreationDoesNotReload()
    {
        using var context = Context(true, out var data);
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

    private static void OpenDraft(IRenderedComponent<OperateAccessRolesPage> page, string name)
    {
        page.Find("[data-rbac-custom-role]").Click();
        page.Find("[data-rbac-new-role-name]").Input(name);
    }

    private static void Navigate(BunitContext context, string workspace) =>
        context.Services.GetRequiredService<NavigationManager>().NavigateTo($"/operate/access?workspace={workspace}");

    private static BunitContext Context(bool v4, out DeferredRbac data)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.AddConsoleNotifications();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = v4 });
        data = new DeferredRbac();
        context.Services.AddSingleton<IRbacAccessDataSource>(data);
        return context;
    }

    private static RbacOverviewLoad Overview(string workspace) => new(
        new(workspace, workspace, [], [new("view", "View")],
            [new("custom", $"{workspace} role", null, true, [new("view", "granted", null)])], 0, 1, 1, true), []);

    private static TeamMembershipLoad Membership(string workspace) => new(
        new(workspace, workspace, [new("user", $"{workspace} user", "user@example.gov", "custom", "Custom", true, null, null, null, null)], [], 1, 0, 0, true), []);

    private sealed class DeferredRbac : IRbacAccessDataSource
    {
        public Func<string, Task<RbacOverviewLoad>> Overview { get; set; } = workspace => Task.FromResult(OperateAccessLifecycleTests.Overview(workspace));
        public Func<string, Task<TeamMembershipLoad>> Membership { get; set; } = workspace => Task.FromResult(OperateAccessLifecycleTests.Membership(workspace));
        public Func<string, Task<RbacAuditLoad>> Audit { get; set; } = _ => Task.FromResult(new RbacAuditLoad([], []));
        public Func<Task<RbacRoleMutationResult>> Create { get; set; } = () => Task.FromResult(new RbacRoleMutationResult(true, null));
        public List<string> OverviewRequests { get; } = [];
        public List<string> DeletedWorkspaces { get; } = [];

        public Task<RbacOverviewLoad> LoadOverviewAsync(string workspaceId, CancellationToken cancellationToken = default)
        {
            OverviewRequests.Add(workspaceId);
            return Overview(workspaceId);
        }

        public Task<TeamMembershipLoad> LoadMembershipAsync(string workspaceId, CancellationToken cancellationToken = default) => Membership(workspaceId);
        public Task<RbacAuditLoad> LoadAuditAsync(string workspaceId, CancellationToken cancellationToken = default) => Audit(workspaceId);
        public Task<RbacRoleMutationResult> CreateRoleAsync(string workspaceId, string name, string? description, IReadOnlyList<string> grantedPermissionKeys, CancellationToken cancellationToken = default) => Create();

        public Task<RbacRoleMutationResult> DeleteRoleAsync(string workspaceId, string roleId, CancellationToken cancellationToken = default)
        {
            DeletedWorkspaces.Add(workspaceId);
            return Task.FromResult(new RbacRoleMutationResult(true, null));
        }
    }
}
