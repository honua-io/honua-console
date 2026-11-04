using Honua.Console.Contracts;

namespace Honua.Console.Shell.Models;

public static class OpsHealthLiveBand
{
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

    public static IReadOnlyList<OpsHealthSnapshotResponse> TrimToAfter(
        IReadOnlyList<OpsHealthSnapshotResponse> existing,
        DateTimeOffset cutoff)
    {
        ArgumentNullException.ThrowIfNull(existing);
        return existing.Where(s => s.GeneratedAt > cutoff).ToArray();
    }

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
