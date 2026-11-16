using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Microsoft.AspNetCore.Components.Web;

namespace Honua.Console.IntegrationTests;

public sealed partial class OperateTemporalPageRenderTests
{
    [Theory]
    [InlineData(false, RollbackSelection.Target)]
    [InlineData(false, RollbackSelection.Scope)]
    [InlineData(true, RollbackSelection.Target)]
    [InlineData(true, RollbackSelection.Scope)]
    public async Task Rollback_RejectsPendingPlanAfterTargetOrScopeEdits(bool enabled, RollbackSelection selection)
    {
        var client = SelectionClient();
        var pending = new TaskCompletionSource<TemporalRollbackPlan>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.PendingPlan = pending.Task;
        using var ctx = NewContext(client, enabled);
        var page = ctx.Render<OperateTemporalPage>();
        InspectFirstSource(page);
        var request = Action(page, "Plan Rollback").ClickAsync(new MouseEventArgs());
        page.WaitForAssertion(() => Assert.True(Action(page, "Plan Rollback").HasAttribute("disabled")));
        EditRollbackSelection(page, selection);
        pending.SetResult(SelectionPlan);
        await request;
        Assert.DoesNotContain(page.FindAll("button"), button => button.TextContent.Trim() == "Execute Rollback Job");
        Assert.False(Action(page, "Plan Rollback").HasAttribute("disabled"));
    }

    [Theory]
    [InlineData(false, RollbackSelection.Target)]
    [InlineData(false, RollbackSelection.Scope)]
    [InlineData(true, RollbackSelection.Target)]
    [InlineData(true, RollbackSelection.Scope)]
    public void Rollback_TargetOrScopeEditClearsPlanAndConfirmation(bool enabled, RollbackSelection selection)
    {
        var client = SelectionClient();
        client.RollbackPlan = SelectionPlan;
        using var ctx = NewContext(client, enabled);
        var page = ctx.Render<OperateTemporalPage>();
        InspectFirstSource(page);
        Action(page, "Plan Rollback").Click();
        Action(page, "Execute Rollback Job").Click();
        Assert.Single(page.FindAll("[data-console-confirm-accept]"));
        EditRollbackSelection(page, selection);
        Assert.Empty(page.FindAll("[data-console-confirm-accept]"));
        Assert.DoesNotContain(page.FindAll("button"), button => button.TextContent.Trim() == "Execute Rollback Job");
        Assert.Equal(0, client.ExecuteRollbackCalls);
        Action(page, "Plan Rollback").Click();
        Assert.Single(page.FindAll("button"), button => button.TextContent.Trim() == "Execute Rollback Job");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Rollback_ToCheckpointEditDoesNotInvalidatePlan(bool enabled)
    {
        var client = SelectionClient();
        client.RollbackPlan = SelectionPlan;
        using var ctx = NewContext(client, enabled);
        var page = ctx.Render<OperateTemporalPage>();
        InspectFirstSource(page);
        Action(page, "Plan Rollback").Click();
        page.Find("select[aria-label='To checkpoint']").Change("cp-prev");
        Assert.Single(page.FindAll("button"), button => button.TextContent.Trim() == "Execute Rollback Job");
    }

    [Fact]
    public async Task Rollback_UnknownCheckpointNavigationRejectsPendingPlan()
    {
        var client = SelectionClient();
        var pending = new TaskCompletionSource<TemporalRollbackPlan>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.PendingPlan = pending.Task;
        using var ctx = NewContext(client, true);
        Navigate(ctx, "src-parcels", "cp-prev");
        var page = ctx.Render<OperateTemporalPage>();
        var request = Action(page, "Plan Rollback").ClickAsync(new MouseEventArgs());
        page.WaitForAssertion(() => Assert.True(Action(page, "Plan Rollback").HasAttribute("disabled")));
        Navigate(ctx, "src-parcels", "missing");
        page.Render();
        pending.SetResult(SelectionPlan);
        await request;
        Assert.DoesNotContain(page.FindAll("button"), button => button.TextContent.Trim() == "Execute Rollback Job");
    }

    [Fact]
    public void Rollback_UnknownCheckpointNavigationClosesConfirmation()
    {
        var client = SelectionClient();
        client.RollbackPlan = SelectionPlan;
        using var ctx = NewContext(client, true);
        Navigate(ctx, "src-parcels", "cp-prev");
        var page = ctx.Render<OperateTemporalPage>();
        Action(page, "Plan Rollback").Click();
        Action(page, "Execute Rollback Job").Click();
        Assert.Single(page.FindAll("[data-console-confirm-accept]"));
        Navigate(ctx, "src-parcels", "missing");
        page.Render();
        Assert.Empty(page.FindAll("[data-console-confirm-accept]"));
        Assert.DoesNotContain(page.FindAll("button"), button => button.TextContent.Trim() == "Execute Rollback Job");
        Assert.Equal(0, client.ExecuteRollbackCalls);
    }

    public enum RollbackSelection { Target, Scope }

    private static void EditRollbackSelection(IRenderedComponent<OperateTemporalPage> page, RollbackSelection selection)
    {
        if (selection == RollbackSelection.Scope) page.Find("select[aria-label='Rollback scope']").Change("Layer");
        else page.Find("select[aria-label='From checkpoint']").Change("cp-now");
    }

    private static readonly TemporalRollbackPlan SelectionPlan = new("selection-plan", "src-parcels",
        TemporalRollbackScope.ChangeSet, "cp-prev", "cp-now", 12, true, true,
        TemporalRollbackMode.DataRevert, "medium", [], []);
}
