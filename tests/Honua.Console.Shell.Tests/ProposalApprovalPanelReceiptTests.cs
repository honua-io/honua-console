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

    [Fact]
    public async Task ApprovalSkipsPolicyUpdateWhenRefreshAlreadyShowsAutoApply()
    {
        using var context = new BunitContext();
        var (panel, autonomy, policyRead, approval) = RenderRuleBackedProposal(context);
        await panel.InvokeAsync(() => policyRead.SetResult());
        panel.WaitForAssertion(() => Assert.NotNull(panel.Find("[data-proposal-auto-apply-toggle]")));
        panel.Find("[data-proposal-auto-apply-toggle]").Change(true);

        var approve = panel.Find("[data-proposal-approve]").ClickAsync(new());
        panel.WaitForAssertion(() => Assert.True(panel.Find("[data-proposal-approve]").HasAttribute("disabled")));
        autonomy.ObservePolicyMode("AutoApply");
        panel.Render(parameters => parameters.Add(component => component.ReloadToken, 1));
        panel.WaitForAssertion(() =>
            Assert.Equal("AutoApply", panel.Find("[data-proposal-autonomy-mode]").TextContent.Trim()));

        await panel.InvokeAsync(() => approval.SetResult());
        await approve;
        Assert.Empty(autonomy.PolicyUpdates);
    }

    private static (IRenderedComponent<ProposalApprovalPanel> Panel, FakeInboxAutonomyClient Autonomy,
        TaskCompletionSource PolicyRead, TaskCompletionSource Approval) RenderRuleBackedProposal(BunitContext context)
    {
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RuleBackedProposalWaitsForPolicyBeforeAllowingPromotion(bool enabledDuringApproval)
    {
        using var context = new BunitContext();
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
        context.Services.AddSingleton<IConsoleProposalsClient>(new SingleDetailProposalClient(detail, approval.Task));
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

    private sealed class SingleDetailProposalClient(ConsoleProposalDetail detail, Task? approval = null) : IConsoleProposalsClient
    {
        private ConsoleProposalDetail _current = detail;

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
            _current = _current with { Status = ConsoleProposalStatus.Submitted, ExecutionOperationId = "op-approved-1" };
            return OperateSectionResult<ConsoleProposalDetail>.Allowed(_current);
        }

        public Task<OperateSectionResult<ConsoleProposalDetail>> RejectAsync(
            string proposalId, string reason, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<ConsoleProposalDetail>.Denied(OperateSectionStatus.Unavailable, "n/a"));
    }

    private sealed class FakeInboxAutonomyClient(string rule, string initialMode, Task policyRead) : IConsoleOpsAutonomyClient
    {
        private string _mode = initialMode;

        public void ObservePolicyMode(string mode) => _mode = mode;

        public Task PolicyRead { get; set; } = policyRead;

        public List<(string Rule, string Mode, string Reason)> PolicyUpdates { get; } = [];

        public async Task<OperateSectionResult<OpsAutonomySnapshot>> LoadAsync(CancellationToken cancellationToken = default)
        {
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
