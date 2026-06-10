using System.Globalization;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Configured candidate temporal source (serviceId + service-local layer index) the temporal viewer probes
/// for capability. The temporal data history API (honua-server#1166) is per service/layer and exposes no
/// enumeration verb, so — exactly like the publishing workspace keying off configured publication ids —
/// the viewer is keyed by a configured list of candidate sources
/// (Honua:Server:TemporalSources / HONUA_SERVER_TEMPORAL_SOURCES, "serviceId:layerId" comma/space
/// separated). Each candidate's capability is then read live from the server.
/// </summary>
public sealed record TemporalSourceCandidate(string ServiceId, int LayerId);

/// <summary>Configured candidate temporal sources for <see cref="HonuaServerTemporalCapabilityClient"/>.</summary>
public sealed record HonuaServerTemporalOptions(IReadOnlyList<TemporalSourceCandidate> Sources)
{
    /// <summary>
    /// Parses the configured "serviceId:layerId" list (comma/space/semicolon separated). A bare "serviceId"
    /// with no layer index defaults to layer 0. Invalid tokens are skipped.
    /// </summary>
    public static HonuaServerTemporalOptions FromConfiguredList(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return new HonuaServerTemporalOptions([]);
        }

        var sources = new List<TemporalSourceCandidate>();
        foreach (var token in configured.Split([',', ';', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = token.Split(':', 2);
            var serviceId = parts[0].Trim();
            if (string.IsNullOrWhiteSpace(serviceId))
            {
                continue;
            }

            var layerId = 0;
            if (parts.Length > 1 && !int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out layerId))
            {
                continue;
            }

            sources.Add(new TemporalSourceCandidate(serviceId, layerId));
        }

        return new HonuaServerTemporalOptions(sources);
    }
}

/// <summary>
/// Live temporal data viewer client bound to the merged honua-server temporal data history API
/// (honua-server#1166 slice 1: capability discovery + as-of read) and the disconnected replica management
/// API (honua-server#1167 slice 1: replica list + detail) through the <see cref="IHonuaTemporalClient"/>
/// shim. There is no in-memory temporal data in the merged result (Console Patterns Charter section 11).
///
/// LIVE (bound here): capability discovery per configured source, an as-of read surfaced as the current
/// generation checkpoint, the diff/feature-timeline/governed-rollback slice (honua-server#1166 slices 2-5,
/// shipped as honua-server#1285), the disconnected replica list/detail, and the replica
/// conflict-review/resolution slice (honua-server#1167 slice 2: list conflicts, per-conflict
/// base/client/server detail, and an operator-selected resolution write).
///
/// HONEST BINDING (Console Patterns Charter section 11): the diff/timeline/rollback endpoints are now live,
/// but the per-source capability descriptor still gates which modes a layer exposes. When the descriptor
/// reports diff/rollback unsupported for a layer, the viewer surfaces the established not-available state
/// instead of probing; when the endpoints return 404/forbidden/unreachable, the real probe's issue is
/// surfaced. The viewer never synthesizes diffs, revision histories, or rollback plans.
/// </summary>
public sealed class HonuaServerTemporalCapabilityClient : ITemporalCapabilityClient
{
    internal const string Surface = "Temporal viewer";
    internal const string CapabilityContract = "GET /api/v1/temporal/services/{serviceId}/layers/{layerId}/capabilities";
    internal const string AsOfContract = "GET /api/v1/temporal/services/{serviceId}/layers/{layerId}/as-of";
    internal const string ReplicaContract = "GET /api/v1/admin/services/{serviceId}/replicas";
    internal const string ConflictReviewContract = "GET /api/v1/admin/services/{serviceId}/replicas/{replicaId}/conflicts";
    internal const string ConflictResolveContract = "POST /api/v1/admin/services/{serviceId}/replicas/{replicaId}/conflicts/{conflictId}/resolve";
    internal const string DiffContract = "GET /api/v1/temporal/services/{serviceId}/layers/{layerId}/diff";
    internal const string TimelineContract = "GET /api/v1/temporal/services/{serviceId}/layers/{layerId}/features/{featureId}/timeline";
    internal const string RollbackPlanContract = "POST /api/v1/temporal/services/{serviceId}/layers/{layerId}/rollback/plan";
    internal const string RollbackExecuteContract = "POST /api/v1/temporal/services/{serviceId}/layers/{layerId}/rollback";

    private readonly IHonuaTemporalClient _client;
    private readonly HonuaServerTemporalOptions _options;

