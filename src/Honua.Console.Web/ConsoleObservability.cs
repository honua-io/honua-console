using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Honua.Console.Web;

/// <summary>
/// OpenTelemetry wiring for the Console web host (honua-console#279 PA-234). The console previously
/// exported zero traces and zero metrics, so a request that crossed into honua-server (or stalled on the
/// map proxy) was invisible to any collector. This registers AspNetCore + HttpClient instrumentation for
/// both traces and metrics plus the console's own map-proxy meter, and exports over OTLP using the
/// standard <c>OTEL_EXPORTER_OTLP_*</c> environment variables.
///
/// It is DEFAULT-SAFE: when no <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is configured no exporter is attached,
/// so the SDK collects into no listener and stays effectively a no-op (matching honua-server's
/// endpoint-gated exporter convention in <c>Honua.ServiceDefaults</c>). Nothing leaves the process and no
/// collector connection is attempted unless a deployment opts in via the standard env vars.
/// </summary>
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
