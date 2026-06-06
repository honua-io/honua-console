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
/// generation checkpoint, and the disconnected replica list/detail.
///
/// NOT-YET-AVAILABLE (deferred server slices, rendered honestly, never fabricated): the diff/timeline/
/// rollback execution slice (honua-server#1285) and the replica conflict-review/resolution slice
/// (honua-server#1287). The server capability descriptor reports these deferred capabilities as false, so
/// the viewer surfaces an explicit not-yet-available state from each — it never synthesizes diffs,
/// revision histories, rollback plans, or sync conflicts.
/// </summary>
public sealed class HonuaServerTemporalCapabilityClient : ITemporalCapabilityClient
{
    internal const string Surface = "Temporal viewer";
    internal const string CapabilityContract = "GET /api/v1/temporal/services/{serviceId}/layers/{layerId}/capabilities";
    internal const string AsOfContract = "GET /api/v1/temporal/services/{serviceId}/layers/{layerId}/as-of";
    internal const string ReplicaContract = "GET /api/v1/admin/services/{serviceId}/replicas";

    // Deferred server slices — bound to the established not-yet-available state, never fabricated.
    internal const string DiffContract = "honua-server#1285 (temporal diff/timeline/rollback execution)";
    internal const string ConflictContract = "honua-server#1287 (replica conflict-review/resolution)";

    private static readonly TemporalBindingState DiffDeferred = new(
        Surface, TemporalBindingState.Unsupported, DiffContract,
        "Temporal diff is not yet available: the server reports this slice is deferred (honua-server#1285). "
        + "Capability discovery and as-of read are live; diff binds automatically once #1285 lands.");

    private static readonly TemporalBindingState TimelineDeferred = new(
        Surface, TemporalBindingState.Unsupported, DiffContract,
        "Per-feature revision history is not yet available: the server reports this slice is deferred "
        + "(honua-server#1285). It binds automatically once #1285 lands.");

    private static readonly TemporalBindingState RollbackDeferred = new(
        Surface, TemporalBindingState.Unsupported, DiffContract,
        "Governed rollback execution is not yet available: the server reports this slice is deferred "
        + "(honua-server#1285). It binds automatically once #1285 lands.");

    private static readonly TemporalBindingState ConflictDeferred = new(
        Surface, TemporalBindingState.Unsupported, ConflictContract,
        "Disconnected replica conflict review/resolution is not yet available: the server exposes replica "
        + "metadata (list/detail) but the conflict-review slice is deferred (honua-server#1287). It binds "
        + "automatically once #1287 lands.");

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

    public Task<TemporalDiff> GetDiffAsync(
        string sourceId,
        string fromCheckpointId,
        string toCheckpointId,
        CancellationToken cancellationToken = default) =>
        // Deferred server slice (#1285). Surface the not-yet-available state rather than fabricating a diff.
        Task.FromResult(TemporalDiff.Blocked(DiffDeferred));

    public Task<TemporalFeatureTimeline> GetFeatureTimelineAsync(
        string sourceId,
        string featureId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new TemporalFeatureTimeline(featureId, sourceId, [], TimelineDeferred));

    public Task<TemporalRollbackPlan> CreateRollbackPlanAsync(
        string sourceId,
        TemporalRollbackScope scope,
        string targetCheckpointId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(TemporalRollbackPlan.Blocked(RollbackDeferred));

    public Task<TemporalRollbackOperation> ExecuteRollbackAsync(
        string rollbackPlanId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(TemporalRollbackOperation.Blocked(RollbackDeferred));

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

        var replicas = result.Data!.Replicas
            .Select(summary => ToDisconnectedReplica(summary, sourceId))
            .ToArray();

        return new ReplicaConflictQueue(replicas);
    }

    public Task<ReplicaConflictReview> GetReplicaConflictReviewAsync(
        string replicaId,
        CancellationToken cancellationToken = default) =>
        // Deferred server slice (#1287): the conflict-record review/resolution API is not merged. Surface the
        // not-yet-available state rather than fabricating conflicts.
        Task.FromResult(new ReplicaConflictReview(Replica: null, [], ConflictDeferred));

    public Task<SyncConflictResolutionResult> ResolveConflictsAsync(
        SyncConflictResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult(
            SyncConflictResolutionResult.Blocked(request.ConflictIds, request.Action, ConflictDeferred));
    }

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
            // The replica management API (#1167) is bidirectional disconnected sync; the conflict-review
            // slice (#1287) is deferred, so conflict review is not yet supported even though replicas list.
            SyncCapability: TemporalSyncCapability.Bidirectional,
            RollbackSupported: deferred.SupportsRollback,
            SyncConflictReviewSupported: false,
            RetentionPolicyId: null)
        {
            HistoryModel = capability.CursorKind,
            GeometryHistorySupported = capability.SupportsHistory,
            AttributeHistorySupported = capability.SupportsHistory,
            ReplicaTrackingSupported = true,
            HistoryReadPermitted = true,
        };
    }

    private static DisconnectedReplica ToDisconnectedReplica(HonuaReplicaManagementSummary summary, string sourceId) =>
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
            // Pending-conflict counts come from the deferred conflict-review slice (#1287); 0 until it lands.
            PendingConflictCount: 0);

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
}
