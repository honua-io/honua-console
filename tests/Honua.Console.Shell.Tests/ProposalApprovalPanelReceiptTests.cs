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
    public async Task ARefreshStartedBeforeApprovalCannotReplaceTheDecision()
    {
        using var context = new BunitContext();
        var client = new PendingProposalClient();
        context.Services.AddSingleton<IConsoleProposalsClient>(client);
        context.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        var panel = context.Render<ProposalApprovalPanel>(parameters => parameters
            .Add(component => component.ProposalId, "proposal-1"));
        var receipts = new ReceiptProposalClient();
        var pendingDetail = await receipts.GetAsync("proposal-1");
        var refresh = new TaskCompletionSource<OperateSectionResult<ConsoleProposalDetail>>();

        client.NextRead = refresh.Task;
        panel.Render(parameters => parameters.Add(component => component.ReloadToken, 1));
        var approval = panel.Find("[data-proposal-approve]").ClickAsync(new());
        client.Completion.SetResult(await receipts.ApproveAsync("proposal-1"));
        await approval;

        await panel.InvokeAsync(() => refresh.SetResult(pendingDetail));

        Assert.Empty(panel.FindAll("[data-proposal-approve]"));
        Assert.Single(panel.FindAll("[data-proposal-action-message]"));
    }

    [Fact]
    public async Task ReopenedReviewKeepsDecisionsDisabledAndDisplaysTheRecordedOutcome()
    {
        using var context = new BunitContext();
        var client = new PendingProposalClient();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        context.Services.AddSingleton<IConsoleProposalsClient>(client);
        context.Services.AddSingleton<IConsoleApprovalInboxClient>(new EmptyInboxClient());
        context.Services.AddSingleton<IConsoleProposalRealtimeClient>(new NoOpRealtimeClient());
        context.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/inbox?proposalId=proposal-1");
        var page = context.Render<ApprovalInboxPage>();
        var approval = page.Find("[data-proposal-approve]").ClickAsync(new());
        page.WaitForAssertion(() => Assert.True(client.Started));
        Assert.True(page.FindAll("button").Single(button => button.TextContent == "Close review").HasAttribute("disabled"));
        await page.InvokeAsync(() => navigation.NavigateTo("/inbox"));
        await page.InvokeAsync(() => navigation.NavigateTo("/inbox?proposalId=proposal-1"));
        page.WaitForAssertion(() => Assert.True(page.Find("[data-proposal-approve]").HasAttribute("disabled")));

        var staleRead = new TaskCompletionSource<OperateSectionResult<ConsoleProposalDetail>>();
        client.NextRead = staleRead.Task;
        var completed = await new ReceiptProposalClient(ConsoleProposalStatus.Succeeded).GetAsync("proposal-1");
        client.Completion.SetResult(completed);
        await approval;

        page.WaitForAssertion(() =>
            Assert.Equal("Succeeded", page.Find("[data-proposal-status]").GetAttribute("data-proposal-status")));
        Assert.Empty(page.FindAll("[data-proposal-approve]"));

        var pending = await new ReceiptProposalClient().GetAsync("proposal-1");
        await page.InvokeAsync(() => staleRead.SetResult(pending));
        Assert.Equal("Succeeded", page.Find("[data-proposal-status]").GetAttribute("data-proposal-status"));
        Assert.Empty(page.FindAll("[data-proposal-approve]"));
    }

    [Fact]
    public async Task OverlappingRefreshesRenderTheFirstReadWhileTheFollowUpIsPending()
    {
        using var context = new BunitContext();
        var client = new PendingProposalClient();
        var first = new TaskCompletionSource<OperateSectionResult<ConsoleProposalDetail>>();
        var followUp = new TaskCompletionSource<OperateSectionResult<ConsoleProposalDetail>>();
        client.NextRead = first.Task;
        context.Services.AddSingleton<IConsoleProposalsClient>(client);
        context.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        var panel = context.Render<ProposalApprovalPanel>(parameters => parameters.Add(p => p.ProposalId, "proposal-1"));
        client.NextRead = followUp.Task;
        panel.Render(parameters => parameters.Add(p => p.ReloadToken, 1));

        var receipts = new ReceiptProposalClient();
        var pending = await receipts.GetAsync("proposal-1");
        await panel.InvokeAsync(() => first.SetResult(pending));
        panel.WaitForAssertion(() => Assert.Single(panel.FindAll("[data-proposal-approve]")));

        var submitted = await receipts.ApproveAsync("proposal-1");
        await panel.InvokeAsync(() => followUp.SetResult(submitted));
        panel.WaitForAssertion(() => Assert.Empty(panel.FindAll("[data-proposal-approve]")));
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

    [Fact]
    public void UnblindedProposalReviewRendersPlanHashOperationIdInitiationCveHighlightAndExecutionPayload()
    {
        using var context = new BunitContext();
        const string digest = "@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var detail = new ConsoleProposalDetail(
            ProposalId: "prop-cve-9",
            Kind: ConsoleProposalKind.Deploy,
            Status: ConsoleProposalStatus.AwaitingApproval,
            RequestedBy: "support:ticket-42",
            RequestedByAgent: "honua-support-agent",
            Summary: "Urgent CVE-2026-99999 image patch",
            Diff: [$"+ image ghcr.io/honua-io/honua-server{digest}"],
            DryRun: ["ECS task definition validated"],
            RiskLevel: ConsoleProposalRisk.High,
            BlockingReasons: [],
            Warnings: [],
            GuardrailTier: "RequiresApproval",
            ResolvedBy: null,
            ResolutionReason: null,
            ExecutionOperationId: null,
            CreatedAt: DateTimeOffset.Parse("2026-06-28T10:00:00Z"),
            UpdatedAt: DateTimeOffset.Parse("2026-06-28T10:05:00Z"),
            ResolvedAt: null)
        {
            Version = 3,
            PlanHash = "sha256:plan-hash-99",
            OperationId = "deploy.ecs.patch",
            ActionDiscriminator = "deploy.image.cve_patch",
            Initiation = "SupportAgent",
            ExecutionPayload = $$"""{"targetId":"ecs-prod","imageUri":"ghcr.io/honua-io/honua-server{{digest}}"}"""
        };
        context.Services.AddSingleton<IConsoleProposalsClient>(new SingleDetailProposalClient(detail));
        context.SetRendererInfo(new RendererInfo("Server", isInteractive: true));

        var panel = context.Render<ProposalApprovalPanel>(parameters => parameters
            .Add(component => component.ProposalId, "prop-cve-9"));

        panel.WaitForAssertion(() =>
        {
            var planHash = panel.Find("[data-proposal-plan-hash]").TextContent;
            Assert.Contains("sha256:plan-hash-99", planHash, StringComparison.Ordinal);
            Assert.Contains("(v3)", planHash, StringComparison.Ordinal);
            Assert.DoesNotContain("@detail.Version", planHash, StringComparison.Ordinal);
            Assert.Equal("deploy.ecs.patch", panel.Find("[data-proposal-operation-id]").TextContent.Trim());
            Assert.Equal("deploy.image.cve_patch", panel.Find("[data-proposal-action-discriminator]").TextContent.Trim());
            Assert.Equal("SupportAgent", panel.Find("[data-proposal-initiation]").TextContent.Trim());
            Assert.Contains("honua-support-agent", panel.Find("[data-proposal-support-initiation]").TextContent, StringComparison.Ordinal);
            Assert.Contains("CVE-2026-99999", panel.Find("[data-proposal-cve-patch]").TextContent, StringComparison.Ordinal);
            Assert.Contains(digest, panel.Find("[data-proposal-cve-patch]").TextContent, StringComparison.Ordinal);
            Assert.Contains("ecs-prod", panel.Find("[data-proposal-execution-payload]").TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task RuleBackedProposalWaitsForPolicyBeforeAllowingPromotion()
    {
        using var context = new BunitContext();
        var (panel, autonomy, policyRead, approval) = RenderRuleBackedProposal(context);
        panel.WaitForAssertion(() =>
        {
            Assert.Equal("Unavailable", panel.Find("[data-proposal-autonomy-mode]").TextContent.Trim());
            Assert.True(panel.Find("[data-proposal-auto-apply-action]").HasAttribute("disabled"));
            Assert.Empty(panel.FindAll("[data-proposal-auto-apply-toggle]"));
        });

        await panel.InvokeAsync(() => policyRead.SetResult());
        panel.WaitForAssertion(() =>
        {
            Assert.Equal("ProposeOnly", panel.Find("[data-proposal-autonomy-mode]").TextContent.Trim());
            Assert.NotNull(panel.Find("[data-proposal-auto-apply-toggle]"));
        });

        panel.Find("[data-proposal-auto-apply-toggle]").Change(true);
        var policyRefresh = new TaskCompletionSource();
        autonomy.PolicyRead = policyRefresh.Task;
        panel.Render(parameters => parameters.Add(component => component.ReloadToken, 1));
        panel.WaitForAssertion(() =>
        {
            Assert.True(panel.Find("[data-proposal-auto-apply-action]").HasAttribute("disabled"));
            Assert.True(panel.Find("[data-proposal-auto-apply-toggle]").HasAttribute("checked"));
            Assert.True(panel.Find("[data-proposal-approve]").HasAttribute("disabled"));
        });
        await panel.InvokeAsync(() => policyRefresh.SetResult());
        panel.WaitForAssertion(() => Assert.False(panel.Find("[data-proposal-approve]").HasAttribute("disabled")));

        var approve = panel.Find("[data-proposal-approve]").ClickAsync(new());
        await panel.InvokeAsync(() => approval.SetResult());
        await approve;

        AssertPromotionReceipt(panel, autonomy);
    }

    public enum ApprovalPanelState { Mounted, Disposed }

    [Theory]
    [InlineData(ApprovalPanelState.Mounted)]
    [InlineData(ApprovalPanelState.Disposed)]
    public async Task SwitchingEnvironmentDuringApprovalKeepsTheRequestedPolicyOnTheOriginalEnvironment(
        ApprovalPanelState panelState)
    {
        using var context = new BunitContext();
        var original = new ConsoleEnvironmentProfile { Id = "original", ServerBaseUri = new("https://original.example") };
        var alternate = new ConsoleEnvironmentProfile { Id = "alternate", ServerBaseUri = new("https://alternate.example") };
        var profiles = new InMemoryConsoleEnvironmentProfileStore([original, alternate], activeProfileId: original.Id);
        context.Services.AddSingleton<IConsoleEnvironmentProfileStore>(profiles);
        var (panel, autonomy, policyRead, approval) = RenderRuleBackedProposal(context);
        var proposals = Assert.IsType<SingleDetailProposalClient>(context.Services.GetRequiredService<IConsoleProposalsClient>());
        await panel.InvokeAsync(() => policyRead.SetResult());
        panel.WaitForAssertion(() => panel.Find("[data-proposal-auto-apply-toggle]"));
        panel.Find("[data-proposal-auto-apply-toggle]").Change(true);
        panel.Find("[data-proposal-reason]").Input("Approved for future runs");

        var approve = panel.Find("[data-proposal-approve]").ClickAsync(new());
        panel.WaitForAssertion(() => Assert.Equal(original, proposals.ApprovalProfile));
        await panel.InvokeAsync(() => profiles.ActivateProfileAsync(alternate.Id));
        if (panelState == ApprovalPanelState.Disposed)
        {
            await panel.InvokeAsync(() => panel.Instance.DisposeAsync().AsTask());
        }
        await panel.InvokeAsync(() => approval.SetResult());
        await approve;

        Assert.Equal(("alert-dispatch-backlog", "AutoApply", "Approved for future runs"),
            Assert.Single(autonomy.PolicyUpdates));
        Assert.Equal(panelState == ApprovalPanelState.Disposed ? new[] { original } : new[] { original, original },
            autonomy.BoundPolicyReads);
        Assert.Equal(original, Assert.Single(autonomy.PolicyWriteProfiles));
        if (panelState == ApprovalPanelState.Mounted)
        {
            AssertPromotionReceipt(panel, autonomy);
        }
    }

    [Theory]
    [InlineData(ConsoleProposalStatus.Submitted)]
    [InlineData(ConsoleProposalStatus.AwaitingApproval)]
    public async Task ApprovalRechecksPolicyAndRequiresAConfirmedDecision(ConsoleProposalStatus status)
    {
        using var context = new BunitContext();
        var (panel, autonomy, policyRead, approval) = RenderRuleBackedProposal(context, status);
        await panel.InvokeAsync(() => policyRead.SetResult());
        panel.WaitForAssertion(() => Assert.NotNull(panel.Find("[data-proposal-auto-apply-toggle]")));
        panel.Find("[data-proposal-auto-apply-toggle]").Change(true);

        var approve = panel.Find("[data-proposal-approve]").ClickAsync(new());
        panel.WaitForAssertion(() => Assert.True(panel.Find("[data-proposal-approve]").HasAttribute("disabled")));
        if (status == ConsoleProposalStatus.Submitted)
        {
            autonomy.ObservePolicyMode("AutoApply");
        }

        await panel.InvokeAsync(() => approval.SetResult());
        await approve;
        Assert.Empty(autonomy.PolicyUpdates);
        var expectedMode = status == ConsoleProposalStatus.Submitted ? "AutoApply" : "ProposeOnly";
        Assert.Equal(expectedMode, panel.Find("[data-proposal-autonomy-mode]").TextContent.Trim());
    }

    private static (IRenderedComponent<ProposalApprovalPanel> Panel, FakeInboxAutonomyClient Autonomy,
        TaskCompletionSource PolicyRead, TaskCompletionSource Approval) RenderRuleBackedProposal(
        BunitContext context, ConsoleProposalStatus status = ConsoleProposalStatus.Submitted)
    {
        var detail = new ConsoleProposalDetail(
            ProposalId: "prop-rule-1",
            Kind: ConsoleProposalKind.AdminConfigChange,
            Status: ConsoleProposalStatus.AwaitingApproval,
            RequestedBy: "ops-copilot",
            RequestedByAgent: "ops-copilot",
            Summary: "Redrive dead-lettered alert deliveries",
            Diff: ["+ redrive 4 dead-lettered deliveries"],
            DryRun: ["4 deliveries eligible"],
            RiskLevel: ConsoleProposalRisk.Low,
            BlockingReasons: [],
            Warnings: [],
            GuardrailTier: "RequiresApproval",
            ResolvedBy: null,
            ResolutionReason: null,
            ExecutionOperationId: null,
            CreatedAt: DateTimeOffset.Parse("2026-06-28T10:00:00Z"),
            UpdatedAt: DateTimeOffset.Parse("2026-06-28T10:05:00Z"),
            ResolvedAt: null)
        {
            Version = 1,
            PlanHash = "sha256:rule-plan-1",
            AutonomyRule = "alert-dispatch-backlog",
            ActionDiscriminator = "alerts.redrive_dead_letters"
        };
        var policyRead = new TaskCompletionSource();
        var autonomy = new FakeInboxAutonomyClient("alert-dispatch-backlog", "ProposeOnly", policyRead.Task);
        var approval = new TaskCompletionSource();
        context.Services.AddSingleton<IConsoleProposalsClient>(new SingleDetailProposalClient(detail, approval.Task, status));
        context.Services.AddSingleton<IConsoleOpsAutonomyClient>(autonomy);
        context.SetRendererInfo(new RendererInfo("Server", isInteractive: true));

        var panel = context.Render<ProposalApprovalPanel>(parameters => parameters
            .Add(component => component.ProposalId, "prop-rule-1"));

        return (panel, autonomy, policyRead, approval);
    }

    private static void AssertPromotionReceipt(IRenderedComponent<ProposalApprovalPanel> panel, FakeInboxAutonomyClient autonomy)
    {
        panel.WaitForAssertion(() =>
        {
            var call = Assert.Single(autonomy.PolicyUpdates);
            Assert.Equal(("alert-dispatch-backlog", "AutoApply"), (call.Rule, call.Mode));
            Assert.Equal("AutoApply", panel.Find("[data-proposal-autonomy-mode]").TextContent.Trim());
            var receipt = panel.Find("[data-policy-audit-receipt]");
            Assert.Contains("ProposeOnly -> AutoApply", receipt.TextContent, StringComparison.Ordinal);
            Assert.Contains("operator.alice", receipt.TextContent, StringComparison.Ordinal);
            Assert.Contains("audit-policy-77", receipt.TextContent, StringComparison.Ordinal);
            Assert.Contains("hmac-sha256:deadbeef", receipt.TextContent, StringComparison.Ordinal);
            Assert.Contains("HMAC audit chain verified", receipt.TextContent, StringComparison.Ordinal);
        });
    }

    private sealed class SingleDetailProposalClient(ConsoleProposalDetail detail, Task? approval = null,
        ConsoleProposalStatus status = ConsoleProposalStatus.Submitted) : IConsoleProposalsClient
    {
        private ConsoleProposalDetail _current = detail;

        public ConsoleEnvironmentProfile? ApprovalProfile { get; private set; }

        public IConsoleProposalsClient ForEnvironment(ConsoleEnvironmentProfile profile)
        {
            ApprovalProfile = profile;
            return this;
        }

        public Task<OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>> ListAsync(
            string? status = null, string? kind = null, string? requestedBy = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>.Allowed([_current.ToSummary()]));

        public Task<OperateSectionResult<ConsoleProposalDetail>> GetAsync(
            string proposalId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<ConsoleProposalDetail>.Allowed(_current));

        public async Task<OperateSectionResult<ConsoleProposalDetail>> ApproveAsync(
            string proposalId, CancellationToken cancellationToken = default)
        {
            await (approval ?? Task.CompletedTask);
            _current = _current with { Status = status, ExecutionOperationId = "op-approved-1" };
            return OperateSectionResult<ConsoleProposalDetail>.Allowed(_current);
        }

        public Task<OperateSectionResult<ConsoleProposalDetail>> RejectAsync(
            string proposalId, string reason, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<ConsoleProposalDetail>.Denied(OperateSectionStatus.Unavailable, "n/a"));
    }

    private sealed class FakeInboxAutonomyClient(string rule, string initialMode, Task policyRead) : IConsoleOpsAutonomyClient
    {
        private string _mode = initialMode;
        private ConsoleEnvironmentProfile? _profile;

        public List<ConsoleEnvironmentProfile> BoundPolicyReads { get; private init; } = [];
        public List<ConsoleEnvironmentProfile?> PolicyWriteProfiles { get; private init; } = [];

        public IConsoleOpsAutonomyClient ForEnvironment(ConsoleEnvironmentProfile profile) =>
            new FakeInboxAutonomyClient(rule, _mode, PolicyRead)
            {
                _profile = profile,
                BoundPolicyReads = BoundPolicyReads,
                PolicyWriteProfiles = PolicyWriteProfiles,
                PolicyUpdates = PolicyUpdates
            };

        public void ObservePolicyMode(string mode) => _mode = mode;

        public Task PolicyRead { get; set; } = policyRead;

        public List<(string Rule, string Mode, string Reason)> PolicyUpdates { get; private init; } = [];

        public async Task<OperateSectionResult<OpsAutonomySnapshot>> LoadAsync(CancellationToken cancellationToken = default)
        {
            if (_profile is { } profile)
            {
                BoundPolicyReads.Add(profile);
            }
            await PolicyRead;
            return OperateSectionResult<OpsAutonomySnapshot>.Allowed(new OpsAutonomySnapshot(
                new Honua.Console.Contracts.OpsAutonomySettingsResponse { KillSwitchEnabled = false },
                [
                    new Honua.Console.Contracts.OpsAutonomyPolicyResponse
                    {
                        Rule = rule,
                        Mode = _mode,
                        MaxAutoActionsPerWindow = 2,
                        WindowSeconds = 3600,
                        MaxBlastRadius = 1,
                        UpdatedBy = "operator.alice",
                        UpdatedAt = DateTimeOffset.Parse("2026-06-28T10:00:00Z")
                    }
                ],
                []));
        }

        public Task<OperateSectionResult<Honua.Console.Contracts.OpsAutonomyPolicyResponse>> SetPolicyModeAsync(
            string targetRule, string mode, string reason, CancellationToken cancellationToken = default)
        {
            var previous = _mode;
            _mode = mode;
            PolicyUpdates.Add((targetRule, mode, reason));
            PolicyWriteProfiles.Add(_profile);
            return Task.FromResult(OperateSectionResult<Honua.Console.Contracts.OpsAutonomyPolicyResponse>.Allowed(
                new Honua.Console.Contracts.OpsAutonomyPolicyResponse
                {
                    Rule = targetRule,
                    Mode = mode,
                    PreviousMode = previous,
                    MaxAutoActionsPerWindow = 2,
                    WindowSeconds = 3600,
                    MaxBlastRadius = 1,
                    UpdatedBy = "operator.alice",
                    UpdatedAt = DateTimeOffset.Parse("2026-06-28T10:12:00Z"),
                    AuditEntryId = "audit-policy-77",
                    AuditEntryHash = "hmac-sha256:deadbeef",
                    AuditChainVerified = true
                }));
        }

        public Task<OperateSectionResult<Honua.Console.Contracts.OpsAutonomySettingsResponse>> SetKillSwitchAsync(
            bool enabled, string reason, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<Honua.Console.Contracts.OpsAutonomySettingsResponse>.Allowed(
                new Honua.Console.Contracts.OpsAutonomySettingsResponse { KillSwitchEnabled = enabled }));
    }

    private sealed class PendingProposalClient : IConsoleProposalsClient
    {
        private readonly ReceiptProposalClient _inner = new();
        public Task<OperateSectionResult<ConsoleProposalDetail>>? NextRead { get; set; }
        public bool Started { get; private set; }
        public TaskCompletionSource<OperateSectionResult<ConsoleProposalDetail>> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<OperateSectionResult<ConsoleProposalDetail>> ApproveAsync(string proposalId, CancellationToken cancellationToken = default)
        {
            Started = true;
            return Completion.Task;
        }
        public Task<OperateSectionResult<ConsoleProposalDetail>> GetAsync(string proposalId, CancellationToken cancellationToken = default) => NextRead ?? _inner.GetAsync(proposalId, cancellationToken);
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
