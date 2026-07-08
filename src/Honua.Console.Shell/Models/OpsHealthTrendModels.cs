using Honua.Console.Contracts;

namespace Honua.Console.Shell.Models;

/// <summary>
/// The Ops Health trend view (console#288): the cluster-aggregated history mapped into
/// per-protocol serving-latency series and vitals points, ready for the shared
/// <c>ChartPreview</c> component's Vega-Lite specs (<see cref="Services.OpsHealthTrendCharts"/>).
/// Produced from the live server history read — never fabricated (Console Patterns Charter
/// section 11). <see cref="IsFirstRun"/> distinguishes a genuinely empty rollup store (a fresh
/// install, or a server too young to have any samples yet) from a normal empty window, so the
/// page can render the "collecting baseline" skeleton instead of a generic empty state (2026-07-07
/// addendum item 4).
/// </summary>
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

/// <summary>
/// One serving-latency trend series for a protocol (and, when <c>perReplica</c> was requested,
/// one replica). <see cref="HasBreach"/> mirrors the same conservative SLO heuristic the
/// snapshot table row status uses (<see cref="Services.OpsHealthDataSource"/>), so a breach badge
/// on the trend chart never disagrees with the at-a-glance table above it.
/// </summary>
public sealed record OpsHealthLatencyTrendSeriesView(
    string Protocol,
    string? ReplicaId,
    IReadOnlyList<OpsHealthLatencyTrendPointView> Points,
    bool HasBreach)
{
    public string SeriesLabel => ReplicaId is null ? Protocol : $"{Protocol} ({ReplicaId})";
}

/// <summary>One serving-latency time-series point (a history bucket, or a live-band snapshot).</summary>
public sealed record OpsHealthLatencyTrendPointView(
    DateTimeOffset BucketStart,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    double ErrorRate,
    long RequestCount,
    long ErrorCount);

/// <summary>
/// One ops-vitals time-series point (GP queue depth, alert-dispatch backlog, database vitals).
/// <see cref="ReplicaId"/> is <see langword="null"/> for a cluster-merged point.
/// </summary>
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

/// <summary>
/// Pure mapping from the wire <see cref="OpsHealthHistoryResponse"/> onto the trend view
/// (console#288). Kept independent of <see cref="Services.OpsHealthDataSource"/>'s snapshot
/// mapper so each is unit-testable on its own, mirroring how <c>OpsHealthDataSource.Map</c> is
/// already tested as a pure static method.
/// </summary>
public static class OpsHealthTrendMapper
{
    // Mirrors OpsHealthDataSource's serving-latency SLO heuristic exactly (display-only; the
    // server does not label history rows either) so the trend chart's breach badge never
    // disagrees with the at-a-glance snapshot table. Public so the page code-behind can apply the
    // same test when extending a series with live-band points (which arrive after this mapper has
    // already run over the history response).
    public const double P95BreachMs = 1000.0;
    public const double ErrorRateWarn = 0.01;

    /// <summary>Whether any point in the sequence breaches the shared serving-latency SLO heuristic.</summary>
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
