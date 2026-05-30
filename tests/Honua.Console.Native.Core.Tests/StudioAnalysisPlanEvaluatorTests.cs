using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Host-independent coverage for the analysis builder pre-submit gate and DAG/pipeline projection
/// (honua-console#53). Pins that a plan needs a title, method, a bound input, an output-schema field,
/// and a compute estimate before submit, and that the pipeline projects load nodes feeding the method node.
/// </summary>
public sealed class StudioAnalysisPlanEvaluatorTests
{
    [Fact]
    public void Evaluate_EmptyPlan_ListsEveryUnmetRequirement()
    {
        var plan = new StudioAnalysisPlanEditor { Method = string.Empty };

        var readiness = StudioAnalysisPlanEvaluator.Evaluate(plan);

        Assert.False(readiness.CanSubmit);
        Assert.Contains(readiness.UnmetRequirements, r => r.Contains("title", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(readiness.UnmetRequirements, r => r.Contains("method", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(readiness.UnmetRequirements, r => r.Contains("input", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(readiness.UnmetRequirements, r => r.Contains("output", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(readiness.UnmetRequirements, r => r.Contains("estimate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_MissingEstimate_StillGatesSubmit()
    {
        var plan = ReadyPlan();
        plan.Estimate = null;

        var readiness = StudioAnalysisPlanEvaluator.Evaluate(plan);

        Assert.False(readiness.CanSubmit);
        Assert.Contains(readiness.UnmetRequirements, r => r.Contains("estimate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_CompletePlan_CanSubmit()
    {
        var readiness = StudioAnalysisPlanEvaluator.Evaluate(ReadyPlan());

        Assert.True(readiness.CanSubmit);
        Assert.Empty(readiness.UnmetRequirements);
    }

    [Fact]
    public void Evaluate_PublishedPlan_IsTerminalAndCannotResubmit()
    {
        var plan = ReadyPlan();
        plan.Status = HonuaAnalysisStatuses.Published;

        var readiness = StudioAnalysisPlanEvaluator.Evaluate(plan);

        Assert.False(readiness.CanSubmit);
        Assert.Contains(readiness.UnmetRequirements, r => r.Contains("published", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildPipeline_MultipleBoundInputs_FeedTheMethodNode()
    {
        var plan = ReadyPlan();
        plan.Inputs.Add(new StudioAnalysisInputEditor { Role = "mask", ServiceId = "boundaries", LayerId = 1 });
        plan.Inputs.Add(new StudioAnalysisInputEditor { Role = "unbound", ServiceId = string.Empty, LayerId = 0 });

        var pipeline = StudioAnalysisPlanEvaluator.BuildPipeline(plan);

        // Two bound inputs yield two load nodes; the unbound input is skipped.
        var loadNodes = pipeline.Where(n => n.Method == "load").ToList();
        Assert.Equal(2, loadNodes.Count);

        var methodNode = Assert.Single(pipeline, n => n.NodeId == "method");
        Assert.Equal(plan.Method, methodNode.Method);
        Assert.Equal(loadNodes.Select(n => n.NodeId).OrderBy(id => id), methodNode.DependsOn.OrderBy(id => id));
    }

    private static StudioAnalysisPlanEditor ReadyPlan()
    {
        var plan = new StudioAnalysisPlanEditor
        {
            Title = "Hydrant buffer",
            Method = "buffer",
            Estimate = new StudioAnalysisComputeEstimate(12.5, 4_200, "standard")
        };
        plan.Inputs.Add(new StudioAnalysisInputEditor { Role = "primary", ServiceId = "hydrants", LayerId = 0 });
        plan.OutputSchema.Add(new StudioAnalysisOutputFieldEditor { Name = "buffer_distance", Type = "double" });
        return plan;
    }
}
