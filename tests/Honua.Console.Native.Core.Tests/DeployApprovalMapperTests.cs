using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

public sealed class DeployApprovalMapperTests
{
    [Theory]
    [InlineData("Planned", DeployOperationLifecycle.Planned)]
    [InlineData("AwaitingApproval", DeployOperationLifecycle.AwaitingApproval)]
    [InlineData("awaiting-approval", DeployOperationLifecycle.AwaitingApproval)]
    [InlineData("Submitted", DeployOperationLifecycle.Submitted)]
    [InlineData("Reconciling", DeployOperationLifecycle.Reconciling)]
    [InlineData("Succeeded", DeployOperationLifecycle.Succeeded)]
    [InlineData("Failed", DeployOperationLifecycle.Failed)]
    [InlineData("RolledBack", DeployOperationLifecycle.RolledBack)]
    [InlineData("rolled-back", DeployOperationLifecycle.RolledBack)]
    [InlineData("ManualInterventionRequired", DeployOperationLifecycle.ManualInterventionRequired)]
    [InlineData("something-new", DeployOperationLifecycle.Unknown)]
    [InlineData("", DeployOperationLifecycle.Unknown)]
    public void MapLifecycle_MapsServerStatusOneToOne(string raw, DeployOperationLifecycle expected)
    {
        Assert.Equal(expected, DeployOperationPresentation.MapLifecycle(raw));
    }

    [Fact]
    public void MapProposal_ProjectsCoreFieldsAndRollbackPlan()
    {
        var response = new DeployOperationResponse
        {
            OperationId = "op-77",
            Kind = "gitops-deploy",
            Status = "AwaitingApproval",
            Priority = "high",
            RequestedBy = "honua-devops",
            Reason = "Promote parcels to prod",
            Warnings = ["smoke window is short"],
            BlockingReasons = [],
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch.AddMinutes(5),
            MetadataRelease = new MetadataReleaseContextResponse
            {
                PackageId = "pkg-1",
                PrUrl = "https://git.example/pr/77",
                CommitSha = "abc123",
                DesiredRevision = "rev-77",
                TargetEnvironment = "prod",
                EvidenceRefs =
                [
                    new MetadataEvidenceRefResponse { Kind = "deploy-plan", RefId = "plan:77", Uri = "https://server.example/plan/77" },
                ],
                RollbackPlan = new MetadataRollbackPlanResponse
                {
                    Class = "snapshot-required",
                    IsDataAffecting = true,
                    RequiresExplicitApproval = false,
                    Steps = ["restore snapshot"],
                    EvidenceRequired = ["backup-id"],
                    ApprovalPolicyRef = "policy/rollback-prod",
                },
            },
        };

        var proposal = DeployApprovalMapper.MapProposal(response);

        Assert.Equal("op-77", proposal.OperationId);
        Assert.Equal(DeployOperationLifecycle.AwaitingApproval, proposal.Lifecycle);
        Assert.True(proposal.IsAwaitingApproval);
        Assert.Equal("prod", proposal.Environment);
        Assert.Equal("rev-77", proposal.DesiredRevision);
        Assert.Equal("https://git.example/pr/77", proposal.PrUrl);
        Assert.Single(proposal.Evidence);
        Assert.Equal("deploy-plan", proposal.Evidence[0].Kind);

        Assert.NotNull(proposal.RollbackPlan);
        Assert.Equal(GitOpsRollbackClassification.SnapshotRequired, proposal.RollbackPlan!.Classification);
        Assert.True(proposal.RollbackPlan.IsDataAffecting);
        // Data-affecting always requires confirmation even when the server field says false.
        Assert.True(proposal.RollbackPlan.RequiresConfirmation);
    }

    [Fact]
    public void MapProposal_WhenNoMetadataRelease_DegradesToUnknownWithoutThrowing()
    {
        var response = new DeployOperationResponse
        {
            OperationId = "op-bare",
            Kind = "gitops-deploy",
            Status = "Planned",
        };

        var proposal = DeployApprovalMapper.MapProposal(response);

        Assert.Equal("unknown", proposal.Environment);
        Assert.Equal("unknown", proposal.DesiredRevision);
        Assert.Null(proposal.RollbackPlan);
        Assert.Empty(proposal.Evidence);
        Assert.False(proposal.IsAwaitingApproval);
    }

    [Fact]
    public void Proposal_RollbackEligibility_TracksLifecycle()
    {
        Assert.True(SampleWith(DeployOperationLifecycle.Submitted).IsRollbackEligible);
        Assert.True(SampleWith(DeployOperationLifecycle.Succeeded).IsRollbackEligible);
        Assert.False(SampleWith(DeployOperationLifecycle.AwaitingApproval).IsRollbackEligible);
        Assert.False(SampleWith(DeployOperationLifecycle.RolledBack).IsRollbackEligible);
        Assert.True(SampleWith(DeployOperationLifecycle.Succeeded).IsTerminal);
        Assert.False(SampleWith(DeployOperationLifecycle.Reconciling).IsTerminal);
    }

    private static DeployOperationProposal SampleWith(DeployOperationLifecycle lifecycle) => new(
        OperationId: "op",
        Lifecycle: lifecycle,
        RawStatus: lifecycle.ToString(),
        Kind: "gitops-deploy",
        Priority: "normal",
        Service: "svc",
        Environment: "prod",
        DesiredRevision: "rev",
        CurrentRevision: null,
        Action: "deploy",
        ChangeSummary: "summary",
        RequestedBy: null,
        Reason: null,
        PrUrl: null,
        CommitSha: null,
        Evidence: [],
        RollbackPlan: null,
        Warnings: [],
        BlockingReasons: [],
        CreatedAt: DateTimeOffset.UnixEpoch,
        UpdatedAt: DateTimeOffset.UnixEpoch);
}
