using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;

namespace Honua.Console.Native.Core.Tests;

public sealed class GitOpsReleaseModelTests
{
    [Fact]
    public void ReleasesPageDeclaresQueueAndDetailRoutes()
    {
        var routes = typeof(OperateReleasesPage)
            .GetCustomAttributes(typeof(RouteAttribute), inherit: false)
            .Cast<RouteAttribute>()
            .Select(attribute => attribute.Template)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("/operate/releases", routes);
        Assert.Contains("/operate/releases/{SelectedReleaseId}", routes);
    }

    [Fact]
    public void ReleasesQueueRouteIsRecognizedAsAnOperateRoute()
    {
        Assert.True(ConsoleRouteMap.IsOperateRoute(GitOpsReleaseRoutes.Releases));
        Assert.True(ConsoleRouteMap.IsOperateRoute(GitOpsReleaseRoutes.ReleaseDetail("rel-001")));
    }

    [Fact]
    public void ReleaseDetailRouteEscapesThePackageId()
    {
        Assert.Equal("/operate/releases/rel%2F001", GitOpsReleaseRoutes.ReleaseDetail("rel/001"));
    }

    [Fact]
    public void ProposalWithBlockingFindingsCannotProposePullRequest()
    {
        var blocked = BuildProposal(hasBlockingFindings: true);
        var ready = BuildProposal(hasBlockingFindings: false);

        Assert.False(blocked.CanProposePullRequest);
        Assert.True(ready.CanProposePullRequest);
    }

    [Theory]
    [InlineData(GitOpsRollbackClassification.MetadataOnly, "metadata-only")]
    [InlineData(GitOpsRollbackClassification.ServiceRevision, "service-revision")]
    [InlineData(GitOpsRollbackClassification.ScriptReversible, "script-reversible")]
    [InlineData(GitOpsRollbackClassification.SnapshotRequired, "snapshot-required")]
    [InlineData(GitOpsRollbackClassification.Manual, "manual")]
    [InlineData(GitOpsRollbackClassification.Unknown, "unknown")]
    public void RollbackClassificationRendersDesignVocabulary(
        GitOpsRollbackClassification classification,
        string expectedLabel)
    {
        Assert.Equal(expectedLabel, GitOpsReleasePresentation.Label(classification));
        Assert.False(string.IsNullOrWhiteSpace(GitOpsReleasePresentation.Description(classification)));
    }

    [Theory]
    [InlineData(GitOpsChangeClass.FieldContract, "field contract")]
    [InlineData(GitOpsChangeClass.SchemaContract, "schema contract")]
    [InlineData(GitOpsChangeClass.ServiceConfig, "service config")]
    [InlineData(GitOpsChangeClass.AppPackage, "app package")]
    public void ChangeClassRendersDesignVocabulary(GitOpsChangeClass changeClass, string expectedLabel)
    {
        Assert.Equal(expectedLabel, GitOpsReleasePresentation.Label(changeClass));
    }

    [Fact]
    public void EnvironmentMatrixCellAlignmentTracksDriftState()
    {
        var aligned = new GitOpsEnvironmentMatrixCell("prod", GitOpsTargetBindingState.Bound, 42, 42);
        var behind = new GitOpsEnvironmentMatrixCell("prod", GitOpsTargetBindingState.Bound, 42, 40);
        var missing = new GitOpsEnvironmentMatrixCell("prod", GitOpsTargetBindingState.Missing, 42, null);
        var unavailable = new GitOpsEnvironmentMatrixCell("prod", GitOpsTargetBindingState.EnvironmentUnavailable, 42, null);

        Assert.True(aligned.IsAligned);
        Assert.Equal("aligned", aligned.DriftLabel);
        Assert.False(behind.IsAligned);
        Assert.Equal("behind", behind.DriftLabel);
        Assert.False(missing.IsAligned);
        Assert.Equal("not yet bound", missing.DriftLabel);
        Assert.Equal("environment unavailable", unavailable.DriftLabel);
    }

