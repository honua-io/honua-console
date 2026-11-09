using Bunit;
using Honua.Console.TestFixtures;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Shell.Tests;

public sealed class ProposalApprovalPanelReceiptTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeepLinkedResolvedProposalRemainsInspectableOutsidePendingQueue(bool enabled)
    {
        using var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        context.Services.AddSingleton<IConsoleProposalsClient>(new ReceiptProposalClient(ConsoleProposalStatus.Submitted));
        context.Services.AddSingleton<IConsoleApprovalInboxClient>(new EmptyInboxClient());
        context.Services.AddSingleton<IConsoleProposalRealtimeClient>(new NoOpRealtimeClient());

        context.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(navigation.GetUriWithQueryParameter("proposalId", "proposal-1"));
        var page = context.Render<ApprovalInboxPage>();

        page.WaitForAssertion(() =>
        {
            Assert.Equal("Submitted", page.Find("[data-proposal-status]").GetAttribute("data-proposal-status"));
            Assert.Equal("operation-1", page.Find("[data-correlation-kind=\"OperationId\"]")
                .GetAttribute("data-correlation-id"));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApprovalKeepsServerStatusAndExecutionOperationMachineReadable(bool enabled)
    {
        using var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        context.Services.AddSingleton<IConsoleProposalsClient>(new ReceiptProposalClient());
        context.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        var panel = context.Render<ProposalApprovalPanel>(parameters => parameters
            .Add(component => component.ProposalId, "proposal-1"));

        panel.WaitForAssertion(() =>
            Assert.Equal("AwaitingApproval", panel.Find("[data-proposal-status]").GetAttribute("data-proposal-status")));

        panel.Find("[data-proposal-approve]").Click();

        panel.WaitForAssertion(() =>
        {
            Assert.Equal("Submitted", panel.Find("[data-proposal-status]").GetAttribute("data-proposal-status"));
            var operation = panel.Find("[data-correlation-kind=\"OperationId\"]");
            Assert.Equal("operation-1", operation.GetAttribute("data-correlation-id"));
            Assert.Contains("Approved", panel.Find("[data-proposal-action-message]").TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task ReasonCallbackClearsAfterApprovalAndDisposal()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<IConsoleProposalsClient>(new ReceiptProposalClient());
        context.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        var reports = new List<ConsoleAgentUnsavedSummary>();
        var panel = context.Render<ProposalApprovalPanel>(parameters => parameters
            .Add(component => component.ProposalId, "proposal-1")
            .Add(component => component.UnsavedChanged, summary => reports.Add(summary)));

        panel.Find("[data-proposal-reason]").Input("Private reason");
        Assert.Equal(1, reports[^1].ChangedFieldCount);
        await panel.Find("[data-proposal-approve]").ClickAsync(new());
        Assert.Contains(reports, summary => summary.PendingOperationCount == 1);
        Assert.Equal(new ConsoleAgentUnsavedSummary(), reports[^1]);
        await panel.InvokeAsync(() => panel.Instance.DisposeAsync().AsTask());
        Assert.Equal(new ConsoleAgentUnsavedSummary(), reports[^1]);
    }

    [Fact]
    public async Task FailedDecisionRetainsReasonAndClearsPendingCount()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<IConsoleProposalsClient>(new ReceiptProposalClient());
        context.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        var reports = new List<ConsoleAgentUnsavedSummary>();
        var panel = context.Render<ProposalApprovalPanel>(parameters => parameters
            .Add(component => component.ProposalId, "proposal-1")
            .Add(component => component.UnsavedChanged, summary => reports.Add(summary)));

        panel.Find("[data-proposal-reason]").Input("Reason");
        await panel.Find("[data-proposal-reject]").ClickAsync(new());
        Assert.Equal(1, reports[^1].ChangedFieldCount);
        Assert.Equal(0, reports[^1].PendingOperationCount);
        panel.Find("[data-proposal-reason]").Input("");
        Assert.Equal(new ConsoleAgentUnsavedSummary(), reports[^1]);
    }

    private sealed class EmptyInboxClient : IConsoleApprovalInboxClient
    {
        public Task<OperateSectionResult<ApprovalInboxSnapshot>> GetInboxAsync(
            string? status = null,
            string? kind = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<ApprovalInboxSnapshot>.Allowed(ApprovalInboxSnapshot.Empty));
    }

    private sealed class NoOpRealtimeClient : IConsoleProposalRealtimeClient
    {
        public event Action<ConsoleProposalEvent>? ProposalChanged { add { } remove { } }
        public bool IsConnected => false;
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
