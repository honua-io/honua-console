using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

public sealed class OperateTemporalLiveServerTests
{
    private const string ContractBlockedSkip =
        "Blocked on honua-server#1166 (temporal data history API: as-of/diff/attribution/rollback) and "
        + "honua-server#1167 (disconnected replica conflict review API + named replica metadata). No server "
        + "image exposes the /temporal/* contract yet, so the live temporal viewer + conflict review "
        + "assertions stay blocked rather than running against a mock (Console Patterns Charter section 11).";

    [SkippableFact]
    public void TemporalViewer_RendersTemporalHistory_FromLiveServer()
    {
        // Stays blocked on honua-server#1166: as-of query, diff, attribution, and feature timeline.
        Skip.If(true, ContractBlockedSkip);
    }

    [SkippableFact]
    public void TemporalViewer_RendersReplicaConflictReview_FromLiveServer()
    {
        // Stays blocked on honua-server#1167: named replica metadata + conflict reads/resolution writes.
        Skip.If(true, ContractBlockedSkip);
    }

    [Fact]
    public async Task MergedBuild_TemporalClient_NeverFabricatesData_UntilContractsLand()
    {
        var client = new UnsupportedTemporalCapabilityClient();

        var workspace = await client.GetWorkspaceAsync();
        var checkpoints = await client.GetCheckpointsAsync("src");
        var diff = await client.GetDiffAsync("src", "a", "b");
        var queue = await client.GetReplicaConflictQueueAsync("src");
        var resolution = await client.ResolveConflictsAsync(
            new SyncConflictResolutionRequest(["c-1"], SyncResolutionAction.KeepServer));

        Assert.Empty(workspace.Sources);
        Assert.Empty(checkpoints.Checkpoints);
        Assert.True(checkpoints.BindingState!.IsMissingBinding);
        Assert.True(diff.BindingState!.IsMissingBinding);
        Assert.Empty(diff.SampleFeatureChanges);
        Assert.Empty(queue.Replicas);
        Assert.True(queue.BindingState!.IsMissingBinding);
        // AC #3: a blocked resolution writes no audited change set.
        Assert.False(resolution.WroteAuditedChangeSet);
    }
}
