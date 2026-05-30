using System.Globalization;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Pure Console-side runtime/cost projection for the analysis plan card. honua-server#1182 exposes no
/// estimate route, so until it does the builder computes a transparent local estimate from the authored
/// plan shape (number of bound inputs, parameters, output fields, and the selected compute profile). This
/// is deliberately a labelled local projection — it is not server data and is surfaced alongside an
/// explicit "estimate route pending" capability state. It exists so the operator still reviews runtime and
/// cost before submit (AC#2). Swap for the server estimate the moment honua-server adds the route.
/// </summary>
public static class StudioAnalysisEstimator
{
    private const long BaseFeaturesPerInput = 1_500;
    private const double BaseRuntimeSeconds = 4.0;

    public static StudioAnalysisComputeEstimate Estimate(StudioAnalysisPlanEditor plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var boundInputs = plan.Inputs.Count(input => !string.IsNullOrWhiteSpace(input.ServiceId));
        var parameterCount = plan.Parameters.Count(p => !string.IsNullOrWhiteSpace(p.Name));
        var outputFieldCount = plan.OutputSchema.Count(f => !string.IsNullOrWhiteSpace(f.Name));

        var estimatedFeatures = Math.Max(1, boundInputs) * BaseFeaturesPerInput;

        var profileFactor = plan.ComputeProfile switch
        {
            "high-memory" => 0.7,
            "gpu" => 0.45,
            _ => 1.0
        };

        // Runtime grows with the data volume, the method's parameter complexity, and the output width, then
        // is scaled down by the worker class. A larger feature count and more parameters cost more time.
        var runtime = BaseRuntimeSeconds
            + (estimatedFeatures / 1_000.0)
            + (parameterCount * 0.75)
            + (outputFieldCount * 0.5);
        runtime *= profileFactor;
        runtime = Math.Round(Math.Max(BaseRuntimeSeconds * profileFactor, runtime), 1);

        var credits = Math.Round((runtime / 60.0) * CreditRate(plan.ComputeProfile), 3);
        var costNote = $"~{credits.ToString("0.###", CultureInfo.InvariantCulture)} compute credit"
            + (credits == 1 ? string.Empty : "s")
            + " (local estimate)";

        return new StudioAnalysisComputeEstimate(runtime, estimatedFeatures, plan.ComputeProfile, costNote);
    }

    private static double CreditRate(string computeProfile) => computeProfile switch
    {
        "high-memory" => 2.0,
        "gpu" => 4.0,
        _ => 1.0
    };
}
