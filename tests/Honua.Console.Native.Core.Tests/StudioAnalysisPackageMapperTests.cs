using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Host-independent coverage for the analysis plan-card &lt;-&gt; honua-server analysis content mapper
/// (honua-console#53). Asserts the authored plan lowers into a real server package document (intent, DAG
/// steps, requested artifacts) and that a loaded server version round-trips back into editor state without
/// losing the method, inputs, parameters, or output schema.
/// </summary>
public sealed class StudioAnalysisPackageMapperTests
{
    [Fact]
    public void ToPackageContent_LowersPlanIntoServerDagWithLoadAndMethodSteps()
    {
        var plan = BuildPlan();

        var content = StudioAnalysisPackageMapper.ToPackageContent(plan);

        var loadStep = Assert.Single(
            content.Plan.Steps,
            s => s.Kind == HonuaAnalysisPlanStepKinds.QueryFeatures);
        Assert.Equal("hydrants", loadStep.Inputs["serviceId"]);
        Assert.Equal("2", loadStep.Inputs["layerId"]);

        var methodStep = Assert.Single(
            content.Plan.Steps,
            s => s.Kind == HonuaAnalysisPlanStepKinds.Geoprocess);
        Assert.Equal("buffer", methodStep.ProcessId);
        // The method step depends on the load step, so the DAG is wired (not a flat list).
        Assert.Contains(loadStep.StepId, methodStep.DependsOn);
        Assert.Equal("250", methodStep.Inputs["distanceMeters"]);

        // The requested output content family becomes a requested server artifact kind (layer -> FeatureLayer).
        Assert.Contains(HonuaArtifactKinds.FeatureLayer, content.RequestedArtifacts);
        Assert.Equal("Buffer hydrants by 250m", content.Intent!.Goal);
    }

    [Fact]
    public void ToPackageContent_BlankGoal_FallsBackToTitle()
    {
        var plan = BuildPlan();
        plan.Goal = string.Empty;

        var content = StudioAnalysisPackageMapper.ToPackageContent(plan);

        Assert.Equal("Hydrant buffer", content.Intent!.Goal);
    }

    [Fact]
    public void ToEditorState_RoundTripsTitleMethodInputsAndSchema()
    {
        var plan = BuildPlan();
        var content = StudioAnalysisPackageMapper.ToPackageContent(plan);
        var response = new HonuaAnalysisContentVersionResponse
        {
            Item = new HonuaAnalysisContentItem { ItemId = "analysis-7", Name = "n", Title = "Hydrant buffer" },
            Version = new HonuaAnalysisContentVersion
            {
                ItemId = "analysis-7",
                Version = 2,
                ContentHash = "hash-2",
                AnalysisPackage = content
            }
        };

        var restored = StudioAnalysisPackageMapper.ToEditorState(response);

        Assert.Equal("analysis-7", restored.AnalysisId);
        Assert.Equal(2, restored.Version);
        Assert.Equal("buffer", restored.Method);
        Assert.Equal("standard", restored.ComputeProfile);
        Assert.Equal("layer", restored.OutputContentType);
        Assert.Contains(restored.Inputs, i => i.ServiceId == "hydrants" && i.LayerId == 2);
        Assert.Contains(restored.Parameters, p => p.Name == "distanceMeters" && p.Value == "250");
        Assert.Contains(restored.OutputSchema, f => f.Name == "buffer_distance" && f.Type == "double");
    }

    [Theory]
    [InlineData(HonuaArtifactKinds.FeatureLayer, "layer", "content")]
    [InlineData(HonuaArtifactKinds.Scalar, "report", "dashboard")]
    [InlineData(HonuaArtifactKinds.Table, "report", "workflow")]
    public void ResolveDownstreamTargets_OffersFamiliesAppropriateToArtifactKind(
        string artifactKind,
        string expectedFirst,
        string expectedAlso)
    {
        var targets = StudioAnalysisPackageMapper.ResolveDownstreamTargets(artifactKind, expectedFirst);

        Assert.Contains(expectedFirst, targets, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(expectedAlso, targets, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveDownstreamTargets_ScalarDoesNotOfferLayer()
    {
        var targets = StudioAnalysisPackageMapper.ResolveDownstreamTargets(HonuaArtifactKinds.Scalar, "report");

        // A scalar value cannot become a map layer; the panel must not claim an impossible binding.
        Assert.DoesNotContain("layer", targets, StringComparer.OrdinalIgnoreCase);
    }

    private static StudioAnalysisPlanEditor BuildPlan()
    {
        var plan = new StudioAnalysisPlanEditor
        {
            AnalysisId = "analysis-7",
            Version = 1,
            Title = "Hydrant buffer",
            Goal = "Buffer hydrants by 250m",
            Method = "buffer",
            ComputeProfile = "standard",
            OutputContentType = "layer"
        };
        plan.Inputs.Add(new StudioAnalysisInputEditor { Role = "primary", ServiceId = "hydrants", LayerId = 2 });
        plan.Parameters.Add(new StudioAnalysisParameterEditor { Name = "distanceMeters", Value = "250" });
        plan.OutputSchema.Add(new StudioAnalysisOutputFieldEditor { Name = "buffer_distance", Type = "double" });
        return plan;
    }
}
