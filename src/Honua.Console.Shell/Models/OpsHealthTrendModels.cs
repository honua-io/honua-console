using Honua.Console.Contracts;

namespace Honua.Console.Shell.Models;

public sealed record OpsHealthTrendView(
    string RangeLabel,
    string Resolution,
    bool PerReplica,
    DateTimeOffset From,
    DateTimeOffset To,
    bool IsFirstRun,
    IReadOnlyList<OpsHealthLatencyTrendSeriesView> LatencySeries,
    IReadOnlyList<OpsHealthVitalsTrendPointView> VitalsPoints)
{
    public static OpsHealthTrendView Empty(string rangeLabel, string resolution, bool perReplica) => new(
        rangeLabel, resolution, perReplica, default, default, true, [], []);
}

public sealed record OpsHealthLatencyTrendSeriesView(
    string Protocol,
    string? ReplicaId,
    IReadOnlyList<OpsHealthLatencyTrendPointView> Points,
    bool HasBreach)
{
    public string SeriesLabel => ReplicaId is null ? Protocol : $"{Protocol} ({ReplicaId})";
}

public sealed record OpsHealthLatencyTrendPointView(
    DateTimeOffset BucketStart,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    double ErrorRate,
    long RequestCount,
    long ErrorCount);

// A null ReplicaId means the point is cluster-merged rather than one replica.
public sealed record OpsHealthVitalsTrendPointView(
    DateTimeOffset BucketStart,
    string? ReplicaId,
    string OverallStatus,
    int GpQueueTotal,
    long? AlertPending,
    long? AlertDeadLettered,
    double? DbPoolUtilization,
    int DbActiveConnections,
    double CacheHitRatio,
    double ErrorRate);

public static class OpsHealthTrendMapper
{
    // Mirrors OpsHealthDataSource's serving-latency SLO heuristic exactly (display-only; the
    // server does not label history rows either) so the trend chart's breach badge never
    // disagrees with the at-a-glance snapshot table. Public so the page code-behind can apply the
    // same test when extending a series with live-band points (which arrive after this mapper has
    // already run over the history response).
    public const double P95BreachMs = 1000.0;
    public const double ErrorRateWarn = 0.01;

    public static bool HasBreach(IEnumerable<OpsHealthLatencyTrendPointView> points) =>
        points.Any(p => p.ErrorRate >= ErrorRateWarn || p.P95Ms >= P95BreachMs);

    public static OpsHealthTrendView Map(OpsHealthHistoryResponse response, string rangeLabel)
    {
        ArgumentNullException.ThrowIfNull(response);

        var latencySeries = (response.Latency ?? [])
            .Select(MapLatencySeries)
            .ToArray();

        var vitalsPoints = (response.Vitals ?? [])
            .OrderBy(v => v.BucketStart)
            .ThenBy(v => v.ReplicaId, StringComparer.Ordinal)
            .Select(MapVitalsPoint)
            .ToArray();

        var isFirstRun = latencySeries.All(s => s.Points.Count == 0) && vitalsPoints.Length == 0;

        return new OpsHealthTrendView(
            rangeLabel,
            response.Resolution ?? "1m",
            response.PerReplica,
            response.From,
            response.To,
            isFirstRun,
            latencySeries,
            vitalsPoints);
    }

    private static OpsHealthLatencyTrendSeriesView MapLatencySeries(OpsHealthHistoryLatencySeriesResponse series)
    {
        var points = (series.Points ?? [])
            .OrderBy(p => p.BucketStart)
            .Select(p => new OpsHealthLatencyTrendPointView(
                p.BucketStart, p.P50Ms, p.P95Ms, p.P99Ms, p.ErrorRate, p.RequestCount, p.ErrorCount))
            .ToArray();

        var hasBreach = HasBreach(points);

        return new OpsHealthLatencyTrendSeriesView(
            string.IsNullOrWhiteSpace(series.Protocol) ? "(unknown)" : series.Protocol!,
            series.ReplicaId,
            points,
            hasBreach);
    }

    private static OpsHealthVitalsTrendPointView MapVitalsPoint(OpsHealthHistoryVitalsPointResponse point) => new(
        point.BucketStart,
        point.ReplicaId,
        string.IsNullOrWhiteSpace(point.OverallStatus) ? "unknown" : point.OverallStatus!,
        point.GpQueueTotal,
        point.AlertPending,
        point.AlertDeadLettered,
        point.DbPoolUtilization,
        point.DbActiveConnections,
        point.CacheHitRatio,
        point.ErrorRate);
}
