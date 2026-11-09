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
    public async Task ReasonCallbackClearsAfterApproval()
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
    }

    [Fact]
    public async Task DisposalClearsAnUnsavedReason()
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
        var reportCount = reports.Count;
        await panel.InvokeAsync(() => panel.Instance.DisposeAsync().AsTask());
        Assert.Equal(reportCount + 1, reports.Count);
        Assert.Equal(new ConsoleAgentUnsavedSummary(), reports[^1]);
    }

    [Fact]
    public async Task DisposalDuringDecisionStillNotifiesTheInbox()
    {
        using var context = new BunitContext();
        var client = new PendingProposalClient();
        context.Services.AddSingleton<IConsoleProposalsClient>(client);
        context.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        var actions = new List<ConsoleProposalDetail>();
        var panel = context.Render<ProposalApprovalPanel>(parameters => parameters
            .Add(component => component.ProposalId, "proposal-1")
            .Add(component => component.OnActioned, detail => actions.Add(detail)));
        var approval = panel.Find("[data-proposal-approve]").ClickAsync(new());
        panel.WaitForAssertion(() => Assert.True(client.Started));
        await panel.InvokeAsync(() => panel.Instance.DisposeAsync().AsTask());
        client.Completion.SetResult(await new ReceiptProposalClient().ApproveAsync("proposal-1"));
        await approval;
        Assert.Equal(ConsoleProposalStatus.Submitted, Assert.Single(actions).Status);
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

    private sealed class PendingProposalClient : IConsoleProposalsClient
    {
        private readonly ReceiptProposalClient _inner = new();
        public bool Started { get; private set; }
        public TaskCompletionSource<OperateSectionResult<ConsoleProposalDetail>> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<OperateSectionResult<ConsoleProposalDetail>> ApproveAsync(string proposalId, CancellationToken cancellationToken = default)
        {
            Started = true;
            return Completion.Task;
        }
        public Task<OperateSectionResult<ConsoleProposalDetail>> GetAsync(string proposalId, CancellationToken cancellationToken = default) => _inner.GetAsync(proposalId, cancellationToken);
        public Task<OperateSectionResult<ConsoleProposalDetail>> RejectAsync(string proposalId, string reason, CancellationToken cancellationToken = default) => _inner.RejectAsync(proposalId, reason, cancellationToken);
        public Task<OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>> ListAsync(string? status = null, string? kind = null, string? requestedBy = null, CancellationToken cancellationToken = default) => _inner.ListAsync(status, kind, requestedBy, cancellationToken);
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