    public HonuaServerTemporalCapabilityClient(IHonuaTemporalClient client, HonuaServerTemporalOptions options)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<TemporalViewerWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default)
    {
        if (_options.Sources.Count == 0)
        {
            // No candidate sources configured. The temporal API exposes no enumeration verb, so surface a
            // capability explanation rather than an empty viewer or fabricated sources.
            var state = new TemporalCapabilityState(
                Surface,
                "Not configured",
                CapabilityContract,
                "No temporal sources are configured. The server-owned temporal API is per service/layer with "
                + "no enumeration verb; configure Honua:Server:TemporalSources (or HONUA_SERVER_TEMPORAL_SOURCES) "
                + "as a list of 'serviceId:layerId' candidate sources to discover their temporal capability.");
            return new TemporalViewerWorkspace([], [state]);
        }

        var sources = new List<TemporalSourceCapability>();
        var states = new List<TemporalCapabilityState>();

        foreach (var candidate in _options.Sources)
        {
            var result = await _client
                .GetCapabilityAsync(candidate.ServiceId, candidate.LayerId, cancellationToken)
                .ConfigureAwait(false);

            if (result.Issue is { } issue)
            {
                states.Add(ToCapabilityState(issue, $"{candidate.ServiceId}/layer {candidate.LayerId.ToString(CultureInfo.InvariantCulture)}"));
                continue;
            }

            sources.Add(ToSourceCapability(result.Data!, candidate));
        }

        return new TemporalViewerWorkspace(sources, states);
    }

    public async Task<TemporalCheckpointList> GetCheckpointsAsync(
        string sourceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        if (!TryResolve(sourceId, out var candidate))
        {
            return new TemporalCheckpointList([], NotConfiguredBinding(sourceId));
        }

        // Slice 1 exposes an as-of read (current generation cursor), not a named-checkpoint store. Surface
        // the current generation as the single available as-of cursor so the scrubber/header render the real
        // server state; a richer checkpoint store arrives with the deferred timeline slice (#1285).
        var result = await _client
            .ReadAsOfAsync(candidate.ServiceId, candidate.LayerId, generation: null, timestamp: null, limit: 1, cancellationToken)
            .ConfigureAwait(false);

        if (result.Issue is { } issue)
        {
            return new TemporalCheckpointList([], ToBindingState(issue));
        }

        var data = result.Data!;
        var checkpoint = new TemporalCheckpoint(
            CheckpointId: $"gen-{data.CurrentGeneration.ToString(CultureInfo.InvariantCulture)}",
            SourceId: sourceId,
            CursorType: TemporalCursorType.Transaction,
            CursorValue: data.CurrentGeneration.ToString(CultureInfo.InvariantCulture),
            Label: $"Current (gen {data.CurrentGeneration.ToString(CultureInfo.InvariantCulture)})",
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedBy: null,
            OperationRef: null,
            JobRunId: null,
            MetadataReleaseId: null);

        return new TemporalCheckpointList([checkpoint]);
    }

    public async Task<TemporalDiff> GetDiffAsync(
        string sourceId,
        string fromCheckpointId,
        string toCheckpointId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        if (!TryResolve(sourceId, out var candidate))
        {
            return TemporalDiff.Blocked(NotConfiguredBinding(sourceId));
        }

        // Honor the per-source capability descriptor: a layer that does not declare diff support surfaces the
        // established not-available state rather than probing an endpoint the server gates off for it.
        var (capabilityIssue, deferred) = await ProbeDeferredAsync(candidate, cancellationToken).ConfigureAwait(false);
        if (capabilityIssue is { } capIssue)
        {
            return TemporalDiff.Blocked(ToBindingState(capIssue, DiffContract));
        }

        if (!deferred.SupportsDiff)
        {
            return TemporalDiff.Blocked(CapabilityUnsupported(
                DiffContract, "Temporal diff is not supported for this layer (the server capability descriptor reports diff unsupported)."));
        }

        var result = await _client
            .GetDiffAsync(candidate.ServiceId, candidate.LayerId, ToCheckpointParam(fromCheckpointId), ToCheckpointParam(toCheckpointId), limit: null, cancellationToken)
            .ConfigureAwait(false);

        if (result.Issue is { } issue)
        {
            return TemporalDiff.Blocked(ToBindingState(issue, DiffContract));
        }

        return ToTemporalDiff(result.Data!, sourceId, fromCheckpointId, toCheckpointId);
    }

    public async Task<TemporalFeatureTimeline> GetFeatureTimelineAsync(
        string sourceId,
        string featureId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        if (!TryResolve(sourceId, out var candidate))
        {
            return new TemporalFeatureTimeline(featureId, sourceId, [], NotConfiguredBinding(sourceId));
        }

        // The server timeline route is keyed by a numeric object id; a non-numeric feature id cannot be routed,
        // so surface an honest rejection rather than guessing.
        if (!long.TryParse(featureId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var objectId))
        {
            return new TemporalFeatureTimeline(featureId, sourceId, [], new TemporalBindingState(
                Surface, "Rejected", TimelineContract,
                $"Feature id '{featureId}' is not a numeric object id; the temporal timeline route is keyed by object id."));
        }

        var (capabilityIssue, deferred) = await ProbeDeferredAsync(candidate, cancellationToken).ConfigureAwait(false);
        if (capabilityIssue is { } capIssue)
        {
            return new TemporalFeatureTimeline(featureId, sourceId, [], ToBindingState(capIssue, TimelineContract));
        }

        if (!deferred.SupportsTimeline)
        {
            return new TemporalFeatureTimeline(featureId, sourceId, [], CapabilityUnsupported(
                TimelineContract, "Per-feature revision history is not supported for this layer (the server capability descriptor reports timeline unsupported)."));
        }

        var result = await _client
            .GetFeatureTimelineAsync(candidate.ServiceId, candidate.LayerId, objectId, limit: null, cancellationToken)
            .ConfigureAwait(false);

        if (result.Issue is { } issue)
        {
            return new TemporalFeatureTimeline(featureId, sourceId, [], ToBindingState(issue, TimelineContract));
        }

        return ToFeatureTimeline(result.Data!, sourceId, featureId);
    }

    public async Task<TemporalRollbackPlan> CreateRollbackPlanAsync(
        string sourceId,
        TemporalRollbackScope scope,
        string targetCheckpointId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        if (!TryResolve(sourceId, out var candidate))
        {
            return TemporalRollbackPlan.Blocked(NotConfiguredBinding(sourceId));
        }

        var (capabilityIssue, deferred) = await ProbeDeferredAsync(candidate, cancellationToken).ConfigureAwait(false);
        if (capabilityIssue is { } capIssue)
        {
            return TemporalRollbackPlan.Blocked(ToBindingState(capIssue, RollbackPlanContract));
        }

        if (!deferred.SupportsRollback)
        {
            return TemporalRollbackPlan.Blocked(CapabilityUnsupported(
                RollbackPlanContract, "Governed rollback is not supported for this layer (the server capability descriptor reports rollback unsupported)."));
        }

        var request = new HonuaTemporalRollbackPlanRequest { Checkpoint = ToCheckpointBody(targetCheckpointId) };
        var result = await _client
            .PlanRollbackAsync(candidate.ServiceId, candidate.LayerId, request, cancellationToken)
            .ConfigureAwait(false);

        if (result.Issue is { } issue)
        {
            return TemporalRollbackPlan.Blocked(ToBindingState(issue, RollbackPlanContract));
        }

        return ToRollbackPlan(result.Data!, sourceId, scope, targetCheckpointId);
    }

    public async Task<TemporalRollbackOperation> ExecuteRollbackAsync(
        string rollbackPlanId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rollbackPlanId);

        // The server computes rollback plans inline (there is no plan store), so the console-issued plan id
        // round-trips the owning source + target checkpoint. Decode it to route the forward corrective job;
        // a plan id that does not decode (or whose source is no longer configured) is reported honestly.
        if (!TryDecodeRollbackPlanId(rollbackPlanId, out var sourceId, out var targetCheckpointId)
            || !TryResolve(sourceId, out var candidate))
        {
            return TemporalRollbackOperation.Blocked(new TemporalBindingState(
                Surface, "Rejected", RollbackExecuteContract,
                $"Rollback plan '{rollbackPlanId}' could not be routed to a configured temporal source."));
        }

        var request = new HonuaTemporalRollbackExecuteRequest
        {
            Checkpoint = ToCheckpointBody(targetCheckpointId),
            Approved = true,
        };

        var result = await _client
            .ExecuteRollbackAsync(candidate.ServiceId, candidate.LayerId, request, cancellationToken)
            .ConfigureAwait(false);

        if (result.Issue is { } issue)
        {
            return TemporalRollbackOperation.Blocked(ToBindingState(issue, RollbackExecuteContract));
        }

        var handle = result.Data!;
        return new TemporalRollbackOperation(
            RollbackOperationId: handle.JobId,
            RollbackPlanId: rollbackPlanId,
            JobRunId: handle.JobId,
            Status: handle.Status,
            RequestedBy: null,
            ApprovedBy: null,
            ResultCheckpointId: $"gen-{handle.TargetCheckpoint.Generation.ToString(CultureInfo.InvariantCulture)}",
            AuditEventIds: [$"temporal.rollback.execute:{handle.JobId}"],
            FailureReason: null);
    }

    public async Task<ReplicaConflictQueue> GetReplicaConflictQueueAsync(
        string sourceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        if (!TryResolve(sourceId, out var candidate))
        {
            return new ReplicaConflictQueue([], NotConfiguredBinding(sourceId));
        }

        var result = await _client
            .ListReplicasAsync(candidate.ServiceId, cancellationToken)
            .ConfigureAwait(false);

        if (result.Issue is { } issue)
        {
            return new ReplicaConflictQueue([], ToBindingState(issue));
        }

        // System.Text.Json overrides the [] initializer with null when the server emits an explicit JSON null
        // for the key; coalesce before LINQ (matches the rest of this file's deserialized-DTO guards).
        var replicas = (result.Data!.Replicas ?? [])
            .Select(summary => ToDisconnectedReplica(summary, sourceId))
            .ToArray();

        return new ReplicaConflictQueue(replicas);
    }

    public async Task<ReplicaConflictReview> GetReplicaConflictReviewAsync(
        string replicaId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(replicaId);

        // The page hands us only a replicaId, but the server conflict routes are keyed by {serviceId} too.
        // Resolve the owning configured source by scanning each candidate service's live replica registry —
        // never guess a serviceId. If the replica is not owned by any configured source, return the honest
        // not-configured state rather than probing an arbitrary service.
        var (owner, issue) = await ResolveReplicaOwnerAsync(replicaId, cancellationToken).ConfigureAwait(false);
        if (issue is { } resolveIssue)
        {
            return new ReplicaConflictReview(Replica: null, [], resolveIssue);
        }

        if (owner is null)
        {
            return new ReplicaConflictReview(Replica: null, [], NotConfiguredReplicaBinding(replicaId));
        }

        var (candidate, sourceId, summary) = owner.Value;

        // List the replica's pending conflicts, then fetch per-conflict base/client/server detail so the
        // page renders the real three-way comparison. Pending is the operator-review default.
        var listResult = await _client
            .ListReplicaConflictsAsync(candidate.ServiceId, replicaId, status: "pending", cancellationToken)
            .ConfigureAwait(false);

        if (listResult.Issue is { } listIssue)
        {
            return new ReplicaConflictReview(ToDisconnectedReplica(summary, sourceId), [], ToBindingState(listIssue));
        }

        var summaries = listResult.Data!.Conflicts ?? [];
        var conflicts = new List<SyncConflict>(summaries.Length);
        foreach (var conflictSummary in summaries)
        {
            var detailResult = await _client
                .GetReplicaConflictAsync(candidate.ServiceId, replicaId, conflictSummary.ConflictId, cancellationToken)
                .ConfigureAwait(false);

            if (detailResult.Issue is { } detailIssue)
            {
                // A per-conflict read failed (e.g. the conflict was resolved concurrently and 404s). Surface
                // the binding state honestly rather than rendering a partial list with a silently dropped row.
                return new ReplicaConflictReview(
                    ToDisconnectedReplica(summary, sourceId), [], ToBindingState(detailIssue));
            }

            conflicts.Add(ToSyncConflict(detailResult.Data!, sourceId));
        }

        return new ReplicaConflictReview(
            ToDisconnectedReplica(summary, sourceId, conflicts.Count), conflicts);
    }

    public async Task<SyncConflictResolutionResult> ResolveConflictsAsync(
        SyncConflictResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ConflictIds.Count == 0)
        {
            return SyncConflictResolutionResult.Blocked(
                request.ConflictIds,
                request.Action,
                new TemporalBindingState(
                    Surface, "Rejected", ConflictResolveContract,
                    "No conflict was selected to resolve."));
        }

        // Map the operator-selected console resolution action to the server's resolution enum. RestoreBase
        // has no server-side equivalent (the server resolves to client/server/merge/geometry/reject/defer),
        // so it is rejected honestly rather than silently mismapped.
        if (!TryMapAction(request.Action, out var serverAction))
        {
            return SyncConflictResolutionResult.Blocked(
                request.ConflictIds,
                request.Action,
                new TemporalBindingState(
                    Surface, "Unsupported", ConflictResolveContract,
                    $"The resolution action '{request.Action}' is not supported by the server conflict-resolution "
                    + "API. Supported actions: accept client, keep server, merge fields, reject client edit, defer."));
        }

        // The resolution request carries only conflict ids; the server resolve route needs {serviceId} and
        // {replicaId} too. Resolve the owning service+replica by scanning each configured source's conflict
        // list for the first selected conflict id — never guess. A batch must belong to a single replica.
        var firstConflictId = request.ConflictIds[0];
        var (owner, issue) = await ResolveConflictOwnerAsync(firstConflictId, cancellationToken).ConfigureAwait(false);
        if (issue is { } resolveIssue)
        {
            return SyncConflictResolutionResult.Blocked(request.ConflictIds, request.Action, resolveIssue);
        }

        if (owner is null)
        {
            return SyncConflictResolutionResult.Blocked(
                request.ConflictIds,
                request.Action,
                new TemporalBindingState(
                    Surface, "Not configured", ConflictResolveContract,
                    $"Conflict '{firstConflictId}' is not owned by any configured temporal source, so its "
                    + "resolution cannot be routed to a server. Configure the owning source in "
                    + "Honua:Server:TemporalSources."));
        }

        var (candidate, replicaId) = owner.Value;
        var body = new HonuaReplicaConflictResolutionRequest { Action = serverAction };

        var resolvedRevisionId = (string?)null;
        var resolvedChangeSetId = (string?)null;
        var auditEventIds = new List<string>(request.ConflictIds.Count);
        var committedAny = false;

        // The server resolve route is per-conflict; apply the same operator-selected action to each selected
        // conflict and aggregate the committed evidence. Any per-conflict failure aborts with an honest
        // binding state rather than claiming partial success.
        foreach (var conflictId in request.ConflictIds)
        {
            var result = await _client
                .ResolveReplicaConflictAsync(candidate.ServiceId, replicaId, conflictId, body, cancellationToken)
                .ConfigureAwait(false);

            if (result.Issue is { } applyIssue)
            {
                return SyncConflictResolutionResult.Blocked(
                    request.ConflictIds, request.Action, ToBindingState(applyIssue));
            }

            var response = result.Data!;
            // The server records an audit event for every applied resolution; reconstruct its real key
            // (action + conflict id) so the result reflects audited evidence without fabricating an opaque id.
            auditEventIds.Add($"replica.conflict.resolve.{serverAction}:{conflictId}");

            if (response.CommittedNewServerState && response.Conflict?.ResolvedServerGeneration is { } gen)
            {
                committedAny = true;
                var generationMarker = $"gen-{gen.ToString(CultureInfo.InvariantCulture)}";
                resolvedChangeSetId = generationMarker;
                resolvedRevisionId = generationMarker;
            }
        }

        return new SyncConflictResolutionResult(
            ResolutionId: firstConflictId,
            ConflictIds: request.ConflictIds,
            Action: request.Action,
            ResultRevisionId: committedAny ? resolvedRevisionId : null,
            ResultChangeSetId: committedAny ? resolvedChangeSetId : null,
            AuditEventIds: auditEventIds,
            JobRunId: null);
    }

    // Resolves a replicaId to its owning configured candidate source by scanning each configured service's
    // live replica registry (the same list the queue binds). Returns the owning candidate, its console
    // source id, and the matching replica summary; null when no configured source owns the replica. A
    // transport/auth issue while scanning short-circuits to that issue so the page renders it honestly.
    private async Task<((TemporalSourceCandidate Candidate, string SourceId, HonuaReplicaManagementSummary Summary)? Owner, TemporalBindingState? Issue)>
        ResolveReplicaOwnerAsync(string replicaId, CancellationToken cancellationToken)
    {
        // Distinct services: multiple candidate sources (serviceId/layerId) can share a serviceId; the
        // replica registry is per service, so list each service once.
        foreach (var serviceId in _options.Sources.Select(s => s.ServiceId).Distinct(StringComparer.Ordinal))
        {
            var result = await _client.ListReplicasAsync(serviceId, cancellationToken).ConfigureAwait(false);
            if (result.Issue is { } issue)
            {
                return (null, ToBindingState(issue));
            }

            var summary = (result.Data!.Replicas ?? [])
                .FirstOrDefault(r => string.Equals(r.ReplicaId, replicaId, StringComparison.Ordinal));
            if (summary is null)
            {
                continue;
            }

            // Bind the owning candidate to a configured source so the console source id round-trips.
            var candidate = _options.Sources.First(s => string.Equals(s.ServiceId, serviceId, StringComparison.Ordinal));
            return ((candidate, SourceId(candidate), summary), null);
        }

        return (null, null);
    }

    // Resolves a conflictId to its owning service+replica by scanning each configured service's replicas and
    // their conflict lists. Returns the owning candidate + replica id; null when no configured source owns it.
    private async Task<((TemporalSourceCandidate Candidate, string ReplicaId)? Owner, TemporalBindingState? Issue)>
        ResolveConflictOwnerAsync(string conflictId, CancellationToken cancellationToken)
    {
        foreach (var serviceId in _options.Sources.Select(s => s.ServiceId).Distinct(StringComparer.Ordinal))
        {
            var replicasResult = await _client.ListReplicasAsync(serviceId, cancellationToken).ConfigureAwait(false);
            if (replicasResult.Issue is { } replicasIssue)
            {
                return (null, ToBindingState(replicasIssue));
            }

            var candidate = _options.Sources.First(s => string.Equals(s.ServiceId, serviceId, StringComparison.Ordinal));
            foreach (var replica in replicasResult.Data!.Replicas ?? [])
            {
                var conflictsResult = await _client
                    .ListReplicaConflictsAsync(serviceId, replica.ReplicaId, status: null, cancellationToken)
                    .ConfigureAwait(false);
                if (conflictsResult.Issue is { } conflictsIssue)
                {
                    return (null, ToBindingState(conflictsIssue));
                }

                if ((conflictsResult.Data!.Conflicts ?? [])
                    .Any(c => string.Equals(c.ConflictId, conflictId, StringComparison.Ordinal)))
                {
                    return ((candidate, replica.ReplicaId), null);
                }
            }
        }

        return (null, null);
    }

    private static SyncConflict ToSyncConflict(HonuaReplicaConflictDetail detail, string sourceId) =>
        new(
            ConflictId: detail.ConflictId,
            ReplicaId: detail.ReplicaId,
            SourceId: sourceId,
            LayerId: detail.LayerId.ToString(CultureInfo.InvariantCulture),
            FeatureId: detail.ObjectId.ToString(CultureInfo.InvariantCulture),
            ConflictType: MapConflictType(detail.ConflictType),
            BaseRevisionId: null,
            ServerRevisionId: $"gen-{detail.ServerGeneration.ToString(CultureInfo.InvariantCulture)}",
            FieldConflicts: BuildFieldConflicts(detail),
            GeometryConflict: string.Equals(detail.ConflictType, "geometry", StringComparison.OrdinalIgnoreCase),
            DetectedAt: detail.DetectedAt,
            Status: MapConflictStatus(detail.Status));

    // The server stores base/client/server feature states as opaque JSON objects. Project them into the
    // page's field-level three-way comparison by unioning the attribute keys across the three states and
    // emitting a row per field with each side's value (only fields that actually differ between client and
    // server, so the operator sees the conflicting fields rather than every unchanged attribute).
    private static IReadOnlyList<SyncFieldConflict> BuildFieldConflicts(HonuaReplicaConflictDetail detail)
    {
        var baseAttrs = ReadAttributes(detail.BaseState);
        var clientAttrs = ReadAttributes(detail.ClientState);
        var serverAttrs = ReadAttributes(detail.ServerState);

        var fields = baseAttrs.Keys
            .Concat(clientAttrs.Keys)
            .Concat(serverAttrs.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal);

        var rows = new List<SyncFieldConflict>();
        foreach (var field in fields)
        {
            baseAttrs.TryGetValue(field, out var baseValue);
            clientAttrs.TryGetValue(field, out var clientValue);
            serverAttrs.TryGetValue(field, out var serverValue);

            // Only surface fields where the client and server diverge — the conflicting fields.
            if (!string.Equals(clientValue, serverValue, StringComparison.Ordinal))
            {
                rows.Add(new SyncFieldConflict(field, baseValue, clientValue, serverValue));
            }
        }

        return rows;
    }

    // Reads a feature state's attribute bag. Honua/GeoServices feature objects carry attributes under an
    // "attributes" object; fall back to the root object's scalar properties when no such envelope is present.
    private static Dictionary<string, string?> ReadAttributes(System.Text.Json.JsonElement? state)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (state is not { } element || element.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return result;
        }

        var attributes = element.TryGetProperty("attributes", out var attrs)
            && attrs.ValueKind == System.Text.Json.JsonValueKind.Object
            ? attrs
            : element;

        foreach (var property in attributes.EnumerateObject())
        {
            if (string.Equals(property.Name, "attributes", StringComparison.Ordinal)
                || string.Equals(property.Name, "geometry", StringComparison.Ordinal))
            {
                continue;
            }

            result[property.Name] = property.Value.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => property.Value.GetString(),
                System.Text.Json.JsonValueKind.Null => null,
                _ => property.Value.GetRawText(),
            };
        }

        return result;
    }

    private static SyncConflictType MapConflictType(string conflictType) => conflictType.ToLowerInvariant() switch
    {
        "attribute" => SyncConflictType.Attribute,
        "geometry" => SyncConflictType.Geometry,
        "deleteupdate" => SyncConflictType.DeleteUpdate,
        "updatedelete" => SyncConflictType.UpdateDelete,
        "duplicateinsert" => SyncConflictType.InsertDuplicate,
        "attachment" => SyncConflictType.Attachment,
        "relationship" => SyncConflictType.Relationship,
        _ => SyncConflictType.Attribute,
    };

    private static SyncConflictStatus MapConflictStatus(string status) => status.ToLowerInvariant() switch
    {
        "pending" => SyncConflictStatus.Pending,
        "resolved" => SyncConflictStatus.Resolved,
        "deferred" => SyncConflictStatus.Superseded,
        _ => SyncConflictStatus.Pending,
    };

    // Maps the console resolution action to the server's resolution action token. RestoreBase has no
    // server-side equivalent (the server resolves to accept-client/keep-server/merge/geometry/reject/defer),
    // so it is reported as unmappable rather than silently coerced to another action.
    private static bool TryMapAction(SyncResolutionAction action, out string serverAction)
    {
        switch (action)
        {
            case SyncResolutionAction.AcceptClient:
                serverAction = "acceptClient";
                return true;
            case SyncResolutionAction.KeepServer:
                serverAction = "keepServer";
                return true;
            case SyncResolutionAction.MergeFields:
                serverAction = "mergeFields";
                return true;
            case SyncResolutionAction.RejectClientEdit:
                serverAction = "rejectClient";
                return true;
            case SyncResolutionAction.Defer:
                serverAction = "defer";
                return true;
            default:
                serverAction = string.Empty;
                return false;
        }
    }

    private static TemporalBindingState NotConfiguredReplicaBinding(string replicaId) =>
        new(
            Surface,
            "Not configured",
            ConflictReviewContract,
            $"Replica '{replicaId}' is not owned by any configured temporal source. Configure its owning "
            + "service in Honua:Server:TemporalSources to review its disconnected-sync conflicts.");

    // The Console source id encodes the configured candidate as "serviceId/layerId" (see ToSourceCapability).
    private bool TryResolve(string sourceId, out TemporalSourceCandidate candidate)
    {
        candidate = _options.Sources.FirstOrDefault(c => string.Equals(SourceId(c), sourceId, StringComparison.Ordinal))!;
        return candidate is not null;
    }

    private static string SourceId(TemporalSourceCandidate candidate) =>
        $"{candidate.ServiceId}/{candidate.LayerId.ToString(CultureInfo.InvariantCulture)}";

    private static TemporalSourceCapability ToSourceCapability(
        HonuaTemporalCapabilityResponse capability,
        TemporalSourceCandidate candidate)
    {
        // Slice 1 supports capability discovery + as-of. Diff/timeline/rollback are reported by the server's
        // deferred-capabilities block; map the highest live mode honestly so the page never promises a mode
        // the server did not declare. Deferred is non-null-typed with a new() initializer but deserializes to
        // null on an explicit server JSON null, so coalesce before the deref.
        var deferred = capability.Deferred ?? new();
        var mode = !capability.SupportsHistory && !capability.SupportsAsOf
            ? TemporalMode.None
            : deferred.SupportsRollback
                ? TemporalMode.Rollback
                : deferred.SupportsDiff
                    ? TemporalMode.Diff
                    : capability.SupportsHistory
                        ? TemporalMode.History
                        : TemporalMode.AsOf;

        return new TemporalSourceCapability(
            SourceId: SourceId(candidate),
            ResourceId: capability.LayerName ?? candidate.ServiceId,
            LayerId: candidate.LayerId.ToString(CultureInfo.InvariantCulture),
            Mode: mode,
            // The replica management API (#1167) is bidirectional disconnected sync; conflict review +
            // resolution shipped in slice 2 (#1167) and is bound live. There is no per-layer conflict-review
            // capability flag in the temporal capability descriptor, so eligibility is keyed off the source
            // exposing the replica registry (replica tracking). A provider that cannot support manual review
            // returns 501 from the conflict routes, which the review/resolve paths surface as an honest
            // Unsupported binding rather than fabricated conflicts.
            SyncCapability: TemporalSyncCapability.Bidirectional,
            RollbackSupported: deferred.SupportsRollback,
            SyncConflictReviewSupported: true,
            RetentionPolicyId: null)
        {
            HistoryModel = capability.CursorKind,
            GeometryHistorySupported = capability.SupportsHistory,
            AttributeHistorySupported = capability.SupportsHistory,
            ReplicaTrackingSupported = true,
            HistoryReadPermitted = true,
        };
    }

    private static DisconnectedReplica ToDisconnectedReplica(
        HonuaReplicaManagementSummary summary,
        string sourceId,
        int pendingConflictCount = 0) =>
        new(
            ReplicaId: summary.ReplicaId,
            ReplicaName: summary.ReplicaName,
            SourceId: sourceId,
            OwnerId: "—",
            DeviceId: null,
            SyncDirection: TemporalSyncCapability.Bidirectional,
            BaseCheckpointId: "—",
            ReplicaServerGen: 0,
            LastSyncAt: summary.LastSyncTime,
            Status: ReplicaStatus.Active,
            // The list path leaves the count at 0 to avoid an N+1 conflict fetch per replica; the review path
            // (which already fetched the live conflict list) supplies the real pending count.
            PendingConflictCount: pendingConflictCount);

    private static TemporalCapabilityState ToCapabilityState(HonuaAdminEndpointIssue issue, string sourceLabel) =>
        new(
            $"{Surface} · {sourceLabel}",
            issue.State,
            issue.Contract,
            issue.StatusCode is null
                ? issue.Detail
                : $"{issue.Detail} HTTP {issue.StatusCode.Value.ToString(CultureInfo.InvariantCulture)}.");

    private static TemporalBindingState ToBindingState(HonuaAdminEndpointIssue issue) =>
        new(
            Surface,
            issue.State,
            issue.Contract,
            issue.StatusCode is null
                ? issue.Detail
                : $"{issue.Detail} HTTP {issue.StatusCode.Value.ToString(CultureInfo.InvariantCulture)}.");

    private static TemporalBindingState NotConfiguredBinding(string sourceId) =>
        new(
            Surface,
            "Not configured",
            CapabilityContract,
            $"Temporal source '{sourceId}' is not a configured candidate source. Configure it in "
            + "Honua:Server:TemporalSources to inspect its temporal capability.");

    // Reads the per-source capability descriptor so diff/timeline/rollback honor the server's declared
    // support flags before probing the (now-live) endpoints. A capability read failure short-circuits to its
    // issue so the surface renders the honest binding state.
    private async Task<(HonuaAdminEndpointIssue? Issue, HonuaTemporalDeferredCapabilities Deferred)> ProbeDeferredAsync(
        TemporalSourceCandidate candidate,
        CancellationToken cancellationToken)
    {
        var result = await _client
            .GetCapabilityAsync(candidate.ServiceId, candidate.LayerId, cancellationToken)
            .ConfigureAwait(false);

        return result.Issue is { } issue
            ? (issue, new HonuaTemporalDeferredCapabilities())
            : (null, result.Data!.Deferred ?? new HonuaTemporalDeferredCapabilities());
    }

    // Maps a console checkpoint id to the server checkpoint query/body syntax. The console emits "gen-N"
    // generation checkpoints (see GetCheckpointsAsync); the server accepts a bare generation number, so strip
    // the prefix. Any other token (e.g. "timestamp:...", "named:...") is passed through verbatim.
    private static string ToCheckpointParam(string checkpointId) =>
        checkpointId.StartsWith("gen-", StringComparison.Ordinal)
            ? checkpointId["gen-".Length..]
            : checkpointId;

    private static HonuaTemporalCheckpointBody ToCheckpointBody(string checkpointId)
    {
        var value = ToCheckpointParam(checkpointId);
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var generation))
        {
            return new HonuaTemporalCheckpointBody { Kind = "generation", Generation = generation };
        }

        var separator = value.IndexOf(':');
        return separator > 0
            ? new HonuaTemporalCheckpointBody { Kind = value[..separator], Value = value[(separator + 1)..] }
            : new HonuaTemporalCheckpointBody { Kind = "named", Value = value };
    }

    // The server has no rollback-plan store (plans are computed inline), so the console plan id round-trips
    // the owning source + target checkpoint needed to execute the forward corrective job. Encoded as
    // "rb|<sourceId>|<targetCheckpointId>"; the source id itself contains no '|' (it is "serviceId/layerId").
    private static string EncodeRollbackPlanId(string sourceId, string targetCheckpointId) =>
        $"rb|{sourceId}|{targetCheckpointId}";

    private static bool TryDecodeRollbackPlanId(string rollbackPlanId, out string sourceId, out string targetCheckpointId)
    {
        sourceId = string.Empty;
        targetCheckpointId = string.Empty;

        var parts = rollbackPlanId.Split('|');
        if (parts.Length != 3 || !string.Equals(parts[0], "rb", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(parts[1]) || string.IsNullOrWhiteSpace(parts[2]))
        {
            return false;
        }

        sourceId = parts[1];
        targetCheckpointId = parts[2];
        return true;
    }

    private static TemporalDiff ToTemporalDiff(
        HonuaTemporalDiffResponse diff,
        string sourceId,
        string fromCheckpointId,
        string toCheckpointId)
    {
        var summary = diff.Summary;
        var sample = (diff.Changes ?? [])
            .Select(ToFeatureChange)
            .ToArray();

        return new TemporalDiff(
            DiffId: $"{sourceId}:{diff.From.Generation.ToString(CultureInfo.InvariantCulture)}->{diff.To.Generation.ToString(CultureInfo.InvariantCulture)}",
            SourceId: sourceId,
            FromCheckpointId: fromCheckpointId,
            ToCheckpointId: toCheckpointId,
            AddedFeatures: summary.Added,
            RemovedFeatures: summary.Removed,
            UpdatedFeatures: summary.Total - summary.Added - summary.Removed,
            GeometryChangedFeatures: summary.GeometryChanged,
            AttributeChangedFeatures: summary.AttributeChanged,
            SampleFeatureChanges: sample,
            GeneratedAt: DateTimeOffset.UtcNow);
    }

    private static TemporalFeatureChange ToFeatureChange(HonuaTemporalFeatureDiffResponse change)
    {
        var attributeChanges = (change.FieldChanges ?? [])
            .Select(f => new TemporalAttributeChange(
                Field: f.Field,
                Before: ReadJsonScalar(f.OldValue),
                After: ReadJsonScalar(f.NewValue),
                Masked: f.Masked))
            .ToArray();

        return new TemporalFeatureChange(
            FeatureId: change.ObjectId.ToString(CultureInfo.InvariantCulture),
            ChangeType: MapChangeType(change.PrimaryClass),
            FromRevisionId: null,
            ToRevisionId: null,
            AttributeChanges: attributeChanges,
            GeometryChanged: change.GeometryChanged,
            ActorId: change.Attribution?.Actor,
            OperationRef: change.Attribution?.Operation);
    }

    private static TemporalChangeType MapChangeType(string primaryClass) => primaryClass.ToLowerInvariant() switch
    {
        "added" or "insert" or "inserted" => TemporalChangeType.Added,
        "removed" or "delete" or "deleted" => TemporalChangeType.Removed,
        "unchanged" => TemporalChangeType.Unchanged,
        _ => TemporalChangeType.Updated,
    };

    private static TemporalFeatureTimeline ToFeatureTimeline(
        HonuaTemporalTimelineResponse timeline,
        string sourceId,
        string featureId)
    {
        var revisions = (timeline.Revisions ?? [])
            .Select(r => new TemporalRevision(
                RevisionId: $"gen-{r.Generation.ToString(CultureInfo.InvariantCulture)}",
                TransactionTime: ParseTimestamp(r.ChangedAt),
                Operation: MapRevisionOperation(r.Operation),
                ActorDisplayName: r.Attribution?.Actor,
                SourceClient: r.Attribution?.Source,
                JobRunId: r.Attribution?.SourceId,
                ReleaseOperationId: null,
                Reason: r.Attribution?.Operation,
                GeometryChanged: false,
                ChangedFieldCount: 0,
                RestoreAllowed: false))
            .ToArray();

        return new TemporalFeatureTimeline(featureId, sourceId, revisions);
    }

    private static TemporalRevisionOperation MapRevisionOperation(string operation) => operation.ToLowerInvariant() switch
    {
        "insert" or "inserted" or "added" => TemporalRevisionOperation.Insert,
        "delete" or "deleted" or "removed" => TemporalRevisionOperation.Delete,
        "restore" or "restored" => TemporalRevisionOperation.Restore,
        _ => TemporalRevisionOperation.Update,
    };

    private static TemporalRollbackPlan ToRollbackPlan(
        HonuaTemporalRollbackPlanResponse plan,
        string sourceId,
        TemporalRollbackScope scope,
        string targetCheckpointId)
    {
        var validation = (plan.ValidationFindings ?? [])
            .Select(FormatFinding)
            .ToArray();
        var compatibility = (plan.CompatibilityFindings ?? [])
            .Select(FormatFinding)
            .ToArray();

        var (mode, requiresJob) = MapRollbackState(plan.State);

        return new TemporalRollbackPlan(
            RollbackPlanId: EncodeRollbackPlanId(sourceId, targetCheckpointId),
            SourceId: sourceId,
            TargetScope: scope,
            TargetCheckpointId: $"gen-{plan.TargetCheckpoint.Generation.ToString(CultureInfo.InvariantCulture)}",
            CurrentCheckpointId: $"gen-{plan.CurrentGeneration.ToString(CultureInfo.InvariantCulture)}",
            AffectedFeatureCount: plan.AffectedFeatureCount,
            RequiresJob: requiresJob,
            RequiresApproval: plan.RequiresApproval,
            RollbackMode: mode,
            RiskLevel: DeriveRiskLevel(plan.State, validation),
            ValidationFindings: validation,
            CompatibilityFindings: compatibility);
    }

    // Maps the server rollback disposition ("supported/blocked/scriptRequired/jobRequired/manual") to the
    // console rollback mode + whether the corrective operation runs as a job.
    private static (TemporalRollbackMode Mode, bool RequiresJob) MapRollbackState(string state) => state.ToLowerInvariant() switch
    {
        "scriptrequired" => (TemporalRollbackMode.ScriptRequired, true),
        "jobrequired" => (TemporalRollbackMode.DataRevert, true),
        "manual" => (TemporalRollbackMode.Manual, false),
        "blocked" => (TemporalRollbackMode.Manual, false),
        _ => (TemporalRollbackMode.DataRevert, true),
    };

    private static string DeriveRiskLevel(string state, IReadOnlyList<string> validationFindings)
    {
        if (string.Equals(state, "blocked", StringComparison.OrdinalIgnoreCase))
        {
            return "blocked";
        }

        return validationFindings.Count > 0 ? "elevated" : "normal";
    }

    private static string FormatFinding(HonuaTemporalRollbackFindingResponse finding) =>
        $"[{finding.Severity}] {finding.Message} ({finding.Code})";

    private static string? ReadJsonScalar(System.Text.Json.JsonElement? element) => element switch
    {
        null => null,
        { ValueKind: System.Text.Json.JsonValueKind.Null } => null,
        { ValueKind: System.Text.Json.JsonValueKind.String } value => value.GetString(),
        { } value => value.GetRawText(),
    };

    private static DateTimeOffset ParseTimestamp(string raw) =>
        DateTimeOffset.TryParse(
            raw, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : default;

    private static TemporalBindingState ToBindingState(HonuaAdminEndpointIssue issue, string contract) =>
        new(
            Surface,
            issue.State,
            contract,
            issue.StatusCode is null
                ? issue.Detail
                : $"{issue.Detail} HTTP {issue.StatusCode.Value.ToString(CultureInfo.InvariantCulture)}.");

    private static TemporalBindingState CapabilityUnsupported(string contract, string detail) =>
        new(Surface, TemporalBindingState.Unsupported, contract, detail);
}
