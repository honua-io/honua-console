using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Honua.Console.Web;

// No exporter is attached unless OTEL_EXPORTER_OTLP_ENDPOINT is set, so nothing leaves the process.
internal static class ConsoleObservability
{
    internal const string ServiceName = "honua-console-web";

    public static WebApplicationBuilder AddConsoleObservability(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Honour the standard OTLP env var (or its Honua-config alias) as the single opt-in signal.
        var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        var useOtlp = !string.IsNullOrWhiteSpace(otlpEndpoint);

        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: ServiceName,
                serviceVersion: typeof(ConsoleObservability).Assembly.GetName().Version?.ToString(),
                serviceInstanceId: Environment.MachineName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddMeter(ConsoleMapProxyTelemetry.MeterName));

        if (useOtlp)
        {
            // Route OTel logs to the same collector, matching honua-server's ConfigureOpenTelemetry.
            builder.Logging.AddOpenTelemetry(logging =>
            {
                logging.IncludeFormattedMessage = true;
                logging.IncludeScopes = true;
            });

            // UseOtlpExporter wires OTLP for traces, metrics, and logs together from the standard
            // OTEL_EXPORTER_OTLP_* environment variables (endpoint, headers, protocol).
            otel.UseOtlpExporter();
        }

        return builder;
    }
}
