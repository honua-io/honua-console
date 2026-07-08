using Bunit;
using Microsoft.AspNetCore.Components;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free bUnit coverage for the human-in-the-loop deploy approval surface. The
/// surface binds to a live honua-server deploy-control operation (charter section 11);
/// these tests drive it through the test/demo <see cref="InMemoryConsoleDeployApprovalClient"/>
/// and a stub client to exercise the approve / rollback actions (there is no Reject action —
/// console#290 addendum item 2), the data-affecting rollback confirmation gate, the
/// ManualInterventionRequired findings-driven recovery panel, and the missing/unsupported
/// binding states without a backend.
/// </summary>
public sealed class OperateDeploymentApprovalTests
{
    [Fact]
    public void ProposalList_WhenUnsupported_RendersHonestUnsupportedSurface()
    {
        var result = OperateSectionResult<IReadOnlyList<DeployOperationProposal>>.Denied(
            OperateSectionStatus.Unsupported,
            "Deploy approvals are not configured for this Console build.");

        using var ctx = new BunitContext();
        var list = ctx.Render<OperateDeploymentProposalList>(p => p.Add(x => x.Result, result));

        Assert.Contains("not configured", list.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProposalList_WhenAwaitingApproval_RendersRowsAndRaisesSelect()
    {
        DeployOperationProposal? selected = null;
        var proposals = new[] { Awaiting("op-1"), Awaiting("op-2") };
        var result = OperateSectionResult<IReadOnlyList<DeployOperationProposal>>.Allowed(proposals);

        using var ctx = new BunitContext();
        var list = ctx.Render<OperateDeploymentProposalList>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.OnSelect, EventCallback.Factory.Create<DeployOperationProposal>(this, p2 => selected = p2)));

        Assert.Equal(2, list.FindAll(".operate-deploy-proposal-row").Count);
        Assert.Contains("awaiting approval", list.Markup, StringComparison.Ordinal);

        list.FindAll("button.operate-deploy-proposal-select")[1].Click();
        Assert.NotNull(selected);
        Assert.Equal("op-2", selected!.OperationId);
    }

