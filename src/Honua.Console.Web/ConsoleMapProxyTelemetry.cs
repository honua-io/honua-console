using System.Diagnostics.Metrics;

namespace Honua.Console.Web;

// These instruments are no-ops until a meter listener subscribes.
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