    [Fact]
    public void DataAffectingRollbackWithoutStepsOrEvidenceIsNotReady()
    {
        var unsafePlan = new GitOpsRollbackPlan(
            GitOpsRollbackClassification.SnapshotRequired,
            IsDataAffecting: true,
            RequiresExplicitApproval: true,
            Steps: [],
            EvidenceRequired: [],
            ApprovalPolicyRef: null);
        var readyPlan = unsafePlan with
        {
            Steps = ["restore snapshot"],
            EvidenceRequired = ["backup-id"]
        };
        var metadataOnly = new GitOpsRollbackPlan(
            GitOpsRollbackClassification.MetadataOnly,
            IsDataAffecting: false,
            RequiresExplicitApproval: false,
            Steps: [],
            EvidenceRequired: [],
            ApprovalPolicyRef: null);
        var unknown = metadataOnly with { Classification = GitOpsRollbackClassification.Unknown };

        Assert.False(unsafePlan.IsRollbackReady);
        Assert.True(readyPlan.IsRollbackReady);
        Assert.True(metadataOnly.IsRollbackReady);
        Assert.False(unknown.IsRollbackReady);
    }

    [Fact]
    public void DetailGatesPullRequestOnBothProposalFindingsAndOperationBlockers()
    {
        var readyProposal = BuildProposal(hasBlockingFindings: false);
        var matrix = new[] { new GitOpsEnvironmentMatrixCell("prod", GitOpsTargetBindingState.Bound, 42, 40) };

        var blockedByOperation = new GitOpsReleaseDetail(
            readyProposal,
            matrix,
            BuildOperation(blockers: ["smoke test failed"]),
            OperateSectionStatus.Allowed,
            string.Empty);
        var clean = blockedByOperation with { Operation = BuildOperation(blockers: []) };
        var blockedByProposal = clean with { Proposal = BuildProposal(hasBlockingFindings: true) };

        Assert.False(blockedByOperation.CanProposePullRequest);
        Assert.True(clean.CanProposePullRequest);
        Assert.False(blockedByProposal.CanProposePullRequest);
        // The matrix has a behind target, so drift is surfaced.
        Assert.True(clean.HasDrift);
    }

    [Theory]
    [InlineData(GitOpsTimelineStatus.Succeeded, "succeeded", "console-state-success")]
    [InlineData(GitOpsTimelineStatus.Failed, "failed", "console-state-danger")]
    [InlineData(GitOpsTimelineStatus.RolledBack, "rolled back", "console-state-warning")]
    [InlineData(GitOpsTimelineStatus.Running, "running", "console-state-info")]
    [InlineData(GitOpsTimelineStatus.Pending, "pending", "console-state-neutral")]
    public void TimelineStatusRendersLabelAndStateClass(
        GitOpsTimelineStatus status,
        string expectedLabel,
        string expectedClass)
    {
        Assert.Equal(expectedLabel, GitOpsReleasePresentation.Label(status));
        Assert.Equal(expectedClass, GitOpsReleasePresentation.StateClass(status));
    }

    private static GitOpsReleaseOperation BuildOperation(IReadOnlyList<string> blockers) => new(
        OperationId: "op-1",
        Status: "running",
        CurrentStage: "ci",
        PrUrl: "https://git.example/pr/1",
        CommitSha: "abc123",
        GitOperationId: "git-1",
        DeployOperationId: null,
        TargetEnvironment: "prod",
        DesiredRevision: "rev-42",
        Timeline: [new GitOpsTimelineStep("CI/GitOps", GitOpsTimelineStatus.Running, "ci")],
        Blockers: blockers,
        Warnings: [],
        RollbackPlan: null);

    private static GitOpsReleaseProposal BuildProposal(bool hasBlockingFindings) => new(
        ReleasePackageId: "rel-001",
        Title: "Promote parcels layer",
        Summary: "Field contract change tested in staging.",
        SourceEnvironmentId: "staging",
        TargetEnvironmentIds: ["prod"],
        DesiredRevision: "rev-42",
        ChangedResources:
        [
            new GitOpsChangedResource(
                "layer:parcels",
                "layer",
                "Parcels",
                [GitOpsChangeClass.FieldContract]),
        ],
        RollbackClassification: GitOpsRollbackClassification.MetadataOnly,
        HasBlockingFindings: hasBlockingFindings);
}
