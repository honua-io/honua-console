using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Host-independent coverage for the server-bound temporal client
/// (<see cref="HonuaServerTemporalCapabilityClient"/>). Covers the deserialized-DTO null-safety contract
/// (System.Text.Json overrides a collection's <c>[]</c> initializer with null when the server emits an
/// explicit JSON <c>null</c> for the key, so reads must coalesce before LINQ rather than throwing and
/// tearing down the Blazor circuit) AND the live disconnected-sync conflict review/resolution binding
/// (honua-server#1167 slice 2): the conflict list + per-conflict base/client/server detail bind, a resolve
/// write that commits a new server state, and the honest not-available/forbidden mapping on failure
/// (Console Patterns Charter section 11 — bind the real server, never fabricate conflicts or outcomes).
/// </summary>
public sealed class HonuaServerTemporalCapabilityClientTests
{
    private const string ServiceId = "svc-parcels";
    private const string SourceId = "svc-parcels/0";
    private const string ReplicaId = "replica-1";

    private static HonuaServerTemporalCapabilityClient CreateClient(IHonuaTemporalClient client) =>
        new(client, new HonuaServerTemporalOptions([new TemporalSourceCandidate(ServiceId, 0)]));

    [Fact]
    public async Task GetReplicaConflictQueue_ServerOmitsReplicas_ReturnsEmptyBoundQueueWithoutThrowing()
    {
        // The server returns a success body whose "replicas" key is an explicit JSON null; STJ leaves the
        // non-null-typed Replicas array null. The read must not throw.
        var fake = new FakeTemporalClient
        {
            ListReplicasResult = HonuaAdminEndpointResult<HonuaReplicaManagementListResponse>.FromData(
                new HonuaReplicaManagementListResponse { Replicas = null! }),
        };

        var queue = await CreateClient(fake).GetReplicaConflictQueueAsync(SourceId);

        Assert.Empty(queue.Replicas);
        // A successful read is bound (no binding-state issue), distinguishing it from the missing/unsupported
        // paths — the empty result reflects a real server payload, not a fabricated or unbound state.
        Assert.Null(queue.BindingState);
        Assert.Equal(ServiceId, fake.LastListReplicasServiceId);
    }

    [Fact]
    public async Task GetReplicaConflictQueue_UnconfiguredSource_ReportsMissingBinding_WithoutCallingServer()
    {
        var fake = new FakeTemporalClient
        {
            ListReplicasResult = HonuaAdminEndpointResult<HonuaReplicaManagementListResponse>.FromData(
                new HonuaReplicaManagementListResponse()),
        };

        var queue = await CreateClient(fake).GetReplicaConflictQueueAsync("not-configured/0");

        Assert.Empty(queue.Replicas);
        Assert.NotNull(queue.BindingState);
        Assert.Null(fake.LastListReplicasServiceId);
    }

