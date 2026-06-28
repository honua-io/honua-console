using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Coverage for the branch-version management operation layer (honua-console#177): the live
/// <see cref="HonuaServerVersionManagementOperation"/> mapping of client results/rejections into the Operate
/// view/result models, and the <see cref="UnsupportedVersionManagementOperation"/> missing-binding behavior.
/// These drive a fake <see cref="IHonuaVersionManagementClient"/> rather than HTTP, so they pin the
/// operation-level projection (counts, can-post, post-blocked, state vocabulary) independently of transport.
/// </summary>
public sealed class VersionManagementOperationTests
{
    private const string ServiceId = "parcels";
    private const string VersionGuid = "11111111-2222-3333-4444-555555555555";

    [Fact]
    public async Task ListVersions_MapsContractToView()
    {
        var client = new FakeClient
        {
            Versions = HonuaAdminEndpointResult<IReadOnlyList<HonuaVersionInfo>>.FromData(
            [
                new HonuaVersionInfo
                {
                    VersionGuid = VersionGuid,
                    VersionName = "alex.edit",
                    Owner = "alex",
                    Access = "private",
                    Status = "active",
                    CreationMoment = 1700000000000
                }
            ])
        };
        var op = new HonuaServerVersionManagementOperation(client);

        var view = await op.ListVersionsAsync(ServiceId);

        Assert.True(view.Bound);
        var version = Assert.Single(view.Versions);
        Assert.Equal("alex.edit", version.VersionName);
        Assert.NotNull(version.Created);
    }

    [Fact]
    public async Task ListVersions_IssueBecomesUnboundWithState()
    {
        var client = new FakeClient
        {
            Versions = HonuaAdminEndpointResult<IReadOnlyList<HonuaVersionInfo>>.FromIssue(
                new HonuaAdminEndpointIssue("Unsupported", "GET versions", "not supported", 501))
        };
        var op = new HonuaServerVersionManagementOperation(client);

        var view = await op.ListVersionsAsync(ServiceId);

        Assert.False(view.Bound);
        Assert.Equal("Unsupported", view.State);
    }

    [Fact]
    public async Task Reconcile_SurfacesCountsAndCanPost()
    {
        var client = new FakeClient
        {
            Reconcile = HonuaAdminEndpointResult<HonuaReconcileResult>.FromData(new HonuaReconcileResult
            {
                Success = true,
                HasConflicts = true,
                CanPost = false,
                AutoResolvedCount = 4,
                Conflicts = [new HonuaVersionConflict { LayerId = 1, ObjectId = 2, ConflictType = "attribute" }]
            })
        };
        var op = new HonuaServerVersionManagementOperation(client);

        var view = await op.ReconcileAsync(new ReconcileVersionCommand
        {
            ServiceId = ServiceId,
            VersionGuid = VersionGuid,
            Policy = "last-write-wins"
        });

        Assert.True(view.Operation.Succeeded);
        Assert.Equal(4, view.AutoResolvedCount);
        Assert.Equal(1, view.RemainingConflictCount);
        Assert.False(view.CanPost);
        Assert.Equal(HonuaVersionReconcilePolicy.LastWriteWins, client.LastPolicy);
    }

    [Fact]
    public async Task Reconcile_ServerRejectsWithSuccessFalse_IsFailureNotReconciled()
    {
        // A 200 body with success:false (e.g. abortIfConflicts aborting because conflicts exist)
        // must NOT be presented to the operator as a successful reconcile.
        var client = new FakeClient
        {
            Reconcile = HonuaAdminEndpointResult<HonuaReconcileResult>.FromData(new HonuaReconcileResult
            {
                Success = false,
                HasConflicts = true,
                CanPost = false,
                AutoResolvedCount = 0,
                Conflicts = [new HonuaVersionConflict { LayerId = 1, ObjectId = 2, ConflictType = "attribute" }]
            })
        };
        var op = new HonuaServerVersionManagementOperation(client);

        var view = await op.ReconcileAsync(new ReconcileVersionCommand
        {
            ServiceId = ServiceId,
            VersionGuid = VersionGuid,
            Policy = "none",
            AbortIfConflicts = true
        });

        Assert.False(view.Operation.Succeeded);
        Assert.True(view.HasConflicts);
        Assert.False(view.CanPost);
        Assert.Equal(1, view.RemainingConflictCount);
    }

    [Fact]
    public async Task ResolveConflicts_MapsChoiceStrings_AndReportsRemaining()
    {
        var client = new FakeClient
        {
            Resolve = HonuaAdminEndpointResult<HonuaResolveConflictsResult>.FromData(new HonuaResolveConflictsResult
            {
                Success = true,
                Resolved = 2,
                Remaining = 1,
                CanPost = false
            })
        };
        var op = new HonuaServerVersionManagementOperation(client);

        var view = await op.ResolveConflictsAsync(ServiceId, VersionGuid,
        [
            new ConflictResolutionChoice(1, 2, "version"),
            new ConflictResolutionChoice(1, 3, "base")
        ]);

        Assert.True(view.Operation.Succeeded);
        Assert.Equal(2, view.Resolved);
        Assert.Equal(1, view.Remaining);
        Assert.False(view.CanPost);
        Assert.Equal(HonuaVersionConflictChoice.TakeVersion, client.LastResolutions![0].Choice);
        Assert.Equal(HonuaVersionConflictChoice.TakeBase, client.LastResolutions[1].Choice);
    }

    [Fact]
    public async Task Post_BlockedByConflicts_IsFailureWithBlockedState()
    {
        var client = new FakeClient
        {
            Post = HonuaAdminEndpointResult<HonuaPostResult>.FromData(new HonuaPostResult
            {
                Success = false,
                BlockedByConflicts = true
            })
        };
        var op = new HonuaServerVersionManagementOperation(client);

        var result = await op.PostAsync(ServiceId, VersionGuid);

        Assert.False(result.Succeeded);
        Assert.Equal("Blocked", result.State);
    }

    [Fact]
    public async Task Post_Success_ReportsAppliedChanges()
    {
        var client = new FakeClient
        {
            Post = HonuaAdminEndpointResult<HonuaPostResult>.FromData(new HonuaPostResult
            {
                Success = true,
                AppliedChanges = 12,
                ServerGeneration = 99
            })
        };
        var op = new HonuaServerVersionManagementOperation(client);

        var result = await op.PostAsync(ServiceId, VersionGuid);

        Assert.True(result.Succeeded);
        Assert.Equal("Posted", result.State);
        Assert.Contains("12", result.Detail!);
    }

    [Fact]
    public async Task Unsupported_ReturnsMissingBindingEverywhere()
    {
        var op = new UnsupportedVersionManagementOperation();

        Assert.False((await op.ListVersionsAsync(ServiceId)).Bound);
        Assert.Equal("Missing binding", (await op.CreateVersionAsync(new CreateVersionCommand { ServiceId = ServiceId, VersionName = "x" })).State);
        Assert.Equal("Missing binding", (await op.PostAsync(ServiceId, VersionGuid)).State);
        Assert.False((await op.InspectConflictsAsync(ServiceId, VersionGuid)).Bound);
        Assert.Equal("Missing binding", (await op.ReconcileAsync(new ReconcileVersionCommand { ServiceId = ServiceId, VersionGuid = VersionGuid })).Operation.State);
    }

    private sealed class FakeClient : IHonuaVersionManagementClient
    {
        public HonuaAdminEndpointResult<IReadOnlyList<HonuaVersionInfo>>? Versions { get; init; }
        public HonuaAdminEndpointResult<HonuaReconcileResult>? Reconcile { get; init; }
        public HonuaAdminEndpointResult<HonuaResolveConflictsResult>? Resolve { get; init; }
        public HonuaAdminEndpointResult<HonuaPostResult>? Post { get; init; }

        public HonuaVersionReconcilePolicy LastPolicy { get; private set; }
        public IReadOnlyList<HonuaVersionConflictResolution>? LastResolutions { get; private set; }

        public Task<HonuaAdminEndpointResult<IReadOnlyList<HonuaVersionInfo>>> ListVersionsAsync(
            string serviceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Versions ?? HonuaAdminEndpointResult<IReadOnlyList<HonuaVersionInfo>>.FromData([]));

        public Task<HonuaAdminEndpointResult<HonuaVersionInfo>> CreateVersionAsync(
            string serviceId, string versionName, string? owner, HonuaVersionAccess access, string? description,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(HonuaAdminEndpointResult<HonuaVersionInfo>.FromData(new HonuaVersionInfo { VersionName = versionName }));

        public Task<HonuaAdminEndpointResult<HonuaVersionMomentResult>> AlterVersionAsync(
            string serviceId, string versionGuid, string? versionName, HonuaVersionAccess? access, string? description,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(HonuaAdminEndpointResult<HonuaVersionMomentResult>.FromData(new HonuaVersionMomentResult { Success = true }));

        public Task<HonuaAdminEndpointResult<HonuaVersionMomentResult>> DeleteVersionAsync(
            string serviceId, string versionGuid, CancellationToken cancellationToken = default) =>
            Task.FromResult(HonuaAdminEndpointResult<HonuaVersionMomentResult>.FromData(new HonuaVersionMomentResult { Success = true }));

        public Task<HonuaAdminEndpointResult<HonuaReconcileResult>> ReconcileAsync(
            string serviceId, string versionGuid, HonuaVersionReconcilePolicy policy, bool abortIfConflicts,
            CancellationToken cancellationToken = default)
        {
            LastPolicy = policy;
            return Task.FromResult(Reconcile ?? HonuaAdminEndpointResult<HonuaReconcileResult>.FromData(new HonuaReconcileResult()));
        }

        public Task<HonuaAdminEndpointResult<HonuaInspectConflictsResult>> InspectConflictsAsync(
            string serviceId, string versionGuid, CancellationToken cancellationToken = default) =>
            Task.FromResult(HonuaAdminEndpointResult<HonuaInspectConflictsResult>.FromData(new HonuaInspectConflictsResult()));

        public Task<HonuaAdminEndpointResult<HonuaResolveConflictsResult>> ResolveConflictsAsync(
            string serviceId, string versionGuid, IReadOnlyList<HonuaVersionConflictResolution> resolutions,
            CancellationToken cancellationToken = default)
        {
            LastResolutions = resolutions;
            return Task.FromResult(Resolve ?? HonuaAdminEndpointResult<HonuaResolveConflictsResult>.FromData(new HonuaResolveConflictsResult()));
        }

        public Task<HonuaAdminEndpointResult<HonuaPostResult>> PostAsync(
            string serviceId, string versionGuid, CancellationToken cancellationToken = default) =>
            Task.FromResult(Post ?? HonuaAdminEndpointResult<HonuaPostResult>.FromData(new HonuaPostResult { Success = true }));
    }
}
