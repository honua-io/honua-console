using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Pure-logic tests for the Ops Health trend charts (console#288): the history-response mapper,
/// the reconnect-seam live-band merge (idempotent append, cutoff dedup, trim-on-refresh), and the
/// Vega-Lite spec builder. None of these need a server or a hub connection — mirroring how
/// <c>OpsHealthDataSource.Map</c> and <c>OperateTimelineEntries</c> are tested as pure statics.
/// </summary>
public sealed class OpsHealthTrendModelTests
{
    [Fact]
    public void MapperProjectsLatencyAndVitalsSeriesInBucketOrder()
    {
        var response = new OpsHealthHistoryResponse
        {
            GeneratedAt = DateTimeOffset.Parse("2026-07-07T10:00:00Z"),
            Resolution = "5m",
            WindowSeconds = 3600,
            From = DateTimeOffset.Parse("2026-07-07T09:00:00Z"),
            To = DateTimeOffset.Parse("2026-07-07T10:00:00Z"),
            PerReplica = false,
            Latency =
            [
                new OpsHealthHistoryLatencySeriesResponse
                {
                    Protocol = "GeoServices",
                    Points =
                    [
                        new OpsHealthHistoryLatencyPointResponse
                        {
                            BucketStart = DateTimeOffset.Parse("2026-07-07T09:10:00Z"),
                            RequestCount = 10, ErrorCount = 0, ErrorRate = 0, P50Ms = 20, P95Ms = 40, P99Ms = 60, MaxMs = 80
                        },
                        new OpsHealthHistoryLatencyPointResponse
                        {
                            BucketStart = DateTimeOffset.Parse("2026-07-07T09:05:00Z"),
                            RequestCount = 5, ErrorCount = 0, ErrorRate = 0, P50Ms = 15, P95Ms = 30, P99Ms = 50, MaxMs = 60
                        }
                    ]
                }
            ],
            Vitals =
            [
                new OpsHealthHistoryVitalsPointResponse
                {
                    BucketStart = DateTimeOffset.Parse("2026-07-07T09:10:00Z"),
                    OverallStatus = "Healthy",
                    GpQueueTotal = 1,
                    GpQueueBreakdown = new Dictionary<string, int>(),
                    DbActiveConnections = 3,
                    CacheHitRatio = 0.9,
                    ErrorRate = 0.001
                }
            ]
        };

        var view = OpsHealthTrendMapper.Map(response, "Last hour");

        Assert.Equal("Last hour", view.RangeLabel);
        Assert.Equal("5m", view.Resolution);
        Assert.False(view.IsFirstRun);
        var series = Assert.Single(view.LatencySeries);
        Assert.Equal("GeoServices", series.Protocol);
        Assert.Equal(2, series.Points.Count);
        // Points must be ordered by bucket start even though the wire payload was not.
        Assert.True(series.Points[0].BucketStart < series.Points[1].BucketStart);
        Assert.False(series.HasBreach);
        Assert.Single(view.VitalsPoints);
    }

    [Fact]
    public void MapperFlagsFirstRunWhenHistoryIsEmpty()
    {
        var response = new OpsHealthHistoryResponse
        {
            GeneratedAt = DateTimeOffset.Parse("2026-07-07T10:00:00Z"),
            Resolution = "1m",
            WindowSeconds = 3600,
            From = DateTimeOffset.Parse("2026-07-07T09:00:00Z"),
            To = DateTimeOffset.Parse("2026-07-07T10:00:00Z"),
            PerReplica = false,
            Latency = [],
            Vitals = []
        };

        var view = OpsHealthTrendMapper.Map(response, "Last hour");

        Assert.True(view.IsFirstRun);
    }

    [Fact]
    public void MapperFlagsBreachOnHighP95OrErrorRate()
    {
        var response = new OpsHealthHistoryResponse
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            Resolution = "1m",
            WindowSeconds = 3600,
            From = DateTimeOffset.UtcNow.AddHours(-1),
            To = DateTimeOffset.UtcNow,
            PerReplica = false,
            Latency =
            [
                new OpsHealthHistoryLatencySeriesResponse
                {
                    Protocol = "OGC",
                    Points =
                    [
                        new OpsHealthHistoryLatencyPointResponse
                        {
                            BucketStart = DateTimeOffset.UtcNow,
                            RequestCount = 10, ErrorCount = 5, ErrorRate = 0.5, P50Ms = 20, P95Ms = 1500, P99Ms = 2000, MaxMs = 3000
                        }
                    ]
                }
            ],
            Vitals = []
        };

        var view = OpsHealthTrendMapper.Map(response, "Last hour");

        Assert.True(Assert.Single(view.LatencySeries).HasBreach);
    }

    [Fact]
    public void LiveBandAppendDropsPushesAlreadyCoveredByHistory()
    {
        var cutoff = DateTimeOffset.Parse("2026-07-07T10:00:00Z");
        var covered = SnapshotAt(cutoff.AddMinutes(-1));

        var result = OpsHealthLiveBand.Append([], covered, cutoff);

        Assert.Empty(result);
    }

    [Fact]
    public void LiveBandAppendIsIdempotentOnRepeatedPush()
    {
        var cutoff = DateTimeOffset.Parse("2026-07-07T10:00:00Z");
        var first = SnapshotAt(cutoff.AddSeconds(30));
        var duplicate = SnapshotAt(cutoff.AddSeconds(30)); // same second, at-least-once redelivery.

        var afterFirst = OpsHealthLiveBand.Append([], first, cutoff);
        var afterDuplicate = OpsHealthLiveBand.Append(afterFirst, duplicate, cutoff);

        Assert.Single(afterFirst);
        Assert.Single(afterDuplicate);
    }

    [Fact]
    public void LiveBandAppendKeepsDistinctPushesSortedByTime()
    {
        var cutoff = DateTimeOffset.Parse("2026-07-07T10:00:00Z");
        var later = SnapshotAt(cutoff.AddMinutes(2));
        var earlier = SnapshotAt(cutoff.AddMinutes(1));

        var band = OpsHealthLiveBand.Append([], later, cutoff);
        band = OpsHealthLiveBand.Append(band, earlier, cutoff);

        Assert.Equal(2, band.Count);
        Assert.True(band[0].GeneratedAt < band[1].GeneratedAt);
    }

    [Fact]
    public void LiveBandTrimToAfterDropsPointsSubsumedByAFreshHistoryFetch()
    {
        var cutoff = DateTimeOffset.Parse("2026-07-07T10:00:00Z");
        var band = OpsHealthLiveBand.Append([], SnapshotAt(cutoff.AddMinutes(1)), cutoff);
        band = OpsHealthLiveBand.Append(band, SnapshotAt(cutoff.AddMinutes(2)), cutoff);

        // A fresh history refresh now covers up to +90s: the +1min point is subsumed, the +2min
        // point still is not.
        var trimmed = OpsHealthLiveBand.TrimToAfter(band, cutoff.AddSeconds(90));

        var remaining = Assert.Single(trimmed);
        Assert.Equal(cutoff.AddMinutes(2), remaining.GeneratedAt);
    }

    [Fact]
    public void LiveBandProjectsLatencyPointsPerProtocolAndVitalsClusterMerged()
    {
        var snapshot = SnapshotAt(DateTimeOffset.Parse("2026-07-07T10:05:00Z"));

        var latencyPoints = OpsHealthLiveBand.ToLatencyPoints([snapshot], "GeoServices");
        var vitalsPoints = OpsHealthLiveBand.ToVitalsPoints([snapshot]);
        var protocols = OpsHealthLiveBand.DistinctProtocols([snapshot]);

        var point = Assert.Single(latencyPoints);
        Assert.Equal(42, point.P50Ms);
        var vitals = Assert.Single(vitalsPoints);
        Assert.Null(vitals.ReplicaId); // live push carries no per-replica breakdown.
        Assert.Contains("GeoServices", protocols);
    }

    [Fact]
    public void ChartBuilderReturnsNullWhenNoPointsExist()
    {
        var emptySeries = new OpsHealthLatencyTrendSeriesView("GeoServices", null, [], false);

        Assert.Null(OpsHealthTrendCharts.BuildLatencySpec(emptySeries, seamAt: null));
        Assert.Null(OpsHealthTrendCharts.BuildErrorRateSpec(emptySeries, seamAt: null));
        Assert.Null(OpsHealthTrendCharts.BuildGpQueueSpec([], seamAt: null));
        Assert.Null(OpsHealthTrendCharts.BuildAlertBacklogSpec([], seamAt: null));
    }

    [Fact]
    public void ChartBuilderEmitsAllThreePercentilesAndASeamRuleWhenRequested()
    {
        var series = new OpsHealthLatencyTrendSeriesView(
            "GeoServices",
            null,
            [new OpsHealthLatencyTrendPointView(DateTimeOffset.Parse("2026-07-07T09:00:00Z"), 10, 20, 30, 0.001, 100, 0)],
            false);
        var seam = DateTimeOffset.Parse("2026-07-07T10:00:00Z");

        var spec = OpsHealthTrendCharts.BuildLatencySpec(series, seam);

        Assert.NotNull(spec);
        Assert.Contains("\"p50\"", spec);
        Assert.Contains("\"p95\"", spec);
        Assert.Contains("\"p99\"", spec);
        Assert.Contains("\"rule\"", spec); // the seam annotation layer.
        // The seam timestamp is JSON-string-escaped by the default encoder (e.g. '+' -> "+"),
        // so match on the unescaped date/time portion rather than the raw ISO-8601 string.
        Assert.Contains("2026-07-07T10:00:00", spec, StringComparison.Ordinal);
    }

    [Fact]
    public void ChartBuilderOmitsSeamLayerWhenNoLiveBandIsContributing()
    {
        var series = new OpsHealthLatencyTrendSeriesView(
            "GeoServices",
            null,
            [new OpsHealthLatencyTrendPointView(DateTimeOffset.Parse("2026-07-07T09:00:00Z"), 10, 20, 30, 0.001, 100, 0)],
            false);

        var spec = OpsHealthTrendCharts.BuildLatencySpec(series, seamAt: null);

        Assert.NotNull(spec);
        Assert.DoesNotContain("\"rule\"", spec);
    }

    [Fact]
    public void AlertBacklogSpecOmitsMetricsWithNoValueRatherThanFabricatingZero()
    {
        var points = new[]
        {
            new OpsHealthVitalsTrendPointView(
                DateTimeOffset.Parse("2026-07-07T09:00:00Z"), null, "Healthy", 0, AlertPending: 3, AlertDeadLettered: null,
                DbPoolUtilization: null, DbActiveConnections: 0, CacheHitRatio: 0, ErrorRate: 0),
        };

        var spec = OpsHealthTrendCharts.BuildAlertBacklogSpec(points, seamAt: null);

        Assert.NotNull(spec);
        Assert.Contains("pending", spec);
        Assert.DoesNotContain("dead-lettered", spec);
    }

    private static OpsHealthSnapshotResponse SnapshotAt(DateTimeOffset generatedAt) => new()
    {
        GeneratedAt = generatedAt,
        OverallStatus = "Healthy",
        ServingLatency = new OpsServingLatencyResponse
        {
            WindowSeconds = 30,
            Protocols =
            [
                new OpsServingLatencyProtocolResponse
                {
                    Protocol = "GeoServices",
                    RequestCount = 5, ErrorCount = 0, ErrorRate = 0, P50Ms = 42, P95Ms = 80, P99Ms = 100, MaxMs = 120
                }
            ]
        },
        Geoprocessing = new OpsGpQueueResponse { TotalActive = 1, Available = true, Buckets = [] },
        AlertDispatch = new OpsAlertDispatchResponse
        {
            DispatcherRunning = true,
            DispatcherEnabled = true,
            StoragePollFailing = false,
            PendingCount = 0,
            DeadLetteredCount = 0
        },
        Database = new OpsDatabaseResponse
        {
            HasConnectionPoolData = true,
            ConnectionPoolUtilization = 0.1,
            ActiveConnections = 2,
            CacheHitRatio = 0.9,
            ErrorRate = 0.001
        }
    };
}