    [Fact]
    public async Task GetReplicaConflictReview_BindsConflictListAndPerConflictDetail()
    {
        var fake = new FakeTemporalClient
        {
            ListReplicasResult = OwningReplicaList(),
            ListConflictsResult = HonuaAdminEndpointResult<HonuaReplicaConflictListResponse>.FromData(
                new HonuaReplicaConflictListResponse
                {
                    ServiceId = ServiceId,
                    ReplicaId = ReplicaId,
                    Conflicts =
                    [
                        new HonuaReplicaConflictSummary
                        {
                            ConflictId = "conflict-1",
                            ReplicaId = ReplicaId,
                            ServiceId = ServiceId,
                            LayerId = 0,
                            ObjectId = 101,
                            ConflictType = "geometry",
                            Status = "pending",
                        },
                    ],
                }),
            GetConflictResult = HonuaAdminEndpointResult<HonuaReplicaConflictDetail>.FromData(
                new HonuaReplicaConflictDetail
                {
                    ConflictId = "conflict-1",
                    ReplicaId = ReplicaId,
                    ServiceId = ServiceId,
                    LayerId = 0,
                    ObjectId = 101,
                    ConflictType = "geometry",
                    Status = "pending",
                    ServerGeneration = 42,
                    BaseState = JsonElementOf("""{"attributes":{"owner":"Acme"}}"""),
                    ClientState = JsonElementOf("""{"attributes":{"owner":"Acme LLC"}}"""),
                    ServerState = JsonElementOf("""{"attributes":{"owner":"Acme Inc"}}"""),
                }),
        };

        var review = await CreateClient(fake).GetReplicaConflictReviewAsync(ReplicaId);

        Assert.Null(review.BindingState);
        Assert.NotNull(review.Replica);
        Assert.Equal(ReplicaId, review.Replica!.ReplicaId);
        Assert.Equal(SourceId, review.Replica.SourceId);
        // The review path supplies the real pending count from the live conflict list.
        Assert.Equal(1, review.Replica.PendingConflictCount);

        var conflict = Assert.Single(review.Conflicts);
        Assert.Equal("conflict-1", conflict.ConflictId);
        Assert.Equal("101", conflict.FeatureId);
        Assert.Equal(SyncConflictType.Geometry, conflict.ConflictType);
        Assert.True(conflict.GeometryConflict);
        // The three-way comparison surfaces the diverging "owner" field with base/client/server values.
        var field = Assert.Single(conflict.FieldConflicts);
        Assert.Equal("owner", field.Field);
        Assert.Equal("Acme", field.BaseValue);
        Assert.Equal("Acme LLC", field.ClientValue);
        Assert.Equal("Acme Inc", field.ServerValue);

        Assert.Equal((ServiceId, ReplicaId, "pending"), fake.LastListConflictsCall);
        Assert.Equal((ServiceId, ReplicaId, "conflict-1"), fake.LastGetConflictCall);
    }

    [Fact]
    public async Task GetReplicaConflictReview_UnownedReplica_ReportsNotConfigured_WithoutFabricating()
    {
        var fake = new FakeTemporalClient
        {
            // The configured service owns a different replica; the requested one is not found anywhere.
            ListReplicasResult = OwningReplicaList(),
        };

        var review = await CreateClient(fake).GetReplicaConflictReviewAsync("replica-unknown");

        Assert.NotNull(review.BindingState);
        Assert.Equal("Not configured", review.BindingState!.State);
        Assert.Null(review.Replica);
        Assert.Empty(review.Conflicts);
        // It scanned the registry but never tried to list conflicts for an unresolved replica.
        Assert.Null(fake.LastListConflictsCall);
    }

    [Fact]
    public async Task ResolveConflicts_AcceptClient_PostsResolutionAndReturnsCommittedServerState()
    {
        var fake = new FakeTemporalClient
        {
            ListReplicasResult = OwningReplicaList(),
            // The owner resolution scans the replica's conflict list for the conflict id.
            ListConflictsResult = ConflictListContaining("conflict-1"),
            ResolveResult = HonuaAdminEndpointResult<HonuaReplicaConflictResolutionResponse>.FromData(
                new HonuaReplicaConflictResolutionResponse
                {
                    CommittedNewServerState = true,
                    Conflict = new HonuaReplicaConflictDetail
                    {
                        ConflictId = "conflict-1",
                        ReplicaId = ReplicaId,
                        ServiceId = ServiceId,
                        Status = "resolved",
                        ResolutionAction = "acceptClient",
                        ResolvedServerGeneration = 43,
                    },
                }),
        };

        var result = await CreateClient(fake).ResolveConflictsAsync(
            new SyncConflictResolutionRequest(["conflict-1"], SyncResolutionAction.AcceptClient));

        Assert.Null(result.BindingState);
        // AC #3: a committed resolution carries a change set + at least one audit event.
        Assert.True(result.WroteAuditedChangeSet);
        Assert.Equal("gen-43", result.ResultChangeSetId);
        var audit = Assert.Single(result.AuditEventIds);
        Assert.Equal("replica.conflict.resolve.acceptClient:conflict-1", audit);
        // The POST was routed to the resolved service+replica+conflict with the mapped server action.
        Assert.Equal((ServiceId, ReplicaId, "conflict-1", "acceptClient"), fake.LastResolveCall);
    }

