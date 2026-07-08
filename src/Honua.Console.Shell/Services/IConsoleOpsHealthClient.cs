using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Reads honua-server's consolidated ops-health snapshot (group
/// <c>/api/v1/admin/observability</c>, admin-authorized via <c>X-API-Key</c>, bare JSON —
/// NO ApiResponse envelope) from the active environment profile's server. The read is
/// independently permissioned/optional, so it returns an
/// <see cref="OperateSectionResult{T}"/> whose status drives the shared
/// missing/forbidden/unsupported/unavailable surfaces, mirroring
/// <see cref="IConsoleMonitoringMetricsClient"/>. Per the Console Patterns Charter
/// section 11 the client never returns seeded data; with no environment bound the read
/// returns a missing-binding result.
/// </summary>
public interface IConsoleOpsHealthClient
{
    /// <summary>Reads the consolidated ops-health snapshot for the active environment.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The snapshot, or a non-allowed section status.</returns>
    Task<OperateSectionResult<OpsHealthSnapshotResponse>> GetSnapshotAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the cluster-aggregated ops-health history for the active environment (console#288,
    /// honua-server PR #2576) — trend data for the serving-latency, geoprocessing, alert-dispatch,
    /// and database-vitals charts, and the reconnect gap-fill contract for the ops-health realtime
    /// hub group. A 404/501 from an older server (the route not yet deployed) degrades to
    /// <see cref="OperateSectionStatus.Unsupported"/> rather than a generic failure.
    /// </summary>
    /// <param name="query">The window/resolution/per-replica selection.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The history response, or a non-allowed section status.</returns>
    Task<OperateSectionResult<OpsHealthHistoryResponse>> GetHistoryAsync(
        ConsoleOpsHealthHistoryQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The client-side selection for the ops-health history read (console#288): a look-back window
/// (e.g. <c>1h</c>/<c>24h</c>/<c>7d</c>), a rollup resolution (<c>1m</c>/<c>5m</c>/<c>1h</c>), and
/// whether to request the per-replica breakdown instead of the cluster-merged series. Mirrors the
/// server's <c>OpsHealthHistoryQuery</c> query-string parameters exactly (honua-server PR #2576).
/// </summary>
public sealed record ConsoleOpsHealthHistoryQuery(string Window, string Resolution, bool PerReplica)
{
    /// <summary>The default selection: the last hour at 1-minute resolution, cluster-merged.</summary>
    public static ConsoleOpsHealthHistoryQuery Default { get; } = new("1h", "1m", false);
}