    [Fact]
    public void ApprovalPanel_Approve_SubmitsAndReflectsSubmittedStatus()
    {
        var client = new InMemoryConsoleDeployApprovalClient([Awaiting("op-approve")]);

        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<IConsoleDeployApprovalClient>(client);

        var panel = ctx.Render<OperateDeploymentApprovalPanel>(p => p
            .Add(x => x.OperationId, "op-approve")
            .Add(x => x.PollInterval, (TimeSpan?)null));

        panel.WaitForAssertion(
            () => Assert.Contains("awaiting approval", panel.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        panel.Find("button.console-button").Click();

        panel.WaitForAssertion(
            () =>
            {
                Assert.Contains("submitted", panel.Markup, StringComparison.Ordinal);
                Assert.Contains("Approved", panel.Markup, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ApprovalPanel_DataAffectingRollback_RequiresExplicitConfirmation()
    {
        var client = new InMemoryConsoleDeployApprovalClient([Submitted("op-rollback", dataAffecting: true)]);

        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<IConsoleDeployApprovalClient>(client);

        var panel = ctx.Render<OperateDeploymentApprovalPanel>(p => p
            .Add(x => x.OperationId, "op-rollback")
            .Add(x => x.PollInterval, (TimeSpan?)null));

        panel.WaitForAssertion(
            () => Assert.NotNull(panel.Find("button.operate-deploy-rollback-button")),
            TimeSpan.FromSeconds(5));

        // Clicking rollback must NOT immediately roll back; it must surface a confirm gate.
        panel.Find("button.operate-deploy-rollback-button").Click();
        panel.WaitForAssertion(
            () =>
            {
                Assert.Contains("data-affecting", panel.Markup, StringComparison.Ordinal);
                Assert.NotNull(panel.Find(".operate-deploy-confirm"));
            },
            TimeSpan.FromSeconds(5));

        // The operation is still not rolled back until the operator confirms.
        Assert.DoesNotContain("rollback requested", panel.Markup, StringComparison.Ordinal);

        panel.Find("button.console-button-danger").Click();
        panel.WaitForAssertion(
            () => Assert.Contains("rollback requested", panel.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ApprovalPanel_NoRejectButton_StatesModelInstead()
    {
        // console#290 addendum item 2: kill the fake Reject. There is no server reject
        // endpoint, so a Reject button that recorded a local "rejection" without calling
        // anything was worse than no button — it is removed, and the model (approve in the
        // inbox; rollback is the recovery lever) is stated in prose instead.
        var client = new InMemoryConsoleDeployApprovalClient([Awaiting("op-no-reject")]);

        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<IConsoleDeployApprovalClient>(client);

        var panel = ctx.Render<OperateDeploymentApprovalPanel>(p => p
            .Add(x => x.OperationId, "op-no-reject")
            .Add(x => x.PollInterval, (TimeSpan?)null));

        panel.WaitForAssertion(
            () =>
            {
                Assert.DoesNotContain(panel.FindAll("button"), b => b.TextContent.Contains("Reject", StringComparison.Ordinal));
                Assert.NotNull(panel.Find("[data-no-reject-note]"));
                Assert.Contains("approval inbox", panel.Markup, StringComparison.OrdinalIgnoreCase);
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ApprovalPanel_ManualInterventionWithoutFindingsClient_RendersRollbackOnly_NoRecoveryPanel()
    {
        // IConsoleOpsFindingsClient is resolved OPTIONALLY; a host that has not wired it up
        // (this test's DI container) must degrade by skipping the recovery panel, never throw.
        var client = new InMemoryConsoleDeployApprovalClient([ManualIntervention("op-mir")]);

        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<IConsoleDeployApprovalClient>(client);

        var panel = ctx.Render<OperateDeploymentApprovalPanel>(p => p
            .Add(x => x.OperationId, "op-mir")
            .Add(x => x.PollInterval, (TimeSpan?)null));

        panel.WaitForAssertion(
            () =>
            {
                Assert.Contains("manual intervention required", panel.Markup, StringComparison.Ordinal);
                Assert.Empty(panel.FindAll("[data-manual-intervention-panel]"));
                // Rollback stays offered — the recovery lever the "no Reject" note points to.
                Assert.NotNull(panel.Find("button.operate-deploy-rollback-button"));
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ApprovalPanel_ManualInterventionWithRecordedFinding_ProposesRecovery_AndShowsProposalChip()
    {
        // console#290 AC4: surfaces the EXISTING findings recovery (forward-deploy to the
        // recorded prior revision), proposed through the same findings/propose flow Copilot
        // Findings already uses — never rebuilt.
        var client = new InMemoryConsoleDeployApprovalClient([ManualIntervention("op-mir-recoverable")]);
        var findingsClient = new StubOpsFindingsClient("op-mir-recoverable");

        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<IConsoleDeployApprovalClient>(client);
        ctx.Services.AddSingleton<IConsoleOpsFindingsClient>(findingsClient);

        var panel = ctx.Render<OperateDeploymentApprovalPanel>(p => p
            .Add(x => x.OperationId, "op-mir-recoverable")
            .Add(x => x.PollInterval, (TimeSpan?)null));

        panel.WaitForAssertion(
            () => Assert.NotNull(panel.Find("[data-propose-recovery]")),
            TimeSpan.FromSeconds(5));

        panel.Find("[data-propose-recovery]").Click();

        panel.WaitForAssertion(
            () =>
            {
                Assert.NotNull(panel.Find("[data-recovery-proposal]"));
                Assert.Contains("prop-forward-1", panel.Markup, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ApprovalPanel_ManualInterventionWithNoRecordedFinding_RendersHonestSupersedeGuidance()
    {
        var client = new InMemoryConsoleDeployApprovalClient([ManualIntervention("op-mir-no-recovery")]);

        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<IConsoleDeployApprovalClient>(client);
        ctx.Services.AddSingleton<IConsoleOpsFindingsClient>(new StubOpsFindingsClient(null));

        var panel = ctx.Render<OperateDeploymentApprovalPanel>(p => p
            .Add(x => x.OperationId, "op-mir-no-recovery")
            .Add(x => x.PollInterval, (TimeSpan?)null));

        panel.WaitForAssertion(
            () =>
            {
                Assert.NotNull(panel.Find("[data-no-recovery-finding]"));
                Assert.Empty(panel.FindAll("[data-propose-recovery]"));
                Assert.Contains("Supersede", panel.Markup, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ApprovalPanel_WhenOperationMissing_RendersMissingBindingSurface()
    {
        var client = new InMemoryConsoleDeployApprovalClient();

        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<IConsoleDeployApprovalClient>(client);

        var panel = ctx.Render<OperateDeploymentApprovalPanel>(p => p
            .Add(x => x.OperationId, "does-not-exist")
            .Add(x => x.PollInterval, (TimeSpan?)null));

        panel.WaitForAssertion(
            () => Assert.Contains("is known to this in-memory client", panel.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
    }

    private static DeployOperationProposal Awaiting(string operationId) => new(
        OperationId: operationId,
        Lifecycle: DeployOperationLifecycle.AwaitingApproval,
        RawStatus: "AwaitingApproval",
        Kind: "gitops-deploy",
        Priority: "normal",
        Service: "roads-api",
        Environment: "prod",
        DesiredRevision: "rev-77",
        CurrentRevision: "rev-70",
        Action: "deploy",
        ChangeSummary: "Promote roads-api to prod @ rev-77.",
        RequestedBy: "honua-devops",
        Reason: "promotion",
        PrUrl: "https://git.example/pr/77",
        CommitSha: "abc123",
        Evidence: [new DeployOperationEvidenceLink("deploy-plan", "plan:77", "https://server.example/plan/77")],
        RollbackPlan: null,
        Warnings: [],
        BlockingReasons: [],
        CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
        UpdatedAt: DateTimeOffset.UtcNow.AddMinutes(-1));

    private static DeployOperationProposal Submitted(string operationId, bool dataAffecting) => Awaiting(operationId) with
    {
        Lifecycle = DeployOperationLifecycle.Submitted,
        RawStatus = "Submitted",
        RollbackPlan = new DeployOperationRollbackSummary(
            GitOpsRollbackClassification.SnapshotRequired,
            IsDataAffecting: dataAffecting,
            RequiresExplicitApproval: dataAffecting,
            Steps: ["restore snapshot"],
            EvidenceRequired: ["backup-id"],
            ApprovalPolicyRef: "policy/rollback-prod"),
    };

    private static DeployOperationProposal ManualIntervention(string operationId) => Awaiting(operationId) with
    {
        Lifecycle = DeployOperationLifecycle.ManualInterventionRequired,
        RawStatus = "ManualInterventionRequired",
    };

    /// <summary>
    /// Minimal stub for the recovery-panel tests: reports one finding pinned to
    /// <paramref name="recoverableOperationId"/> (or none, when null — the honest
    /// "no recorded prior revision" case) with a recommended forward-deploy action, and
    /// records propose calls as creating a gateway proposal (never executing directly).
    /// </summary>
    private sealed class StubOpsFindingsClient(string? recoverableOperationId) : IConsoleOpsFindingsClient
    {
        public Task<OperateSectionResult<Honua.Console.Contracts.OpsFindingsListResponse>> ListAsync(
            CancellationToken cancellationToken = default)
        {
            var findings = recoverableOperationId is null
                ? new List<Honua.Console.Contracts.OpsFindingResponse>()
                : new List<Honua.Console.Contracts.OpsFindingResponse>
                {
                    new()
                    {
                        Id = "finding-forward-1",
                        Rule = "deploy-manual-intervention-recovery",
                        Severity = "Warning",
                        Title = "Manual intervention recorded a recoverable prior revision",
                        Subject = new Honua.Console.Contracts.OpsFindingSubjectResponse { OperationId = recoverableOperationId },
                        RecommendedAction = new Honua.Console.Contracts.OpsFindingActionResponse
                        {
                            Kind = "Deploy",
                            Summary = "Forward-deploy to the last known-good revision.",
                            Reason = "Recorded prior revision from the workflow operation.",
                        },
                    },
                };

            return Task.FromResult(OperateSectionResult<Honua.Console.Contracts.OpsFindingsListResponse>.Allowed(
                new Honua.Console.Contracts.OpsFindingsListResponse { Findings = findings }));
        }

        public Task<OperateSectionResult<Honua.Console.Contracts.OpsFindingProposeResponse>> ProposeAsync(
            string findingId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<Honua.Console.Contracts.OpsFindingProposeResponse>.Allowed(
                new Honua.Console.Contracts.OpsFindingProposeResponse
                {
                    FindingId = findingId,
                    Status = "ProposalCreated",
                    ProposalId = "prop-forward-1",
                }));
    }
}
