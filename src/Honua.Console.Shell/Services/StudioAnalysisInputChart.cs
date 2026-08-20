using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Builds the live "data preview" graph for an authored analysis plan: a Vega-Lite count-by-field bar
/// chart of the REAL rows of the analysis's first bound input layer (the data the analysis runs over),
/// plus the console features-proxy URL that feeds it. The analysis input editor carries a service+layer
/// binding but no field list, so the chart's dimension is resolved client-side from the real rows (the
/// "__auto__" sentinel — see chart-preview.js). The chart only appears when an input is bound to a real
/// service+layer; never fabricated data (no-mock, Charter §11).
/// </summary>
public static class StudioAnalysisInputChart
{
    public const string VegaLiteSchema = "https://vega.github.io/schema/vega-lite/v6.json";

    // Cached once rather than allocated per BuildSpec() call. A fresh JsonSerializerOptions instance
    // per serialization bypasses the type-metadata cache and reallocates on every chart render
    // (honua-console#279 PA-238). JsonSerializerOptions is thread-safe for read after first use.
    private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };

    /// <summary>The first input bound to a real service+layer, or null when none is bound.</summary>
    public static StudioAnalysisInputEditor? BoundInput(StudioAnalysisPlanEditor plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.Inputs.FirstOrDefault(input =>
            !string.IsNullOrWhiteSpace(input.ServiceId) && input.LayerId >= 0
            && !string.IsNullOrWhiteSpace(input.ServiceId));
    }

    /// <summary>True when the plan has at least one input bound to a real service+layer.</summary>
    public static bool IsBound(StudioAnalysisPlanEditor plan) => BoundInput(plan) is not null;

    /// <summary>
    /// The console features-proxy URL serving the first bound input layer's real rows
    /// (/map-proxy/features/{serviceId}/{layerId}), or null when no input is bound.
    /// </summary>
    public static string? FeaturesUrl(StudioAnalysisPlanEditor plan)
    {
        var input = BoundInput(plan);
        if (input is null)
        {
            return null;
        }

        var service = Uri.EscapeDataString(input.ServiceId);
        var layer = input.LayerId.ToString(CultureInfo.InvariantCulture);
        return $"/map-proxy/features/{service}/{layer}";
    }

    /// <summary>
    /// Builds the Vega-Lite count-by-dimension bar-chart spec for the bound input's real data. The
    /// dimension field is the "__auto__" sentinel — chart-preview.js resolves it from the real rows since
    /// the analysis input carries no field list. Returns null when no input is bound (nothing to chart).
    /// </summary>
    public static string? BuildSpec(StudioAnalysisPlanEditor plan)
    {
        if (!IsBound(plan))
        {
            return null;
        }

        var spec = new JsonObject
        {
            ["$schema"] = VegaLiteSchema,
            ["mark"] = new JsonObject { ["type"] = "bar", ["tooltip"] = true },
            ["encoding"] = new JsonObject
            {
                ["x"] = new JsonObject
                {
                    ["field"] = "__auto__",
                    ["type"] = "nominal",
                    ["title"] = "__auto__",
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
