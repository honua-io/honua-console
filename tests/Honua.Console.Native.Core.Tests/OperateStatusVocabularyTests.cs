using Honua.Console.Shell.Models;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Pins the shared status-vocabulary mapping table (console#293): the platform's status space —
/// proposal lifecycle, deploy/workflow lifecycle (including <c>ManualInterventionRequired</c>),
/// and any other raw server status string — all normalize onto <see cref="OperateStatus"/>'s five
/// visual buckets identically, so <c>ConsoleProposalPresentation.StatusClass</c> and
/// <c>DeployOperationPresentation.StateClass</c> (which now delegate to
/// <see cref="OperateStatus.CssClass"/>) render the same class as every other Operate surface for
/// the same word.
/// </summary>
public sealed class OperateStatusVocabularyTests
{
    [Theory]
    [InlineData(ConsoleProposalStatus.Planned, "console-state-neutral")]
    [InlineData(ConsoleProposalStatus.AwaitingApproval, "console-state-warning")]
    [InlineData(ConsoleProposalStatus.Submitted, "console-state-info")]
    [InlineData(ConsoleProposalStatus.Reconciling, "console-state-info")]
    [InlineData(ConsoleProposalStatus.Succeeded, "console-state-success")]
    [InlineData(ConsoleProposalStatus.Failed, "console-state-danger")]
    [InlineData(ConsoleProposalStatus.Rejected, "console-state-danger")]
    [InlineData(ConsoleProposalStatus.RolledBack, "console-state-warning")]
    [InlineData(ConsoleProposalStatus.Unknown, "console-state-neutral")]
    public void ProposalStatus_MapsThroughTheSharedVocabulary(ConsoleProposalStatus status, string expectedCssClass)
    {
        var mapped = ConsoleProposalPresentation.ToStatus(status);

        Assert.Equal(expectedCssClass, mapped.CssClass);
        // StatusClass is the pre-existing entry point every page still calls; it must agree.
        Assert.Equal(expectedCssClass, ConsoleProposalPresentation.StatusClass(status));
    }

    [Theory]
    [InlineData(DeployOperationLifecycle.Planned, "console-state-neutral")]
    [InlineData(DeployOperationLifecycle.AwaitingApproval, "console-state-warning")]
    [InlineData(DeployOperationLifecycle.Submitted, "console-state-info")]
    [InlineData(DeployOperationLifecycle.Reconciling, "console-state-info")]
    [InlineData(DeployOperationLifecycle.Succeeded, "console-state-success")]
    [InlineData(DeployOperationLifecycle.Failed, "console-state-danger")]
    [InlineData(DeployOperationLifecycle.RollbackRequested, "console-state-warning")]
    [InlineData(DeployOperationLifecycle.RolledBack, "console-state-warning")]
    [InlineData(DeployOperationLifecycle.ManualInterventionRequired, "console-state-danger")]
    [InlineData(DeployOperationLifecycle.Unknown, "console-state-neutral")]
    public void DeployLifecycle_MapsThroughTheSharedVocabulary(DeployOperationLifecycle lifecycle, string expectedCssClass)
    {
        var mapped = DeployOperationPresentation.ToStatus(lifecycle);

        Assert.Equal(expectedCssClass, mapped.CssClass);
        Assert.Equal(expectedCssClass, DeployOperationPresentation.StateClass(lifecycle));
    }

    [Fact]
    public void ManualInterventionRequired_RendersAsADangerState()
    {
        // Named explicitly in the issue's acceptance criteria: this deploy/workflow state must be
        // part of the single mapping table, not a bespoke one-off class.
        var status = DeployOperationPresentation.ToStatus(DeployOperationLifecycle.ManualInterventionRequired);

        Assert.Equal("manual intervention required", status.Label);
        Assert.True(status.IsFailure);
        Assert.Equal("console-state-danger", status.CssClass);
    }

    [Theory]
    [InlineData("open")]
    [InlineData("triaged")]
    [InlineData("wontfix")]
    [InlineData("some-brand-new-finding-state-the-server-just-started-sending")]
    public void UnrecognizedRawState_DefaultsToNeutral_NeverGuessesSuccessOrDanger(string rawState)
    {
        // A raw status string this table has not seen yet (e.g. a future finding state) must
        // degrade to neutral, never be guessed as success or danger.
        var status = new OperateStatus(rawState, "test");

        Assert.False(status.IsFailure);
        Assert.Equal("console-state-neutral", status.CssClass);
    }
}
