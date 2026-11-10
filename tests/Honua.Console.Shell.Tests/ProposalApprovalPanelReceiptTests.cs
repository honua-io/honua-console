using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Shell.Tests;

public sealed class ProposalApprovalPanelReceiptTests
{
    [Fact]
    public void AmbiguousDecision_ReadsStatusWithoutAutomaticallyOpeningAnotherReview()
    {
        using var context = new BunitContext();
        var proposals = new ProposalClient
        {
            DecisionResponse = _ => Task.FromResult(OperateSectionResult<ConsoleProposalDetail>.Denied(
                OperateSectionStatus.Unavailable, "The decision response was lost."))
        };
        context.Services.AddSingleton<IConsoleProposalsClient>(proposals);
        context.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        var panel = context.Render<ProposalApprovalPanel>(parameters => parameters
            .Add(component => component.ProposalId, "proposal-1"));
        var originalReviews = proposals.ReviewReads;

        panel.Find("[data-proposal-approve]").Click();

        panel.WaitForAssertion(() =>
        {
            Assert.Equal(1, proposals.StatusReads);
            Assert.Equal(originalReviews, proposals.ReviewReads);
            Assert.True(panel.Find("[data-proposal-approve]").HasAttribute("disabled"));
        });
        panel.Render(parameters => parameters.Add(component => component.ReloadToken, 1));
        panel.WaitForAssertion(() =>
        {
            Assert.Equal(2, proposals.StatusReads);
            Assert.Equal(originalReviews, proposals.ReviewReads);
        });
        panel.Find("[data-proposal-refresh-review]").Click();
        panel.WaitForAssertion(() =>
        {
            Assert.Equal(originalReviews + 1, proposals.ReviewReads);
            Assert.False(panel.Find("[data-proposal-approve]").HasAttribute("disabled"));
            Assert.Equal(1, proposals.DecisionCalls);
        });
    }

