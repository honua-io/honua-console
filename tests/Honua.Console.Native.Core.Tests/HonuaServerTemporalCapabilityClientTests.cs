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

    [Fact]
    public async Task GetDiff_CapabilitySupportsDiff_BindsLiveDiffAndMapsSummary()
    {
        var fake = new FakeTemporalClient
        {
            CapabilityResult = CapabilityWith(diff: true),
            DiffResult = HonuaAdminEndpointResult<HonuaTemporalDiffResponse>.FromData(
                new HonuaTemporalDiffResponse
                {
                    ServiceId = ServiceId,
                    LayerId = 0,
                    From = new HonuaTemporalCheckpointResponse { Kind = "generation", Generation = 40 },
                    To = new HonuaTemporalCheckpointResponse { Kind = "generation", Generation = 42 },
                    Summary = new HonuaTemporalDiffSummaryResponse
                    {
                        Added = 2, Removed = 1, AttributeChanged = 3, GeometryChanged = 1, Total = 6,
                    },
                    Changes =
                    [
                        new HonuaTemporalFeatureDiffResponse
                        {
                            ObjectId = 101,
                            PrimaryClass = "attributeChanged",
                            Classes = ["attributeChanged"],
                            GeometryChanged = false,
                            FieldChanges =
                            [
                                new HonuaTemporalFieldChangeResponse
                                {
                                    Field = "owner",
                                    OldValue = JsonElementOf("\"Acme\""),
                                    NewValue = JsonElementOf("\"Acme Inc\""),
                                    Masked = false,
                                },
                            ],
                            Attribution = new HonuaTemporalAttributionResponse { Actor = "alice", Source = "editSession", Operation = "edit" },
                        },
                    ],
                }),
        };

        var diff = await CreateClient(fake).GetDiffAsync(SourceId, "gen-40", "gen-42");

        Assert.Null(diff.BindingState);
        Assert.Equal(2, diff.AddedFeatures);
        Assert.Equal(1, diff.RemovedFeatures);
        // Updated = total - added - removed.
        Assert.Equal(3, diff.UpdatedFeatures);
        Assert.Equal(1, diff.GeometryChangedFeatures);
        Assert.Equal(3, diff.AttributeChangedFeatures);
        var change = Assert.Single(diff.SampleFeatureChanges);
        Assert.Equal("101", change.FeatureId);
        Assert.Equal(TemporalChangeType.Updated, change.ChangeType);
        var attr = Assert.Single(change.AttributeChanges);
        Assert.Equal("owner", attr.Field);
        Assert.Equal("Acme", attr.Before);
        Assert.Equal("Acme Inc", attr.After);
        Assert.Equal("alice", change.ActorId);

        // The console "gen-N" checkpoint ids are mapped to the server's bare-generation checkpoint syntax.
        Assert.Equal((ServiceId, 0, "40", "42", (int?)null), fake.LastDiffCall);
    }

    [Fact]
    public async Task GetDiff_CapabilityDoesNotSupportDiff_ReportsUnsupported_WithoutProbingDiff()
    {
        var fake = new FakeTemporalClient { CapabilityResult = CapabilityWith(diff: false) };

        var diff = await CreateClient(fake).GetDiffAsync(SourceId, "gen-40", "gen-42");

        Assert.NotNull(diff.BindingState);
        Assert.Equal("Unsupported", diff.BindingState!.State);
        // The capability gate short-circuits before any diff probe.
        Assert.Null(fake.LastDiffCall);
    }

    [Fact]
    public async Task GetDiff_Server404_SurfacesHonestBinding_NotFabricatedDiff()
    {
        var fake = new FakeTemporalClient
        {
            CapabilityResult = CapabilityWith(diff: true),
            DiffResult = HonuaAdminEndpointResult<HonuaTemporalDiffResponse>.FromIssue(
                new HonuaAdminEndpointIssue("Unsupported", "contract", "not found", 404)),
        };

        var diff = await CreateClient(fake).GetDiffAsync(SourceId, "gen-40", "gen-42");

        Assert.NotNull(diff.BindingState);
        Assert.Equal("Unsupported", diff.BindingState!.State);
        Assert.Empty(diff.SampleFeatureChanges);
    }

    [Fact]
    public async Task GetFeatureTimeline_CapabilitySupportsTimeline_BindsRevisions()
    {
        var fake = new FakeTemporalClient
        {
            CapabilityResult = CapabilityWith(timeline: true),
            TimelineResult = HonuaAdminEndpointResult<HonuaTemporalTimelineResponse>.FromData(
                new HonuaTemporalTimelineResponse
                {
                    ServiceId = ServiceId,
                    LayerId = 0,
                    ObjectId = 101,
                    CurrentGeneration = 42,
                    Revisions =
                    [
                        new HonuaTemporalRevisionResponse
                        {
                            Generation = 40,
                            Operation = "Update",
                            ChangedAt = "2026-05-28T09:00:00.000Z",
                            Attribution = new HonuaTemporalAttributionResponse { Actor = "bob", Source = "job" },
                        },
                    ],
                }),
        };

        var timeline = await CreateClient(fake).GetFeatureTimelineAsync(SourceId, "101");

        Assert.Null(timeline.BindingState);
        Assert.Equal("101", timeline.FeatureId);
        var revision = Assert.Single(timeline.Revisions);
        Assert.Equal("gen-40", revision.RevisionId);
        Assert.Equal(TemporalRevisionOperation.Update, revision.Operation);
        Assert.Equal("bob", revision.ActorDisplayName);
        Assert.Equal((ServiceId, 0, 101L, (int?)null), fake.LastTimelineCall);
    }

    [Fact]
    public async Task GetFeatureTimeline_NonNumericFeatureId_IsRejected_WithoutProbing()
    {
        var fake = new FakeTemporalClient { CapabilityResult = CapabilityWith(timeline: true) };

        var timeline = await CreateClient(fake).GetFeatureTimelineAsync(SourceId, "not-a-number");

        Assert.NotNull(timeline.BindingState);
        Assert.Equal("Rejected", timeline.BindingState!.State);
        Assert.Null(fake.LastTimelineCall);
    }

    [Fact]
    public async Task CreateRollbackPlan_CapabilitySupportsRollback_BindsPlanAndEncodesExecutablePlanId()
    {
        var fake = new FakeTemporalClient
        {
            CapabilityResult = CapabilityWith(rollback: true),
            PlanResult = HonuaAdminEndpointResult<HonuaTemporalRollbackPlanResponse>.FromData(
                new HonuaTemporalRollbackPlanResponse
                {
                    ServiceId = ServiceId,
                    LayerId = 0,
                    TargetCheckpoint = new HonuaTemporalCheckpointResponse { Kind = "generation", Generation = 40 },
                    CurrentGeneration = 42,
                    State = "jobRequired",
                    AffectedFeatureCount = 17,
                    RequiresApproval = true,
                    ValidationFindings =
                    [
                        new HonuaTemporalRollbackFindingResponse { Code = "FK_RISK", Severity = "warning", Message = "Foreign keys may break." },
                    ],
                }),
        };

        var plan = await CreateClient(fake).CreateRollbackPlanAsync(SourceId, TemporalRollbackScope.Layer, "gen-40");

        Assert.Null(plan.BindingState);
        Assert.Equal(17, plan.AffectedFeatureCount);
        Assert.True(plan.RequiresApproval);
        Assert.True(plan.RequiresJob);
        Assert.Equal(TemporalRollbackMode.DataRevert, plan.RollbackMode);
        Assert.Equal("gen-40", plan.TargetCheckpointId);
        Assert.Equal("gen-42", plan.CurrentCheckpointId);
        var finding = Assert.Single(plan.ValidationFindings);
        Assert.Contains("Foreign keys", finding, StringComparison.Ordinal);
        // The plan id round-trips the owning source + target checkpoint so execute can route the job.
        Assert.Equal("rb|svc-parcels/0|gen-40", plan.RollbackPlanId);
        // The plan request carried the bare-generation target checkpoint.
        Assert.Equal("generation", fake.LastPlanCall!.Value.Request.Checkpoint!.Kind);
        Assert.Equal(40, fake.LastPlanCall.Value.Request.Checkpoint!.Generation);
    }

    [Fact]
    public async Task CreateRollbackPlan_CapabilityDoesNotSupportRollback_ReportsUnsupported()
    {
        var fake = new FakeTemporalClient { CapabilityResult = CapabilityWith(rollback: false) };

        var plan = await CreateClient(fake).CreateRollbackPlanAsync(SourceId, TemporalRollbackScope.Layer, "gen-40");

        Assert.NotNull(plan.BindingState);
        Assert.Equal("Unsupported", plan.BindingState!.State);
        Assert.Null(fake.LastPlanCall);
    }

    [Fact]
    public async Task ExecuteRollback_DecodesPlanId_RoutesApprovedJob_AndReturnsHandle()
    {
        var fake = new FakeTemporalClient
        {
            ExecuteResult = HonuaAdminEndpointResult<HonuaTemporalRollbackJobResponse>.FromData(
                new HonuaTemporalRollbackJobResponse
                {
                    JobId = "job-7",
                    ServiceId = ServiceId,
                    LayerId = 0,
                    TargetCheckpoint = new HonuaTemporalCheckpointResponse { Kind = "generation", Generation = 40 },
                    Status = "queued",
                }),
        };

        var operation = await CreateClient(fake).ExecuteRollbackAsync("rb|svc-parcels/0|gen-40");

        Assert.Null(operation.BindingState);
        Assert.Equal("job-7", operation.RollbackOperationId);
        Assert.Equal("job-7", operation.JobRunId);
        Assert.Equal("queued", operation.Status);
        Assert.Equal("gen-40", operation.ResultCheckpointId);
        Assert.Equal("temporal.rollback.execute:job-7", Assert.Single(operation.AuditEventIds));
        // The execute call is routed to the decoded source/layer with the approved flag set.
        Assert.Equal(ServiceId, fake.LastExecuteCall!.Value.ServiceId);
        Assert.Equal(0, fake.LastExecuteCall.Value.LayerId);
        Assert.True(fake.LastExecuteCall.Value.Request.Approved);
        Assert.Equal(40, fake.LastExecuteCall.Value.Request.Checkpoint!.Generation);
    }

    [Fact]
    public async Task ExecuteRollback_UndecodablePlanId_IsRejected_WithoutProbing()
    {
        var fake = new FakeTemporalClient();

        var operation = await CreateClient(fake).ExecuteRollbackAsync("not-a-plan-id");

        Assert.NotNull(operation.BindingState);
        Assert.Equal("Rejected", operation.BindingState!.State);
        Assert.Null(fake.LastExecuteCall);
    }

    private static HonuaAdminEndpointResult<HonuaTemporalCapabilityResponse> CapabilityWith(
        bool diff = false, bool timeline = false, bool rollback = false) =>
        HonuaAdminEndpointResult<HonuaTemporalCapabilityResponse>.FromData(
            new HonuaTemporalCapabilityResponse
            {
                ServiceId = ServiceId,
                LayerId = 0,
                SupportsHistory = true,
                SupportsAsOf = true,
                CurrentGeneration = 42,
                Deferred = new HonuaTemporalDeferredCapabilities
                {
                    SupportsDiff = diff,
                    SupportsTimeline = timeline,
                    SupportsRollback = rollback,
                },
            });

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
        public HonuaAdminEndpointResult<HonuaTemporalCapabilityResponse>? CapabilityResult { get; set; }
        public HonuaAdminEndpointResult<HonuaTemporalDiffResponse>? DiffResult { get; set; }
        public HonuaAdminEndpointResult<HonuaTemporalTimelineResponse>? TimelineResult { get; set; }
        public HonuaAdminEndpointResult<HonuaTemporalRollbackPlanResponse>? PlanResult { get; set; }
        public HonuaAdminEndpointResult<HonuaTemporalRollbackJobResponse>? ExecuteResult { get; set; }

        public string? LastListReplicasServiceId { get; private set; }
        public (string ServiceId, string ReplicaId, string? Status)? LastListConflictsCall { get; private set; }
        public (string ServiceId, string ReplicaId, string ConflictId)? LastGetConflictCall { get; private set; }
        public (string? ServiceId, string? ReplicaId, string? ConflictId, string? Action) LastResolveCall { get; private set; }
        public (string ServiceId, int LayerId, string From, string? To, int? Limit)? LastDiffCall { get; private set; }
        public (string ServiceId, int LayerId, long FeatureId, int? Limit)? LastTimelineCall { get; private set; }
        public (string ServiceId, int LayerId, HonuaTemporalRollbackPlanRequest Request)? LastPlanCall { get; private set; }
        public (string ServiceId, int LayerId, HonuaTemporalRollbackExecuteRequest Request)? LastExecuteCall { get; private set; }

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
            Task.FromResult(
                CapabilityResult ?? throw new NotSupportedException("CapabilityResult not configured."));

        public Task<HonuaAdminEndpointResult<HonuaTemporalDiffResponse>> GetDiffAsync(
            string serviceId, int layerId, string from, string? to, int? limit,
            CancellationToken cancellationToken = default)
        {
            LastDiffCall = (serviceId, layerId, from, to, limit);
            return Task.FromResult(DiffResult ?? throw new NotSupportedException("DiffResult not configured."));
        }

        public Task<HonuaAdminEndpointResult<HonuaTemporalTimelineResponse>> GetFeatureTimelineAsync(
            string serviceId, int layerId, long featureId, int? limit,
            CancellationToken cancellationToken = default)
        {
            LastTimelineCall = (serviceId, layerId, featureId, limit);
            return Task.FromResult(TimelineResult ?? throw new NotSupportedException("TimelineResult not configured."));
        }

        public Task<HonuaAdminEndpointResult<HonuaTemporalRollbackPlanResponse>> PlanRollbackAsync(
            string serviceId, int layerId, HonuaTemporalRollbackPlanRequest request,
            CancellationToken cancellationToken = default)
        {
            LastPlanCall = (serviceId, layerId, request);
            return Task.FromResult(PlanResult ?? throw new NotSupportedException("PlanResult not configured."));
        }

        public Task<HonuaAdminEndpointResult<HonuaTemporalRollbackJobResponse>> ExecuteRollbackAsync(
            string serviceId, int layerId, HonuaTemporalRollbackExecuteRequest request,
            CancellationToken cancellationToken = default)
        {
            LastExecuteCall = (serviceId, layerId, request);
            return Task.FromResult(ExecuteResult ?? throw new NotSupportedException("ExecuteResult not configured."));
        }

        public Task<HonuaAdminEndpointResult<HonuaTemporalAsOfResponse>> ReadAsOfAsync(
            string serviceId, int layerId, long? generation, string? timestamp, int? limit,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<HonuaAdminEndpointResult<HonuaReplicaManagementDetail>> GetReplicaAsync(
            string serviceId, string replicaId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
