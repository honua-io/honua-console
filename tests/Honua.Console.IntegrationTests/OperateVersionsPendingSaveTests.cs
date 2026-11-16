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
        Assert.True(ctx.Services.GetRequiredService<ConsoleAgentContextRegistry>().Current.Unsaved.ChangedFieldCount > 0);
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
}