    [Fact]
    public void RejectionForbidden_DoesNotDisableTheSeparateHumanApproval()
    {
        using var context = new BunitContext();
        var proposals = new ProposalClient
        {
            Rejection = OperateSectionResult<ConsoleProposalDetail>.Denied(OperateSectionStatus.Forbidden, "Reject scope denied.")
        };
        context.Services.AddSingleton<IConsoleProposalsClient>(proposals);
        context.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        var panel = context.Render<ProposalApprovalPanel>(parameters => parameters
            .Add(component => component.ProposalId, "proposal-1"));
        panel.Find("[data-proposal-reason]").Change("Do not perform this change.");

        panel.Find("[data-proposal-reject]").Click();

        panel.WaitForAssertion(() =>
        {
            Assert.True(panel.Find("[data-proposal-reject]").HasAttribute("disabled"));
            Assert.False(panel.Find("[data-proposal-approve]").HasAttribute("disabled"));
        });
        panel.Find("[data-proposal-approve]").Click();
        panel.WaitForAssertion(() => Assert.Equal(1, proposals.DecisionCalls));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeepLinkedResolvedProposalRemainsInspectableOutsidePendingQueue(bool enabled)
    {
        using var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        context.Services.AddSingleton<IConsoleProposalsClient>(new ProposalClient(ConsoleProposalStatus.Submitted));
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
        var proposals = new ProposalClient();
        context.Services.AddSingleton<IConsoleProposalsClient>(proposals);
        context.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        var panel = context.Render<ProposalApprovalPanel>(parameters => parameters
            .Add(component => component.ProposalId, "proposal-1"));

        panel.WaitForAssertion(() =>
            Assert.Equal("AwaitingApproval", panel.Find("[data-proposal-status]").GetAttribute("data-proposal-status")));

        panel.Find("[data-proposal-approve]").Click();
        Assert.Equal(new ConsoleProposalDecision("proposal-1", 7, "sealed-plan", "environment"), proposals.Decision);

        panel.WaitForAssertion(() =>
        {
            Assert.Equal("Submitted", panel.Find("[data-proposal-status]").GetAttribute("data-proposal-status"));
            var operation = panel.Find("[data-correlation-kind=\"OperationId\"]");
            Assert.Equal("operation-1", operation.GetAttribute("data-correlation-id"));
            Assert.Contains("Approved", panel.Find("[data-proposal-action-message]").TextContent, StringComparison.Ordinal);
        });
    }


    [Theory]
    [InlineData(OperateSectionStatus.Allowed)]
    [InlineData(OperateSectionStatus.Forbidden)]
    [InlineData(OperateSectionStatus.Unavailable)]
    public async Task DecisionCompletedAfterSelectionChange_PreservesCurrentProposal(OperateSectionStatus status)
    {
        using var context = new BunitContext();
        var pendingDecision = new TaskCompletionSource<OperateSectionResult<ConsoleProposalDetail>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var proposals = new ProposalClient { PendingDecision = pendingDecision };
        var actioned = new List<string>();
        context.Services.AddSingleton<IConsoleProposalsClient>(proposals);
        context.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        var panel = context.Render<ProposalApprovalPanel>(parameters => parameters
            .Add(component => component.ProposalId, "proposal-1")
            .Add(component => component.OnActioned, detail => actioned.Add(detail.ProposalId)));

        var click = panel.Find("[data-proposal-approve]").ClickAsync(new());
        panel.Render(parameters => parameters.Add(component => component.ProposalId, "proposal-2"));
        panel.WaitForAssertion(() => Assert.Equal("proposal-2",
            panel.Find("[data-proposal-id]").GetAttribute("data-proposal-id")));
        pendingDecision.SetResult(status == OperateSectionStatus.Allowed
            ? ProposalClient.Allowed(ConsoleProposalStatus.Submitted)
            : OperateSectionResult<ConsoleProposalDetail>.Denied(status, "Old proposal decision failed."));
        await click;

        Assert.Equal("proposal-2", panel.Find("[data-proposal-id]").GetAttribute("data-proposal-id"));
        Assert.Empty(actioned);
        Assert.Empty(panel.FindAll("[data-proposal-action-message]"));
        Assert.False(panel.Find("[data-proposal-approve]").HasAttribute("disabled"));
    }

    [Fact]
    public void ReviewCompletedAfterSelectionChange_DoesNotReplaceCurrentReviewOrNotifyParent()
    {
        using var context = new BunitContext();
        var oldReview = new TaskCompletionSource<OperateSectionResult<ConsoleProposalDetail>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var loaded = new List<string>();
        var proposals = new ProposalClient
        {
            ReadResponse = proposalId => proposalId == "proposal-1"
                ? oldReview.Task
                : Task.FromResult(ProposalClient.Allowed(ConsoleProposalStatus.AwaitingApproval, proposalId))
        };
        context.Services.AddSingleton<IConsoleProposalsClient>(proposals);
        context.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        var panel = context.Render<ProposalApprovalPanel>(parameters => parameters
            .Add(component => component.ProposalId, "proposal-1")
            .Add(component => component.OnDetailLoaded, detail => loaded.Add(detail.ProposalId)));

        panel.Render(parameters => parameters.Add(component => component.ProposalId, "proposal-2"));
        panel.WaitForAssertion(() => Assert.Equal("proposal-2",
            panel.Find("[data-proposal-id]").GetAttribute("data-proposal-id")));
        var completedSelectionRender = panel.RenderCount;
        oldReview.SetResult(ProposalClient.Allowed(ConsoleProposalStatus.AwaitingApproval));
        panel.WaitForState(() => panel.RenderCount > completedSelectionRender);

        Assert.Equal("proposal-2", panel.Find("[data-proposal-id]").GetAttribute("data-proposal-id"));
        Assert.DoesNotContain("proposal-1", loaded);
    }

    [Fact]
    public void SelectionChangeWhileReviewIsLoading_CannotApprovePreviousProposal()
    {
        using var context = new BunitContext();
        var pendingReview = new TaskCompletionSource<OperateSectionResult<ConsoleProposalDetail>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var proposals = new ProposalClient();
        context.Services.AddSingleton<IConsoleProposalsClient>(proposals);
        context.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        var panel = context.Render<ProposalApprovalPanel>(parameters => parameters
            .Add(component => component.ProposalId, "proposal-1"));
        proposals.ReadResponse = _ => pendingReview.Task;

        panel.Render(parameters => parameters.Add(component => component.ProposalId, "proposal-2"));

        Assert.All(panel.FindAll("[data-proposal-approve]"), button => Assert.True(button.HasAttribute("disabled")));
        Assert.Null(proposals.Decision);
        pendingReview.SetResult(ProposalClient.Allowed(ConsoleProposalStatus.AwaitingApproval, "proposal-2"));
        panel.WaitForAssertion(() => Assert.Equal("proposal-2",
            panel.Find("[data-proposal-id]").GetAttribute("data-proposal-id")));
    }

    [Fact]
    public async Task PreviousDecisionCompleted_DoesNotReleaseCurrentDecisionBusyState()
    {
        using var context = new BunitContext();
        var oldDecision = new TaskCompletionSource<OperateSectionResult<ConsoleProposalDetail>>();
        var currentDecision = new TaskCompletionSource<OperateSectionResult<ConsoleProposalDetail>>();
        var proposals = new ProposalClient
        {
            DecisionResponse = decision => decision.ProposalId == "proposal-1" ? oldDecision.Task : currentDecision.Task
        };
        context.Services.AddSingleton<IConsoleProposalsClient>(proposals);
        context.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        var panel = context.Render<ProposalApprovalPanel>(parameters => parameters
            .Add(component => component.ProposalId, "proposal-1"));
        var oldClick = panel.Find("[data-proposal-approve]").ClickAsync(new());
        panel.Render(parameters => parameters.Add(component => component.ProposalId, "proposal-2"));
        var currentClick = panel.Find("[data-proposal-approve]").ClickAsync(new());
        Assert.Equal("proposal-2", proposals.Decision!.ProposalId);

        oldDecision.SetResult(ProposalClient.Allowed(ConsoleProposalStatus.Submitted));
        await oldClick;

        Assert.True(panel.Find("[data-proposal-approve]").HasAttribute("disabled"));
        Assert.Equal("proposal-2", panel.Find("[data-proposal-id]").GetAttribute("data-proposal-id"));
        currentDecision.SetResult(ProposalClient.Allowed(ConsoleProposalStatus.Submitted, "proposal-2"));
        await currentClick;
        Assert.Equal("Submitted", panel.Find("[data-proposal-status]").GetAttribute("data-proposal-status"));
    }

    [Fact]
    public async Task ReloadedReview_IgnoresEarlierDecisionResult()
    {
        using var context = new BunitContext();
        var pending = new TaskCompletionSource<OperateSectionResult<ConsoleProposalDetail>>();
        var proposals = new ProposalClient { PendingDecision = pending };
        var actioned = new List<string>();
        context.Services.AddSingleton<IConsoleProposalsClient>(proposals);
        context.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        var panel = context.Render<ProposalApprovalPanel>(parameters => parameters
            .Add(component => component.ProposalId, "proposal-1")
            .Add(component => component.OnActioned, detail => actioned.Add(detail.ProposalId)));
        var click = panel.Find("[data-proposal-approve]").ClickAsync(new());
        panel.Render(parameters => parameters.Add(component => component.ReloadToken, 1));

        pending.SetResult(ProposalClient.Allowed(ConsoleProposalStatus.Submitted));
        await click;

        Assert.Equal("AwaitingApproval", panel.Find("[data-proposal-status]").GetAttribute("data-proposal-status"));
        Assert.Empty(actioned);
        Assert.False(panel.Find("[data-proposal-approve]").HasAttribute("disabled"));
    }

    [Fact]
    public void ReviewForAnotherProposal_CannotEnableApprovalOrNotifyParent()
    {
        using var context = new BunitContext();
        var loaded = new List<string>();
        var proposals = new ProposalClient
        {
            ReadResponse = _ => Task.FromResult(ProposalClient.Allowed(ConsoleProposalStatus.AwaitingApproval, "wrong-proposal"))
        };
        context.Services.AddSingleton<IConsoleProposalsClient>(proposals);
        context.SetRendererInfo(new RendererInfo("Server", isInteractive: true));

        var panel = context.Render<ProposalApprovalPanel>(parameters => parameters
            .Add(component => component.ProposalId, "proposal-1")
            .Add(component => component.OnDetailLoaded, detail => loaded.Add(detail.ProposalId)));

        Assert.Empty(panel.FindAll("[data-proposal-approve]"));
        Assert.Empty(loaded);
    }

    private sealed class ProposalClient(ConsoleProposalStatus initialStatus = ConsoleProposalStatus.AwaitingApproval)
        : IConsoleProposalsClient
    {
        public ConsoleProposalDecision? Decision { get; private set; }
        public int ReviewReads { get; private set; }
        public int StatusReads { get; private set; }
        public int DecisionCalls { get; private set; }
        public OperateSectionResult<ConsoleProposalDetail>? Rejection { get; init; }
        public Func<string, Task<OperateSectionResult<ConsoleProposalDetail>>>? ReadResponse { get; set; }
        public TaskCompletionSource<OperateSectionResult<ConsoleProposalDetail>>? PendingDecision { get; set; }
        public Func<ConsoleProposalDecision, Task<OperateSectionResult<ConsoleProposalDetail>>>? DecisionResponse { get; set; }

        public Task<OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>> ListAsync(
            string? status = null,
            string? kind = null,
            string? requestedBy = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>.Allowed([]));

        public Task<OperateSectionResult<ConsoleProposalDetail>> GetAsync(
            string proposalId,
            CancellationToken cancellationToken = default)
        {
            ReviewReads++;
            return ReadResponse?.Invoke(proposalId) ?? Task.FromResult(Allowed(initialStatus, proposalId));
        }

        public Task<OperateSectionResult<ConsoleProposalDetail>> GetStatusAsync(
            string proposalId, CancellationToken cancellationToken = default)
        {
            StatusReads++;
            var result = Allowed(initialStatus, proposalId);
            return Task.FromResult(result with { Value = result.Value!.AsStatusOnly() });
        }


        public Task<OperateSectionResult<ConsoleProposalDetail>> ApproveReviewedAsync(
            ConsoleProposalDecision decision, CancellationToken cancellationToken = default)
        {
            DecisionCalls++;
            Decision = decision;
            return DecisionResponse?.Invoke(decision) ?? PendingDecision?.Task
                ?? Task.FromResult(Allowed(ConsoleProposalStatus.Submitted, decision.ProposalId));
        }

        public Task<OperateSectionResult<ConsoleProposalDetail>> RejectAsync(
            string proposalId,
            string reason,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Rejection ?? OperateSectionResult<ConsoleProposalDetail>.Denied(OperateSectionStatus.Unsupported, "not used"));

        public static OperateSectionResult<ConsoleProposalDetail> Allowed(
            ConsoleProposalStatus status, string proposalId = "proposal-1") =>
            Honua.Console.TestFixtures.ReceiptProposalClient.Allowed(status, proposalId);
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
        client.Completion.SetResult(ReceiptProposalClient.Allowed(ConsoleProposalStatus.Submitted));
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
        public Task<OperateSectionResult<ConsoleProposalDetail>> ApproveReviewedAsync(ConsoleProposalDecision decision, CancellationToken cancellationToken = default)
        {
            Started = true;
            return Completion.Task;
        }
        public Task<OperateSectionResult<ConsoleProposalDetail>> GetAsync(string proposalId, CancellationToken cancellationToken = default) => _inner.GetAsync(proposalId, cancellationToken);
        public Task<OperateSectionResult<ConsoleProposalDetail>> GetStatusAsync(string proposalId, CancellationToken cancellationToken = default) => _inner.GetStatusAsync(proposalId, cancellationToken);
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
