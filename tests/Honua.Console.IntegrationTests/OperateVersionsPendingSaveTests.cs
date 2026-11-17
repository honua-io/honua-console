using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed partial class OperateVersionsPageRenderTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SuccessfulSave_PreservesEditsTypedWhilePending(bool enabled, bool alter)
    {
        using var ctx = CreateContext(enabled);
        var pending = new TaskCompletionSource<VersionOperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = new SeededVersionOperation { PendingSave = pending.Task };
        ctx.Services.AddSingleton<IVersionManagementOperation>(operation);
        var page = ctx.Render<OperateVersionsPage>();
        Load(page);
        if (alter) page.Find("[data-alter]").Click();
        page.Find("[data-version-name]").Input("Submitted name");
        var save = page.Find("[data-submit-version]").ClickAsync(new MouseEventArgs());
        page.WaitForAssertion(() => Assert.Equal("Submitted name", alter ? operation.Altered?.VersionName : operation.Created?.VersionName));
        page.Find("[data-version-name]").Input("Next name");
        page.Find("[data-version-owner]").Input("Next owner");
        page.Find("[data-version-description]").Input("Next description");
        page.Find("[data-version-access]").Change("public");
        pending.SetResult(VersionOperationResult.Success("Saved", "Saved submitted draft."));
        await save;

        Assert.Equal("Next name", page.Find("[data-version-name]").GetAttribute("value"));
        Assert.Equal("Next owner", page.Find("[data-version-owner]").GetAttribute("value"));
        Assert.Equal("Next description", page.Find("[data-version-description]").GetAttribute("value"));
        Assert.Equal("public", page.Find("[data-version-access]").GetAttribute("value"));
        var changedFields = ctx.Services.GetRequiredService<ConsoleAgentContextRegistry>().Current.Unsaved.ChangedFieldCount;
        Assert.Equal(enabled ? 4 : 0, changedFields);
        operation.PendingSave = null;
        page.Find("[data-submit-version]").Click();
        page.WaitForAssertion(() => Assert.Equal("Next name", alter ? operation.Altered?.VersionName : operation.Created?.VersionName));
        Assert.Equal(string.Empty, page.Find("[data-version-name]").GetAttribute("value"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfulSave_DoesNotResetReplacedAlterForm(bool enabled)
    {
        using var ctx = CreateContext(enabled);
        var pending = new TaskCompletionSource<VersionOperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = new SeededVersionOperation { PendingSave = pending.Task };
        ctx.Services.AddSingleton<IVersionManagementOperation>(operation);
        var page = ctx.Render<OperateVersionsPage>();
        Load(page);
        page.Find("[data-alter]").Click();
        page.Find("[data-version-description]").Input("Submitted edit");
        var save = page.Find("[data-submit-version]").ClickAsync(new MouseEventArgs());
        page.WaitForAssertion(() => Assert.NotNull(operation.Altered));
        page.Find("[data-cancel-alter]").Click();
        page.Find("[data-alter]").Click();
        pending.SetResult(VersionOperationResult.Success("Saved", "Saved."));
        await save;
        Assert.Equal("Review edits", page.Find("[data-version-name]").GetAttribute("value"));
        Assert.Single(page.FindAll("[data-cancel-alter]"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task SuccessfulSave_AfterLeavingDoesNotReloadVersions(bool enabled, bool alter)
    {
        using var ctx = CreateContext(enabled);
        var pending = new TaskCompletionSource<VersionOperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = new SeededVersionOperation { PendingSave = pending.Task };
        ctx.Services.AddSingleton<IVersionManagementOperation>(operation);
        var page = ctx.Render<OperateVersionsPage>();
        Load(page);
        if (alter) page.Find("[data-alter]").Click();
        else page.Find("[data-version-name]").Input("New branch");
        var save = page.Find("[data-submit-version]").ClickAsync(new MouseEventArgs());
        page.WaitForAssertion(() => Assert.True(alter ? operation.Altered is not null : operation.Created is not null));
        await ctx.DisposeComponentsAsync();
        pending.SetResult(VersionOperationResult.Success("Saved", "Saved."));
        await save;
        Assert.Equal(1, operation.ListCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfulDelete_AfterLeavingDoesNotReloadVersions(bool enabled)
    {
        using var ctx = CreateContext(enabled);
        var pending = new TaskCompletionSource<VersionOperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = new SeededVersionOperation { PendingSave = pending.Task };
        ctx.Services.AddSingleton<IVersionManagementOperation>(operation);
        var page = ctx.Render<OperateVersionsPage>();
        Load(page);
        page.Find("[data-delete]").Click();
        var deletion = page.Find("[data-console-confirm-accept]").ClickAsync(new MouseEventArgs());
        page.WaitForAssertion(() => Assert.NotNull(operation.DeletedVersion));
        await ctx.DisposeComponentsAsync();
        pending.SetResult(VersionOperationResult.Success("Deleted", "Deleted."));
        await deletion;
        Assert.Equal(1, operation.ListCalls);
    }
}