    [Fact]
    public async Task ResolveConflicts_KeepServer_DoesNotClaimCommittedServerState()
    {
        var fake = new FakeTemporalClient
        {
            ListReplicasResult = OwningReplicaList(),
            ListConflictsResult = ConflictListContaining("conflict-1"),
            ResolveResult = HonuaAdminEndpointResult<HonuaReplicaConflictResolutionResponse>.FromData(
                new HonuaReplicaConflictResolutionResponse
                {
                    CommittedNewServerState = false,
                    Conflict = new HonuaReplicaConflictDetail
                    {
                        ConflictId = "conflict-1",
                        ReplicaId = ReplicaId,
                        ServiceId = ServiceId,
                        Status = "resolved",
                        ResolutionAction = "keepServer",
                    },
                }),
        };

        var result = await CreateClient(fake).ResolveConflictsAsync(
            new SyncConflictResolutionRequest(["conflict-1"], SyncResolutionAction.KeepServer));

        Assert.Null(result.BindingState);
        // Keep-server produces no new committed server state; the result must not fabricate a change set.
        Assert.False(result.WroteAuditedChangeSet);
        Assert.Null(result.ResultChangeSetId);
        Assert.Equal("keepServer", fake.LastResolveCall.Action);
    }

    [Fact]
    public async Task ResolveConflicts_RestoreBase_IsRejectedAsUnsupported_WithoutPosting()
    {
        // RestoreBase has no server-side resolution-action equivalent; it must be reported honestly rather
        // than silently coerced to another action.
        var fake = new FakeTemporalClient { ListReplicasResult = OwningReplicaList() };

        var result = await CreateClient(fake).ResolveConflictsAsync(
            new SyncConflictResolutionRequest(["conflict-1"], SyncResolutionAction.RestoreBase));

        Assert.NotNull(result.BindingState);
        Assert.Equal("Unsupported", result.BindingState!.State);
        Assert.Null(fake.LastResolveCall.Action);
    }

    [Fact]
    public async Task GetReplicaConflictReview_Server501_SurfacesUnsupportedBinding_NotFabricatedConflicts()
    {
        var fake = new FakeTemporalClient
        {
            ListReplicasResult = OwningReplicaList(),
            // A read-only provider reports conflict review unsupported (501 -> Unsupported issue).
            ListConflictsResult = HonuaAdminEndpointResult<HonuaReplicaConflictListResponse>.FromIssue(
                new HonuaAdminEndpointIssue("Unsupported", "contract", "not supported", 501)),
        };

        var review = await CreateClient(fake).GetReplicaConflictReviewAsync(ReplicaId);

        Assert.NotNull(review.BindingState);
        Assert.Equal("Unsupported", review.BindingState!.State);
        Assert.Empty(review.Conflicts);
    }

    private static HonuaAdminEndpointResult<HonuaReplicaManagementListResponse> OwningReplicaList() =>
        HonuaAdminEndpointResult<HonuaReplicaManagementListResponse>.FromData(
            new HonuaReplicaManagementListResponse
            {
                ServiceId = ServiceId,
                Replicas =
                [
                    new HonuaReplicaManagementSummary
                    {
                        ReplicaId = ReplicaId,
                        ReplicaName = "Field Crew 7",
                        ServiceId = ServiceId,
                        SyncModel = "perReplica",
                        LayerIds = [0],
                    },
                ],
            });

