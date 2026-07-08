using Honua.Console.Contracts;

namespace Honua.Console.Shell.Models;

/// <summary>
/// The pinned reconnect-seam behavior for the Ops Health trend charts (console#288 addendum
/// 2026-07-07 item 3): live pushes from the <c>ops-health</c> realtime hub group are held in a
/// separate "live band" at their native (~30-60s) cadence rather than appended onto the
/// downsampled 5-min/hourly history window. The live band is cleared whenever a fresh history
/// fetch's <c>to</c> timestamp advances past it — the history read is the sole gap-fill contract
/// (no Last-Event-ID) — so a point is rendered exactly once: from history while it is within a
/// confirmed window, then from the live band until the next history refresh subsumes it. Charts
/// mark the boundary between the two with a seam annotation
/// (<see cref="Services.OpsHealthTrendCharts"/>) rather than silently blending resolutions.
///
/// The live push carries the same cluster-aggregated snapshot DTO as <c>GET ops-health</c> (no
/// per-replica breakdown field), so the live band always projects as cluster-merged points even
/// when the history request is a per-replica breakdown; it still contributes to the aggregate
/// per-protocol trend and the vitals trend, honestly, without inventing a replica id.
/// </summary>
public static class OpsHealthLiveBand
{
    /// <summary>
    /// Idempotently appends <paramref name="incoming"/> to <paramref name="existing"/>. A push
    /// already covered by the confirmed history window (<paramref name="historyCutoff"/>) is
    /// dropped rather than double-counted; a push already seen (same flush timestamp, to the
    /// second — the at-least-once dedup key for a point-in-time snapshot push that carries no
    /// event id of its own, mirroring <see cref="OperateTimelineEntries.Append"/>'s pattern) is a
    /// no-op.
    /// </summary>
    public static IReadOnlyList<OpsHealthSnapshotResponse> Append(
        IReadOnlyList<OpsHealthSnapshotResponse> existing,
        OpsHealthSnapshotResponse incoming,
        DateTimeOffset historyCutoff)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(incoming);

        if (incoming.GeneratedAt <= historyCutoff)
        {
            return existing;
        }

        var key = DedupeKey(incoming);
        foreach (var entry in existing)
        {
            if (DedupeKey(entry) == key)
            {
                return existing;
            }
        }

        var result = new List<OpsHealthSnapshotResponse>(existing.Count + 1);
        result.AddRange(existing);
        result.Add(incoming);
        result.Sort((a, b) => a.GeneratedAt.CompareTo(b.GeneratedAt));
        return result;
    }

    /// <summary>
    /// Drops every live-band entry now covered by a fresh history fetch (called once per history
    /// refresh, with the new response's <c>to</c> as the cutoff). This is what keeps the live band
    /// from growing without bound and what prevents a point from ever rendering twice across a
    /// refresh.
    /// </summary>
    public static IReadOnlyList<OpsHealthSnapshotResponse> TrimToAfter(
        IReadOnlyList<OpsHealthSnapshotResponse> existing,
        DateTimeOffset cutoff)
    {
        ArgumentNullException.ThrowIfNull(existing);
        return existing.Where(s => s.GeneratedAt > cutoff).ToArray();
    }

    /// <summary>Projects the live band onto latency trend points for one protocol.</summary>
    public static IReadOnlyList<OpsHealthLatencyTrendPointView> ToLatencyPoints(
        IReadOnlyList<OpsHealthSnapshotResponse> liveBand,
        string protocol)
    {
        ArgumentNullException.ThrowIfNull(liveBand);
        var result = new List<OpsHealthLatencyTrendPointView>();
        foreach (var snapshot in liveBand)
        {
            var row = snapshot.ServingLatency?.Protocols?
                .FirstOrDefault(p => string.Equals(p.Protocol, protocol, StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                continue;
            }

            result.Add(new OpsHealthLatencyTrendPointView(
                snapshot.GeneratedAt, row.P50Ms, row.P95Ms, row.P99Ms, row.ErrorRate, row.RequestCount, row.ErrorCount));
        }

        return result;
    }

    /// <summary>Projects the live band onto vitals trend points (always cluster-merged; see type remarks).</summary>
    public static IReadOnlyList<OpsHealthVitalsTrendPointView> ToVitalsPoints(
        IReadOnlyList<OpsHealthSnapshotResponse> liveBand)
    {
        ArgumentNullException.ThrowIfNull(liveBand);
        return liveBand
            .Select(s => new OpsHealthVitalsTrendPointView(
                s.GeneratedAt,
                null,
                string.IsNullOrWhiteSpace(s.OverallStatus) ? "unknown" : s.OverallStatus!,
                s.Geoprocessing?.TotalActive ?? 0,
                s.AlertDispatch?.PendingCount,
                s.AlertDispatch?.DeadLetteredCount,
                s.Database?.HasConnectionPoolData == true ? s.Database.ConnectionPoolUtilization : null,
                s.Database?.ActiveConnections ?? 0,
                s.Database?.CacheHitRatio ?? 0,
                s.Database?.ErrorRate ?? 0))
            .ToArray();
    }

    /// <summary>Every protocol the live band has seen (for protocols the history window predates).</summary>
    public static IReadOnlyList<string> DistinctProtocols(IReadOnlyList<OpsHealthSnapshotResponse> liveBand)
    {
        ArgumentNullException.ThrowIfNull(liveBand);
        return liveBand
            .SelectMany(s => s.ServingLatency?.Protocols ?? [])
            .Select(p => p.Protocol ?? string.Empty)
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    // The live push carries no event id; its idempotency key is the flush timestamp rounded to
    // the second (the addendum's "bucketStart" fallback for a point-in-time snapshot push rather
    // than a bucketed rollup row).
    private static long DedupeKey(OpsHealthSnapshotResponse snapshot) => snapshot.GeneratedAt.ToUnixTimeSeconds();
}
