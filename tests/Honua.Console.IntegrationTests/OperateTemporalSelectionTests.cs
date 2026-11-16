using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Microsoft.AspNetCore.Components.Web;

namespace Honua.Console.IntegrationTests;

public sealed partial class OperateTemporalPageRenderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Diff_RejectsLateResponseAfterSelectionChangesAwayAndBack(bool enabled)
    {
        var client = SelectionClient();
        var pending = new TaskCompletionSource<TemporalDiff>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.PendingDiffs[RollbackSource.SourceId] = pending.Task;
        using var ctx = NewContext(client, enabled);
        var page = ctx.Render<OperateTemporalPage>();
        InspectFirstSource(page);
        var request = Action(page, "Compare").ClickAsync(new MouseEventArgs());
        page.WaitForAssertion(() => Assert.Equal(1, client.DiffCalls));
        page.Find("select[aria-label='From checkpoint']").Change("cp-now");
        page.Find("select[aria-label='From checkpoint']").Change("cp-prev");
        Assert.True(Action(page, "Compare").HasAttribute("disabled"));
        pending.SetResult(TemporalDiff.Blocked(StaleSelectionBinding));
        await request;
        Assert.DoesNotContain("Discarded selection response", page.Markup, StringComparison.Ordinal);
        Assert.False(Action(page, "Compare").HasAttribute("disabled"));
        client.PendingDiffs.Clear();
        client.Diff = TemporalDiff.Blocked(StaleSelectionBinding);
        Action(page, "Compare").Click();
        page.WaitForAssertion(() => Assert.Contains("Discarded selection response", page.Markup, StringComparison.Ordinal));
        page.Find("select[aria-label='To checkpoint']").Change("cp-prev");
        Assert.DoesNotContain("Discarded selection response", page.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Timeline_RejectsLateResponseAndClearsPreviousResultOnInput(bool enabled)
    {
        var client = SelectionClient();
        var pending = new TaskCompletionSource<TemporalFeatureTimeline>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.PendingTimeline = pending.Task;
        using var ctx = NewContext(client, enabled);
        var page = ctx.Render<OperateTemporalPage>();
        InspectFirstSource(page);
        page.Find("input[aria-label='Feature id']").Input("f-1");
        var request = Action(page, "Open Timeline").ClickAsync(new MouseEventArgs());
        page.WaitForAssertion(() => Assert.True(Action(page, "Open Timeline").HasAttribute("disabled")));
        page.Find("input[aria-label='Feature id']").Input("f-2");
        page.Find("input[aria-label='Feature id']").Input("f-1");
        pending.SetResult(new TemporalFeatureTimeline("f-1", RollbackSource.SourceId, [], StaleSelectionBinding));
        await request;
        Assert.DoesNotContain("Discarded selection response", page.Markup, StringComparison.Ordinal);
        client.PendingTimeline = null;
        client.Timeline = new TemporalFeatureTimeline("f-1", RollbackSource.SourceId, []);
        Action(page, "Open Timeline").Click();
        page.WaitForAssertion(() => Assert.Contains("No revisions were found for feature f-1", page.Markup, StringComparison.Ordinal));
        page.Find("input[aria-label='Feature id']").Input("f-2");
        Assert.DoesNotContain("No revisions were found for feature f-1", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diff_UnknownCheckpointNavigationRejectsPendingResponse()
    {
        var client = SelectionClient();
        var pending = new TaskCompletionSource<TemporalDiff>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.PendingDiffs[RollbackSource.SourceId] = pending.Task;
        using var ctx = NewContext(client, true);
        Navigate(ctx, "src-parcels", "cp-prev");
        var page = ctx.Render<OperateTemporalPage>();
        var request = Action(page, "Compare").ClickAsync(new MouseEventArgs());
        page.WaitForAssertion(() => Assert.Equal(1, client.DiffCalls));
        Navigate(ctx, "src-parcels", "missing");
        page.Render();
        pending.SetResult(TemporalDiff.Blocked(StaleSelectionBinding));
        await request;
        Assert.DoesNotContain("Discarded selection response", page.Markup, StringComparison.Ordinal);
    }

    private static FakeTemporalClient SelectionClient() => new()
    {
        Workspace = WorkspaceWith(RollbackSource),
        Checkpoints = new TemporalCheckpointList([Checkpoint("cp-now", "Now"), Checkpoint("cp-prev", "Before")])
    };

    private static AngleSharp.Dom.IElement Action(IRenderedComponent<OperateTemporalPage> page, string text) =>
        page.FindAll("button").First(button => button.TextContent.Trim() == text);

    private static readonly TemporalBindingState StaleSelectionBinding = new(
        "Inspection", TemporalBindingState.MissingBinding, "test", "Discarded selection response");
}