    private static HonuaAdminEndpointResult<HonuaReplicaConflictListResponse> ConflictListContaining(string conflictId) =>
        HonuaAdminEndpointResult<HonuaReplicaConflictListResponse>.FromData(
            new HonuaReplicaConflictListResponse
            {
                ServiceId = ServiceId,
                ReplicaId = ReplicaId,
                Conflicts =
                [
                    new HonuaReplicaConflictSummary
                    {
                        ConflictId = conflictId,
                        ReplicaId = ReplicaId,
                        ServiceId = ServiceId,
                        LayerId = 0,
                        ObjectId = 101,
                        ConflictType = "attribute",
                        Status = "pending",
                    },
                ],
            });

    private static JsonElement JsonElementOf(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class FakeTemporalClient : IHonuaTemporalClient
    {
        public HonuaAdminEndpointResult<HonuaReplicaManagementListResponse> ListReplicasResult { get; set; } =
            HonuaAdminEndpointResult<HonuaReplicaManagementListResponse>.FromData(new HonuaReplicaManagementListResponse());

        public HonuaAdminEndpointResult<HonuaReplicaConflictListResponse>? ListConflictsResult { get; set; }
        public HonuaAdminEndpointResult<HonuaReplicaConflictDetail>? GetConflictResult { get; set; }
        public HonuaAdminEndpointResult<HonuaReplicaConflictResolutionResponse>? ResolveResult { get; set; }

        public string? LastListReplicasServiceId { get; private set; }
        public (string ServiceId, string ReplicaId, string? Status)? LastListConflictsCall { get; private set; }
        public (string ServiceId, string ReplicaId, string ConflictId)? LastGetConflictCall { get; private set; }
        public (string? ServiceId, string? ReplicaId, string? ConflictId, string? Action) LastResolveCall { get; private set; }

        public Uri BaseUri { get; } = new("https://temporal.test/");

        public Task<HonuaAdminEndpointResult<HonuaReplicaManagementListResponse>> ListReplicasAsync(
            string serviceId,
            CancellationToken cancellationToken = default)
        {
            LastListReplicasServiceId = serviceId;
            return Task.FromResult(ListReplicasResult);
        }

        public Task<HonuaAdminEndpointResult<HonuaReplicaConflictListResponse>> ListReplicaConflictsAsync(
            string serviceId, string replicaId, string? status, CancellationToken cancellationToken = default)
        {
            LastListConflictsCall = (serviceId, replicaId, status);
            return Task.FromResult(
                ListConflictsResult
                ?? HonuaAdminEndpointResult<HonuaReplicaConflictListResponse>.FromData(
                    new HonuaReplicaConflictListResponse { ServiceId = serviceId, ReplicaId = replicaId, Conflicts = [] }));
        }

        public Task<HonuaAdminEndpointResult<HonuaReplicaConflictDetail>> GetReplicaConflictAsync(
            string serviceId, string replicaId, string conflictId, CancellationToken cancellationToken = default)
        {
            LastGetConflictCall = (serviceId, replicaId, conflictId);
            return Task.FromResult(
                GetConflictResult ?? throw new NotSupportedException("GetConflictResult not configured."));
        }

        public Task<HonuaAdminEndpointResult<HonuaReplicaConflictResolutionResponse>> ResolveReplicaConflictAsync(
            string serviceId, string replicaId, string conflictId, HonuaReplicaConflictResolutionRequest request,
            CancellationToken cancellationToken = default)
        {
            LastResolveCall = (serviceId, replicaId, conflictId, request.Action);
            return Task.FromResult(
                ResolveResult ?? throw new NotSupportedException("ResolveResult not configured."));
        }

        public Task<HonuaAdminEndpointResult<HonuaTemporalCapabilityResponse>> GetCapabilityAsync(
            string serviceId, int layerId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<HonuaAdminEndpointResult<HonuaTemporalAsOfResponse>> ReadAsOfAsync(
            string serviceId, int layerId, long? generation, string? timestamp, int? limit,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<HonuaAdminEndpointResult<HonuaReplicaManagementDetail>> GetReplicaAsync(
            string serviceId, string replicaId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
