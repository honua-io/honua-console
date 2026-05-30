using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Real-server integration coverage for the temporal viewer + disconnected sync conflict review surface
/// (honua-console#43, Console Patterns Charter section 11). This suite is designed to boot a real
/// honua-server (with PostgreSQL) via Testcontainers, seed a temporal/replica fixture, and assert that the
/// viewer and conflict review render from live data — but it stays <b>blocked</b> until the server
/// contracts land. The temporal data history API (honua-server#1166) and the disconnected replica conflict
/// review API (honua-server#1167) are both still open, so no server image exposes
/// <c>/temporal/*</c> endpoints to bind against. Per the issue and the charter, this is never implemented
/// against a mock: the live facts skip with the precise contract dependency, and the merged-build behavior
/// (the unsupported client surfaces the missing binding and never fabricates data) is asserted as the
/// host-independent proof until #1166/#1167 ship.
/// </summary>
public sealed class OperateTemporalLiveServerTests
{
    /// <summary>
    /// The contract dependency that blocks the live temporal/conflict-review assertions. When
    /// honua-server#1166/#1167 land and honua-sdk-dotnet (or the Honua.Console.Contracts shim) projects the
    /// temporal/replica DTOs, replace this skip with the boot-server fixture + live-data assertions and wire
    /// the live <see cref="ITemporalCapabilityClient"/> in the DI seam.
    /// </summary>
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

    /// <summary>
    /// Until the live contracts land, the merged build must never fabricate temporal data: the only
    /// registered client surfaces the honua-server#1166/#1167 missing binding from every operation. This
    /// fact runs without Docker and is the standing proof of the section-11 no-standing-mock guarantee.
    /// </summary>
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
