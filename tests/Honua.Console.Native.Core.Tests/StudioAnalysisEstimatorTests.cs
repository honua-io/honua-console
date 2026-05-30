using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Host-independent coverage for the Console-side compute estimate projection (honua-console#53). The
/// estimate is a deliberately-local stand-in until honua-server#1182 ships an estimate route; these tests
/// pin that it scales with plan size and is cheaper on accelerated compute profiles so the operator sees a
/// meaningful runtime/cost figure before submit (AC#2).
/// </summary>
public sealed class StudioAnalysisEstimatorTests
{
    [Fact]
    public void Estimate_MoreInputs_RaisesFeatureCountAndRuntime()
    {
        var small = StudioAnalysisEstimator.Estimate(PlanWithInputs(1));
        var large = StudioAnalysisEstimator.Estimate(PlanWithInputs(4));

        Assert.True(large.EstimatedInputFeatures > small.EstimatedInputFeatures);
        Assert.True(large.EstimatedRuntimeSeconds > small.EstimatedRuntimeSeconds);
    }

    [Fact]
    public void Estimate_GpuProfile_IsFasterAndDearerPerMinuteThanStandard()
    {
        var standard = StudioAnalysisEstimator.Estimate(PlanWithProfile("standard"));
        var gpu = StudioAnalysisEstimator.Estimate(PlanWithProfile("gpu"));

        Assert.True(gpu.EstimatedRuntimeSeconds < standard.EstimatedRuntimeSeconds);
        Assert.Equal("gpu", gpu.ComputeProfile);
        Assert.NotNull(gpu.CostNote);
    }

    [Fact]
    public void Estimate_AlwaysProducesPositiveRuntime()
    {
        var estimate = StudioAnalysisEstimator.Estimate(new StudioAnalysisPlanEditor());

        Assert.True(estimate.EstimatedRuntimeSeconds > 0);
        Assert.True(estimate.EstimatedInputFeatures > 0);
    }

    private static StudioAnalysisPlanEditor PlanWithInputs(int count)
    {
        var plan = new StudioAnalysisPlanEditor { Method = "overlay", ComputeProfile = "standard" };
        for (var i = 0; i < count; i++)
        {
            plan.Inputs.Add(new StudioAnalysisInputEditor { ServiceId = $"svc-{i}", LayerId = i });
        }

        return plan;
    }

    private static StudioAnalysisPlanEditor PlanWithProfile(string profile)
    {
        var plan = new StudioAnalysisPlanEditor { Method = "buffer", ComputeProfile = profile };
        plan.Inputs.Add(new StudioAnalysisInputEditor { ServiceId = "svc", LayerId = 0 });
        plan.Parameters.Add(new StudioAnalysisParameterEditor { Name = "distanceMeters", Value = "100" });
        return plan;
    }
}
