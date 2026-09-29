using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Builds the live "result" visualization for an authored saved query: a Vega-Lite spec (a count-by-field
/// bar chart — the query family's graph) plus the console features-proxy URL that feeds it the bound
/// layer's REAL rows. Both are deterministic and pure so the result render is unit-testable without a
/// server, and the chart only ever appears when the query carries a real source binding
/// (ServiceName + LayerId) — never fabricated data (no-mock, Charter §11).
/// </summary>
public static class StudioQueryResultChart
{
    public const string VegaLiteSchema = "https://vega.github.io/schema/vega-lite/v6.json";

    // Cached once rather than allocated per BuildSpec() call. A fresh JsonSerializerOptions instance
    // per serialization bypasses the type-metadata cache and reallocates on every chart render
    // (honua-console#279 PA-238). JsonSerializerOptions is thread-safe for read after first use.
    private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };

    /// <summary>
    /// True when the query is bound to a real source (service + layer) so its result can be fetched and
    /// charted. An unbound draft keeps the schematic placeholder.
    /// </summary>
    public static bool IsBound(StudioQueryEditor query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return !string.IsNullOrWhiteSpace(query.ServiceName) && query.LayerId >= 0
            && !string.IsNullOrWhiteSpace(query.ServiceName);
    }

    /// <summary>
    /// The console features-proxy URL serving the bound layer's real rows
    /// (/map-proxy/features/{serviceName}/{layerId}), or null when the query is unbound.
    /// </summary>
    public static string? FeaturesUrl(StudioQueryEditor query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query.ServiceName))
        {
            return null;
        }

        var service = Uri.EscapeDataString(query.ServiceName);
        var layer = query.LayerId.ToString(CultureInfo.InvariantCulture);
        return $"/map-proxy/features/{service}/{layer}";
    }

    /// <summary>
    /// Picks the dimension field to group the result by: the first projected non-id field, else the first
    /// projected field, else a conventional "name" (the server's common display field). The chart counts
    /// rows per distinct value of this field.
    /// </summary>
    public static string DimensionField(StudioQueryEditor query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var projected = query.OutFields.Where(f => !string.IsNullOrWhiteSpace(f)).ToList();
        var preferred = projected.FirstOrDefault(f =>
            !f.Equals("id", StringComparison.OrdinalIgnoreCase)
            && !f.Equals("objectid", StringComparison.OrdinalIgnoreCase));
        return preferred ?? projected.FirstOrDefault() ?? "name";
    }

    /// <summary>
    /// Builds the Vega-Lite count-by-dimension bar-chart spec for the query result. The chart's encoding
    /// binds to the REAL field name so the live rows (fetched from <see cref="FeaturesUrl"/>) plot
    /// correctly. Returns null when the query is unbound (no real result to chart).
    /// </summary>
    public static string? BuildSpec(StudioQueryEditor query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!IsBound(query))
        {
            return null;
        }

        var dimension = DimensionField(query);
        var spec = new JsonObject
        {
            ["$schema"] = VegaLiteSchema,
            ["mark"] = new JsonObject { ["type"] = "bar", ["tooltip"] = true },
            ["encoding"] = new JsonObject
            {
                ["x"] = new JsonObject
                {
                    ["field"] = dimension,
                    ["type"] = "nominal",
                    ["title"] = dimension,
                    ["sort"] = "-y",
                },
                ["y"] = new JsonObject
                {
                    ["aggregate"] = "count",
                    ["type"] = "quantitative",
                    ["title"] = "features",
                },
            },
        };

        return spec.ToJsonString(IndentedOptions);
    }
}
