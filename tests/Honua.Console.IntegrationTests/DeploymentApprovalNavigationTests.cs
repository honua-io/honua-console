using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class DeploymentApprovalNavigationTests
{
    [Fact]
    public async Task ChangingOperationHidesPriorProposalWhileReadIsPending()
    {
        using var ctx = new BunitContext();
        var client = new DelayedApprovalClient();
        var panel = Render(ctx, client);
        var pending = NewCompletion<OperateSectionResult<DeployOperationProposal>>();
        client.Read = _ => pending.Task;

        panel.Render(p => p.Add(x => x.OperationId, "next"));

        Assert.Empty(panel.FindAll(".operate-deploy-approval-panel"));
        Assert.Contains("Loading", panel.Markup);
        pending.SetResult(Allowed("next"));
        await panel.InvokeAsync(() => Task.CompletedTask);
        panel.WaitForAssertion(() => Assert.Equal("next", panel.Find("h3").TextContent.Trim()));
    }

    [Fact]
    public async Task RetiredOperationReadCannotReplaceCurrentProposal()
    {
        using var ctx = new BunitContext();
        var client = new DelayedApprovalClient();
        var first = NewCompletion<OperateSectionResult<DeployOperationProposal>>();
        client.Read = _ => first.Task;
        var panel = Render(ctx, client);
        client.Read = id => Task.FromResult(Allowed(id));
        panel.Render(p => p.Add(x => x.OperationId, "next"));
        var renders = panel.RenderCount;

        first.SetResult(Allowed("first"));
        panel.WaitForState(() => panel.RenderCount > renders);
        await panel.InvokeAsync(() => Task.CompletedTask);

        Assert.Equal("next", panel.Find("h3").TextContent.Trim());
        Assert.DoesNotContain("Loading", panel.Markup);
    }

    [Fact]
    public async Task DisposalDuringStatusCallbackStopsActionNotification()
    {
        using var ctx = new BunitContext();
        var client = new DelayedApprovalClient();
        var action = NewCompletion<OperateSectionResult<DeployOperationProposal>>();
        client.Rollback = _ => action.Task;
        var panel = Render(ctx, client);
        var status = NewCompletion<object?>();
        var entered = NewCompletion<object?>();
        var notified = 0;
        panel.Render(p => p.Add(x => x.OnStatusChanged, _ =>
        {
            entered.TrySetResult(null);
            return status.Task;
        }).Add(x => x.OnActioned, _ => notified++));
        var pending = Rollback(panel);
        action.SetResult(Allowed("first"));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await panel.InvokeAsync(() => panel.Instance.Dispose());

        status.SetResult(null);
        await pending;

        Assert.Equal(0, notified);
    }

    [Fact]
    public void ChangingOperationClearsReasonAndRollbackConfirmation()
    {
        using var ctx = new BunitContext();
        var panel = Render(ctx, new DelayedApprovalClient());
        panel.Find("textarea").Change("Reason for the first operation");
        panel.Find(".operate-deploy-rollback-button").Click();
        Assert.Single(panel.FindAll(".operate-deploy-confirm"));

        panel.Render(p => p.Add(x => x.OperationId, "next"));

        Assert.Empty(panel.Find("textarea").TextContent);
        Assert.Empty(panel.FindAll(".operate-deploy-confirm"));
    }

    [Fact]
    public async Task RetiredRollbackCannotUnlockCurrentRollback()
    {
        using var ctx = new BunitContext();
        var client = new DelayedApprovalClient();
        var first = NewCompletion<OperateSectionResult<DeployOperationProposal>>();
        var next = NewCompletion<OperateSectionResult<DeployOperationProposal>>();
        client.Rollback = id => id == "first" ? first.Task : next.Task;
        var panel = Render(ctx, client);
        var firstAction = Rollback(panel);
        panel.Render(p => p.Add(x => x.OperationId, "next"));
        Assert.False(panel.Find(".operate-deploy-rollback-button").HasAttribute("disabled"));
        var nextAction = Rollback(panel);

        first.SetResult(Allowed("first"));
        await firstAction;

        Assert.True(panel.Find("textarea").HasAttribute("disabled"));
        Assert.Empty(panel.FindAll(".operate-deploy-action-message"));
        next.SetResult(Allowed("next"));
        await nextAction;
        Assert.False(panel.Find("textarea").HasAttribute("disabled"));
        Assert.Contains("Rollback requested", panel.Markup);
    }

    [Fact]
    public async Task RetiredReviewCannotUnlockCurrentReview()
    {
        using var ctx = new BunitContext();
        var client = new DelayedApprovalClient { Lifecycle = DeployOperationLifecycle.AwaitingApproval };
        var first = NewCompletion<OperateSectionResult<ConsoleDeployApprovalReview>>();
        var next = NewCompletion<OperateSectionResult<ConsoleDeployApprovalReview>>();
        client.Review = id => id == "first" ? first.Task : next.Task;
        var panel = Render(ctx, client);
        var firstAction = panel.Find("button.console-button").ClickAsync(new());
        panel.Render(p => p.Add(x => x.OperationId, "next"));
        Assert.False(panel.Find("button.console-button").HasAttribute("disabled"));
        var nextAction = panel.Find("button.console-button").ClickAsync(new());

        first.SetResult(OperateSectionResult<ConsoleDeployApprovalReview>.Denied(OperateSectionStatus.Unavailable, "old refusal"));
        await firstAction;

        Assert.True(panel.Find("textarea").HasAttribute("disabled"));
        Assert.DoesNotContain("old refusal", panel.Markup);
        next.SetResult(OperateSectionResult<ConsoleDeployApprovalReview>.Denied(OperateSectionStatus.Forbidden, "current refusal"));
        await nextAction;
        Assert.Contains("current refusal", panel.Markup);
        Assert.False(panel.Find("textarea").HasAttribute("disabled"));
    }

    [Fact]
    public async Task RetiredFindingReadCannotReplaceCurrentRecoveryEvidence()
    {
        using var ctx = new BunitContext();
        var findings = new DelayedFindingsClient();
        var oldRead = NewCompletion<OperateSectionResult<OpsFindingsListResponse>>();
        findings.Read = () => oldRead.Task;
        ctx.Services.AddSingleton<IConsoleOpsFindingsClient>(findings);
        var panel = Render(ctx, new DelayedApprovalClient { Lifecycle = DeployOperationLifecycle.ManualInterventionRequired });
        findings.Read = () => Task.FromResult(Findings("next"));
        panel.Render(p => p.Add(x => x.OperationId, "next"));
        Assert.Contains("Recovery for next", panel.Markup);

        var renders = panel.RenderCount;
        oldRead.SetResult(Findings("first"));
        panel.WaitForState(() => panel.RenderCount > renders);
        await panel.InvokeAsync(() => Task.CompletedTask);

        Assert.Contains("Recovery for next", panel.Markup);
        Assert.Empty(panel.FindAll("[data-no-recovery-finding]"));
    }

    [Fact]
    public async Task RetiredRecoveryCannotReplaceOutcomeOrUnlockCurrentRecovery()
    {
        using var ctx = new BunitContext();
        var findings = new DelayedFindingsClient();
        var first = NewCompletion<OperateSectionResult<OpsFindingProposeResponse>>();
        var next = NewCompletion<OperateSectionResult<OpsFindingProposeResponse>>();
        findings.Propose = id => id == "first" ? first.Task : next.Task;
        ctx.Services.AddSingleton<IConsoleOpsFindingsClient>(findings);
        var panel = Render(ctx, new DelayedApprovalClient { Lifecycle = DeployOperationLifecycle.ManualInterventionRequired });
        var firstAction = panel.Find("[data-propose-recovery]").ClickAsync(new());
        panel.Render(p => p.Add(x => x.OperationId, "next"));
        Assert.False(panel.Find("[data-propose-recovery]").HasAttribute("disabled"));
        var nextAction = panel.Find("[data-propose-recovery]").ClickAsync(new());

        first.SetResult(Proposed("first"));
        await firstAction;

        Assert.True(panel.Find("[data-propose-recovery]").HasAttribute("disabled"));
        Assert.Empty(panel.FindAll("[data-recovery-proposal]"));
        next.SetResult(Proposed("next"));
        await nextAction;
        Assert.Contains("proposal-next", panel.Markup);
        Assert.DoesNotContain("proposal-first", panel.Markup);
    }

    [Fact]
    public async Task NavigationFromStatusCallbackStopsRetiredActionNotification()
    {
        using var ctx = new BunitContext();
        var client = new DelayedApprovalClient();
        var action = NewCompletion<OperateSectionResult<DeployOperationProposal>>();
        client.Rollback = _ => action.Task;
        var panel = Render(ctx, client);
        var status = NewCompletion<object?>();
        var entered = NewCompletion<object?>();
        var notified = 0;
        panel.Render(p => p.Add(x => x.OnStatusChanged, _ =>
        {
            entered.TrySetResult(null);
            return status.Task;
        }).Add(x => x.OnActioned, _ => notified++));
        var pending = Rollback(panel);
        action.SetResult(Allowed("first"));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        panel.Render(p => p.Add(x => x.OperationId, "next"));

        status.SetResult(null);
        await pending;

        Assert.Equal(0, notified);
        Assert.Empty(panel.FindAll(".operate-deploy-action-message"));
        Assert.Equal("next", panel.Find("h3").TextContent.Trim());
    }

    private static IRenderedComponent<OperateDeploymentApprovalPanel> Render(BunitContext ctx, DelayedApprovalClient client)
    {
        ctx.JSInterop.SetupModule(ConsoleOverlayFocusTrap.ModulePath).Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IConsoleDeployApprovalClient>(client);
        return ctx.Render<OperateDeploymentApprovalPanel>(p => p.Add(x => x.OperationId, "first")
            .Add(x => x.PollInterval, (TimeSpan?)null));
    }

    private static Task Rollback(IRenderedComponent<OperateDeploymentApprovalPanel> panel)
    {
        panel.Find(".operate-deploy-rollback-button").Click();
        return panel.Find(".console-button-danger").ClickAsync(new());
    }

    private static TaskCompletionSource<T> NewCompletion<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static OperateSectionResult<DeployOperationProposal> Allowed(string id) =>
        OperateSectionResult<DeployOperationProposal>.Allowed(Operation(id));

    private static DeployOperationProposal Operation(string id) => new(id, DeployOperationLifecycle.Submitted,
        "Submitted", "Deploy", "Normal", "roads", "production", "revision-2", "revision-1", "Promote",
        "Promote roads", "operator", null, null, null, [], null, [], [], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static OperateSectionResult<OpsFindingsListResponse> Findings(string id) =>
        OperateSectionResult<OpsFindingsListResponse>.Allowed(new()
        {
            Findings = [new() { Id = id, Subject = new() { OperationId = id },
                RecommendedAction = new() { Summary = $"Recovery for {id}" } }],
        });

    private static OperateSectionResult<OpsFindingProposeResponse> Proposed(string id) =>
        OperateSectionResult<OpsFindingProposeResponse>.Allowed(new() { FindingId = id, ProposalId = $"proposal-{id}" });

    private sealed class DelayedFindingsClient : IConsoleOpsFindingsClient
    {
        public Func<Task<OperateSectionResult<OpsFindingsListResponse>>>? Read { get; set; }
        public Func<string, Task<OperateSectionResult<OpsFindingProposeResponse>>>? Propose { get; set; }
        public Task<OperateSectionResult<OpsFindingsListResponse>> ListAsync(CancellationToken cancellationToken = default) =>
            Read?.Invoke() ?? Task.FromResult(OperateSectionResult<OpsFindingsListResponse>.Allowed(new()
            { Findings = [.. Findings("first").Value!.Findings!, .. Findings("next").Value!.Findings!] }));
        public Task<OperateSectionResult<OpsFindingProposeResponse>> ProposeAsync(string findingId, CancellationToken cancellationToken = default) =>
            Propose?.Invoke(findingId) ?? Task.FromResult(Proposed(findingId));
    }

    private sealed class DelayedApprovalClient : IConsoleDeployApprovalClient
    {
        public DeployOperationLifecycle Lifecycle { get; init; } = DeployOperationLifecycle.Submitted;
        public Func<string, Task<OperateSectionResult<DeployOperationProposal>>>? Read { get; set; }
        public Func<string, Task<OperateSectionResult<DeployOperationProposal>>>? Rollback { get; set; }
        public Func<string, Task<OperateSectionResult<ConsoleDeployApprovalReview>>>? Review { get; set; }
        public Task<OperateSectionResult<DeployOperationProposal>> GetOperationAsync(string operationId, CancellationToken cancellationToken = default) =>
            Read?.Invoke(operationId) ?? Task.FromResult(OperateSectionResult<DeployOperationProposal>.Allowed(
                Operation(operationId) with { Lifecycle = Lifecycle, RawStatus = Lifecycle.ToString() }));
        public Task<OperateSectionResult<ConsoleDeployApprovalReview>> OpenSubmitReviewAsync(string operationId, CancellationToken cancellationToken = default) =>
            Review!.Invoke(operationId);
        public Task<OperateSectionResult<DeployOperationProposal>> RollbackAsync(string operationId, string? reason, CancellationToken cancellationToken = default) =>
            Rollback!.Invoke(operationId);
        public Task<OperateSectionResult<DeployOperationProposal>> SubmitAsync(ConsoleDeployApprovalReview review, string? reason, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Submission was not requested by this scenario.");
        public Task<OperateSectionResult<IReadOnlyList<DeployOperationProposal>>> ListPendingAsync(IReadOnlyList<string> operationIds, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Listing was not requested by this scenario.");
    }
}
