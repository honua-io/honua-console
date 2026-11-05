using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Tests;

public sealed class DeployOperationKindTests
{
    private static DeployOperationProposal WithKind(string kind) => new(
        OperationId: "op-1",
        Lifecycle: DeployOperationLifecycle.AwaitingApproval,
        RawStatus: "AwaitingApproval",
        Kind: kind,
        Priority: "normal",
        Service: kind,
        Environment: "prod",
        DesiredRevision: "rev-1",
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
        CreatedAt: DateTimeOffset.UtcNow,
        UpdatedAt: DateTimeOffset.UtcNow);

    [Theory]
    [InlineData("Deploy")]
    [InlineData("deploy")]
    [InlineData("Rollback")]
    public void DeployAndRollback_AreServerUpgrades(string kind)
    {
        var op = WithKind(kind);
        Assert.True(op.IsServerUpgrade);
        Assert.False(op.IsMetadataPromotion);
    }

    [Theory]
    [InlineData("MetadataRelease")]
    [InlineData("metadatarelease")]
    public void MetadataRelease_IsPromotion(string kind)
    {
        var op = WithKind(kind);
        Assert.True(op.IsMetadataPromotion);
        Assert.False(op.IsServerUpgrade);
    }

    [Fact]
    public void Migration_IsNeither()
    {
        var op = WithKind("Migration");
        Assert.False(op.IsServerUpgrade);
        Assert.False(op.IsMetadataPromotion);
    }
}
