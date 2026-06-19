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
/// and a stub client to exercise the approve / reject / rollback actions, the data-affecting
/// rollback confirmation gate, and the missing/unsupported binding states without a backend.
/// </summary>
public sealed class OperateDeploymentApprovalTests
{
    [Fact]
    public void ProposalList_WhenUnsupported_RendersHonestUnsupportedSurface()
    {
        var result = OperateSectionResult<IReadOnlyList<DeployOperationProposal>>.Denied(
            OperateSectionStatus.Unsupported,
            "Deploy approvals are not configured for this Console build.");

        using var ctx = new Bunit.TestContext();
        var list = ctx.Render<OperateDeploymentProposalList>(p => p.Add(x => x.Result, result));

        Assert.Contains("not configured", list.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProposalList_WhenAwaitingApproval_RendersRowsAndRaisesSelect()
    {
        DeployOperationProposal? selected = null;
        var proposals = new[] { Awaiting("op-1"), Awaiting("op-2") };
        var result = OperateSectionResult<IReadOnlyList<DeployOperationProposal>>.Allowed(proposals);

        using var ctx = new Bunit.TestContext();
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

        using var ctx = new Bunit.TestContext();
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

        using var ctx = new Bunit.TestContext();
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
    public void ApprovalPanel_Reject_DoesNotSubmit_AndRecordsIntent()
    {
        var client = new InMemoryConsoleDeployApprovalClient([Awaiting("op-reject")]);

        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IConsoleDeployApprovalClient>(client);

        var panel = ctx.Render<OperateDeploymentApprovalPanel>(p => p
            .Add(x => x.OperationId, "op-reject")
            .Add(x => x.PollInterval, (TimeSpan?)null));

        panel.WaitForAssertion(
            () => Assert.NotNull(panel.FindAll("button.console-button-secondary").FirstOrDefault(b => b.TextContent.Contains("Reject", StringComparison.Ordinal))),
            TimeSpan.FromSeconds(5));

        var rejectButton = panel.FindAll("button.console-button-secondary")
            .First(b => b.TextContent.Contains("Reject", StringComparison.Ordinal));
        rejectButton.Click();

        panel.WaitForAssertion(
            () => Assert.Contains("Rejection recorded", panel.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // Reject never advances the operation; the status badge stays AwaitingApproval.
        var badge = panel.Find(".operate-deploy-approval-panel .console-panel-heading .console-status");
        Assert.Equal("awaiting approval", badge.TextContent.Trim());
    }

    [Fact]
    public void ApprovalPanel_WhenOperationMissing_RendersMissingBindingSurface()
    {
        var client = new InMemoryConsoleDeployApprovalClient();

        using var ctx = new Bunit.TestContext();
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
}
