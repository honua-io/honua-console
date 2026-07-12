using System.Diagnostics.Metrics;

namespace Honua.Console.Web;

/// <summary>
/// Metrics for the map-preview BFF proxy — the hottest console path (vector tiles + feature rows are
/// fetched on every map pan/zoom). Previously this path emitted no telemetry at all (honua-console#279
/// PA-235): an upstream honua-server fault surfaced to the browser as a bare status code with nothing
/// recorded server-side. This meter counts proxied requests and upstream failures per endpoint so a
/// deployment can alert on a rising proxy error rate. The instruments are no-ops until a
/// <see cref="System.Diagnostics.Metrics.MeterListener"/> / OpenTelemetry MeterProvider subscribes to
/// <see cref="MeterName"/> (wired in <c>Program.cs</c> when an OTLP endpoint is configured), so this is
/// zero-cost and default-safe when no collector is attached.
/// </summary>
internal static class ConsoleMapProxyTelemetry
{
    public const string MeterName = "Honua.Console.MapProxy";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> RequestCounter =
        Meter.CreateCounter<long>("honua.console.map_proxy.requests", unit: "{request}",
            description: "Map-proxy upstream requests to honua-server, tagged by endpoint and outcome.");

    private static readonly Counter<long> UpstreamFailureCounter =
        Meter.CreateCounter<long>("honua.console.map_proxy.upstream_failures", unit: "{failure}",
            description: "Map-proxy upstream requests that failed (non-success status or transport fault).");

    /// <summary>Records a completed upstream request and its status code for the given endpoint.</summary>
    public static void RecordResponse(string endpoint, int statusCode)
    {
        var succeeded = statusCode is >= 200 and < 400;
        RequestCounter.Add(1,
            new KeyValuePair<string, object?>("endpoint", endpoint),
            new KeyValuePair<string, object?>("status_code", statusCode),
            new KeyValuePair<string, object?>("outcome", succeeded ? "success" : "upstream_error"));

        if (!succeeded)
        {
            UpstreamFailureCounter.Add(1,
                new KeyValuePair<string, object?>("endpoint", endpoint),
                new KeyValuePair<string, object?>("status_code", statusCode),
                new KeyValuePair<string, object?>("reason", "status"));
        }
    }

    /// <summary>Records an upstream request that faulted before yielding a status (transport/timeout).</summary>
    public static void RecordTransportFault(string endpoint)
    {
        RequestCounter.Add(1,
            new KeyValuePair<string, object?>("endpoint", endpoint),
            new KeyValuePair<string, object?>("outcome", "transport_fault"));
        UpstreamFailureCounter.Add(1,
            new KeyValuePair<string, object?>("endpoint", endpoint),
            new KeyValuePair<string, object?>("reason", "transport"));
    }
}
