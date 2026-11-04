using System.Text.Json;
using System.Text.Json.Nodes;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

// An empty series returns null so the caller keeps the no-backend schematic.
public static class OpsHealthTrendCharts
{
    public const string VegaLiteSchema = "https://vega.github.io/schema/vega-lite/v6.json";

    public static string? BuildLatencySpec(OpsHealthLatencyTrendSeriesView series, DateTimeOffset? seamAt)
    {
        ArgumentNullException.ThrowIfNull(series);
        var rows = series.Points.SelectMany(p => new (DateTimeOffset BucketStart, string Series, double Value)[]
        {
            (p.BucketStart, "p50", p.P50Ms),
            (p.BucketStart, "p95", p.P95Ms),
            (p.BucketStart, "p99", p.P99Ms),
        }).ToArray();

        return BuildLineSpec(rows, "Latency (ms)", seamAt);
    }

    public static string? BuildErrorRateSpec(OpsHealthLatencyTrendSeriesView series, DateTimeOffset? seamAt)
    {
        ArgumentNullException.ThrowIfNull(series);
        var rows = series.Points
            .Select(p => (p.BucketStart, Series: "error rate", Value: p.ErrorRate * 100.0))
            .ToArray();

        return BuildLineSpec(rows, "Error rate (%)", seamAt);
    }

    public static string? BuildGpQueueSpec(IReadOnlyList<OpsHealthVitalsTrendPointView> points, DateTimeOffset? seamAt)
    {
        ArgumentNullException.ThrowIfNull(points);
        var rows = points
            .Select(p => (p.BucketStart, Series: p.ReplicaId ?? "cluster", Value: (double)p.GpQueueTotal))
            .ToArray();

        return BuildLineSpec(rows, "Active jobs", seamAt);
    }

    public static string? BuildAlertBacklogSpec(IReadOnlyList<OpsHealthVitalsTrendPointView> points, DateTimeOffset? seamAt)
    {
        ArgumentNullException.ThrowIfNull(points);
        var rows = new List<(DateTimeOffset BucketStart, string Series, double Value)>();
        foreach (var point in points)
        {
            var prefix = point.ReplicaId is null ? string.Empty : $"{point.ReplicaId} ";
            if (point.AlertPending is { } pending)
            {
                rows.Add((point.BucketStart, $"{prefix}pending", pending));
            }

            if (point.AlertDeadLettered is { } deadLettered)
            {
                rows.Add((point.BucketStart, $"{prefix}dead-lettered", deadLettered));
            }
        }

        return BuildLineSpec(rows, "Count", seamAt);
    }

    private static string? BuildLineSpec(
        IReadOnlyList<(DateTimeOffset BucketStart, string Series, double Value)> rows,
        string yTitle,
        DateTimeOffset? seamAt)
    {
        if (rows.Count == 0)
        {
            return null;
        }

        var values = new JsonArray();
        foreach (var row in rows)
        {
            values.Add(new JsonObject
            {
                ["bucketStart"] = row.BucketStart.ToUniversalTime().ToString("o"),
                ["series"] = row.Series,
                ["value"] = row.Value,
            });
        }

        var layers = new JsonArray
        {
            new JsonObject
            {
                ["mark"] = new JsonObject { ["type"] = "line", ["point"] = true, ["tooltip"] = true },
                ["encoding"] = new JsonObject
                {
                    ["x"] = new JsonObject { ["field"] = "bucketStart", ["type"] = "temporal", ["title"] = "Time" },
                    ["y"] = new JsonObject { ["field"] = "value", ["type"] = "quantitative", ["title"] = yTitle },
                    ["color"] = new JsonObject { ["field"] = "series", ["type"] = "nominal", ["title"] = "Series" },
                },
            },
        };

        var spec = new JsonObject
        {
            ["$schema"] = VegaLiteSchema,
            ["data"] = new JsonObject { ["values"] = values },
            ["layer"] = layers,
        };

        if (seamAt is { } seam)
        {
            layers.Add(new JsonObject
            {
                ["data"] = new JsonObject
                {
                    ["values"] = new JsonArray
                    {
                        new JsonObject { ["at"] = seam.ToUniversalTime().ToString("o") },
                    },
                },
                ["mark"] = new JsonObject { ["type"] = "rule", ["strokeDash"] = new JsonArray { 4, 4 }, ["color"] = "#888888" },
                ["encoding"] = new JsonObject
                {
                    ["x"] = new JsonObject { ["field"] = "at", ["type"] = "temporal" },
                },
            });
        }

        return spec.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
