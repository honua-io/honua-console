using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

public sealed partial class ApprovalInboxPageRenderTests
{
    [Fact]
    public async Task RealtimeEvents_DuringRefresh_ReadTheQueueAgainAfterThePendingResponse()
    {
        var pending = new TaskCompletionSource<OperateSectionResult<ApprovalInboxSnapshot>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var latest = OperateSectionResult<ApprovalInboxSnapshot>.Allowed(
            FakeProposalFactory.Snapshot(FakeProposalFactory.Summary("late-op", ConsoleProposalKind.Deploy)));
        var inbox = new ScriptedApprovalInboxClient
        {
            ReadOperation = _ => ++calls == 2
                ? pending.Task
                : Task.FromResult(calls > 2 ? latest
                    : OperateSectionResult<ApprovalInboxSnapshot>.Allowed(ApprovalInboxSnapshot.Empty)),
        };
        var realtime = new FakeConsoleProposalRealtimeClient();
        await using var ctx = NewContextWithInbox(inbox, realtime);
        var page = ctx.Render<ApprovalInboxPage>();
        page.WaitForAssertion(() => Assert.Equal(1, realtime.StartCount));

        var refresh = page.Find("[data-refresh-inbox]").ClickAsync(new());
        page.WaitForAssertion(() => Assert.Equal(2, calls));
        await page.InvokeAsync(() =>
        {
            for (var i = 0; i < 3; i++)
            {
                realtime.Raise(new ConsoleProposalEvent(
                    ConsoleProposalEventKind.Pending, "late-op", ConsoleProposalKind.Deploy,
                    ConsoleProposalStatus.AwaitingApproval, "agent", ConsoleProposalRisk.Low, DateTimeOffset.UtcNow));
            }
        });
        Assert.Equal(2, calls);
        pending.SetResult(OperateSectionResult<ApprovalInboxSnapshot>.Allowed(ApprovalInboxSnapshot.Empty));
        await refresh;

        page.WaitForAssertion(() => Assert.Single(page.FindAll("[data-proposal-id='late-op']")));
        Assert.Equal(5, calls);
    }
}
