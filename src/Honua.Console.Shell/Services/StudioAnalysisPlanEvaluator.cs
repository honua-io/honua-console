using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Console-side pre-submit gate and DAG/pipeline projection for the analysis builder. Pure functions over
/// <see cref="StudioAnalysisPlanEditor"/> so the plan card can show what must be resolved before a job is
/// submitted, and so the pipeline view can be rendered from the authored plan. This is an authoring-surface
/// readiness check layered on top of (not a replacement for) the server's own validation: the server owns
/// final acceptance of an analysis job (honua-server#1182 / execution engine #681/#721/#724).
/// </summary>
public static class StudioAnalysisPlanEvaluator
{
    /// <summary>
    /// Evaluates whether the plan can be submitted. A plan needs a title, a method, at least one input,
    /// at least one output-schema field, and a fresh compute estimate (so the operator sees runtime/cost
    /// before submit, per AC#2). Published plans are terminal in the builder and cannot be re-submitted.
    /// </summary>
    public static StudioAnalysisSubmitReadiness Evaluate(StudioAnalysisPlanEditor plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var unmet = new List<string>();

        if (string.IsNullOrWhiteSpace(plan.Title))
        {
            unmet.Add("Give the analysis a title.");
        }

        if (string.IsNullOrWhiteSpace(plan.Method))
        {
            unmet.Add("Select an analysis method.");
        }

        if (!plan.Inputs.Any(input => !string.IsNullOrWhiteSpace(input.ServiceId)))
        {
            unmet.Add("Bind at least one input service/layer.");
        }

        if (!plan.OutputSchema.Any(field => !string.IsNullOrWhiteSpace(field.Name)))
        {
            unmet.Add("Define at least one output-schema field.");
        }

        if (plan.Estimate is null)
        {
            unmet.Add("Run a compute estimate to review runtime and cost before submit.");
        }

        if (plan.IsPublished)
        {
            unmet.Add("This analysis is published; reopen a draft to author a new version.");
        }

        return new StudioAnalysisSubmitReadiness(unmet.Count == 0, unmet);
    }

    /// <summary>
    /// Projects the authored plan to a DAG/pipeline view. The single-method case yields one node; a plan
    /// with inputs across multiple services yields per-input load nodes feeding the method node, so the
    /// multi-step pipeline is visible before submit (AC#2). Node ids are stable for a given plan shape.
    /// </summary>
    public static IReadOnlyList<StudioAnalysisPipelineNode> BuildPipeline(StudioAnalysisPlanEditor plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        // Prefer the real server-compiled plan steps when the plan was loaded/saved against honua-server, so
        // the DAG view reflects the execution plan the engine will run rather than a Console reconstruction.
        if (plan.ServerPipeline.Count > 0)
        {
            return plan.ServerPipeline;
        }

        var boundInputs = plan.Inputs
            .Where(input => !string.IsNullOrWhiteSpace(input.ServiceId))
            .ToList();

        var nodes = new List<StudioAnalysisPipelineNode>();
        var dependencies = new List<string>();

        for (var i = 0; i < boundInputs.Count; i++)
        {
            var input = boundInputs[i];
            var nodeId = $"input-{i}";
            dependencies.Add(nodeId);
            var role = string.IsNullOrWhiteSpace(input.Role) ? "input" : input.Role;
            nodes.Add(new StudioAnalysisPipelineNode(
                nodeId,
                $"{role}: {input.ServiceId}/layer {input.LayerId}",
                "load",
                []));
        }

        var method = string.IsNullOrWhiteSpace(plan.Method) ? "analysis" : plan.Method;
        nodes.Add(new StudioAnalysisPipelineNode(
            "method",
            string.IsNullOrWhiteSpace(plan.Title) ? method : plan.Title,
            method,
            dependencies));

        return nodes;
    }
}

/// <summary>Pre-submit readiness for the analysis plan card.</summary>
public sealed record StudioAnalysisSubmitReadiness(
    bool CanSubmit,
    IReadOnlyList<string> UnmetRequirements);
