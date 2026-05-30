using AngleSharp.Dom;
using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for the analysis builder page (<c>/studio/analysis</c>, honua-console#53).
/// Verifies the missing-binding surface, that an opened plan renders the plan card / DAG view, and that
/// Submit stays gated behind the pre-submit gate until a compute estimate is present (AC#2). Drives the
/// page through a fake <see cref="IStudioAnalysisPackageDataSource"/> rather than a mock server.
/// </summary>
public sealed class StudioAnalysisBuilderRenderTests
{
    [Fact]
    public void AnalysisBuilder_WhenBindingMissing_RendersNotBoundSurface()
    {
        var data = new FakeAnalysisDataSource
        {
            Workspace = new StudioAnalysisWorkspace([], [MissingBinding])
        };
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioAnalysisPackageDataSource>(data);

        var page = ctx.RenderComponent<StudioAnalysisBuilderPage>();

        page.WaitForAssertion(
            () => Assert.Contains("Analysis content lifecycle is not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("data-analysis-builder", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalysisBuilder_OpenReadyPlan_RendersPlanCardPipelineAndEnablesSubmit()
    {
        var data = new FakeAnalysisDataSource
        {
            Workspace = new StudioAnalysisWorkspace(
                [new StudioAnalysisPlanListItem("analysis-1", "Hydrant buffer", "buffer", "standard", 2, null, DateTimeOffset.UtcNow)],
                []),
            EditorLoad = new StudioAnalysisEditorLoad(ReadyPlan(), [])
        };
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioAnalysisPackageDataSource>(data);

        var page = ctx.RenderComponent<StudioAnalysisBuilderPage>();
        page.WaitForAssertion(() => FindButton(page, "Hydrant buffer"), TimeSpan.FromSeconds(5));
        FindButton(page, "Hydrant buffer").Click();

        page.WaitForAssertion(
            () => Assert.Contains("data-analysis-builder", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        // Plan card, compute estimate, and DAG/pipeline view are all visible before submit (AC#2).
        Assert.Contains("Pipeline view", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-analysis-pipeline", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-analysis-estimate", page.Markup, StringComparison.Ordinal);
        Assert.False(FindButton(page, "Submit job").HasAttribute("disabled"));
    }

    [Fact]
    public void AnalysisBuilder_OpenPlanWithoutEstimate_GatesSubmit()
    {
        var incomplete = ReadyPlan();
        incomplete.Estimate = null; // No estimate yet: the operator must review runtime/cost before submit.
        var data = new FakeAnalysisDataSource
        {
            Workspace = new StudioAnalysisWorkspace(
                [new StudioAnalysisPlanListItem("analysis-2", "No estimate", "buffer", "standard", 1, null, DateTimeOffset.UtcNow)],
                []),
            EditorLoad = new StudioAnalysisEditorLoad(incomplete, [])
        };
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioAnalysisPackageDataSource>(data);

        var page = ctx.RenderComponent<StudioAnalysisBuilderPage>();
        page.WaitForAssertion(() => FindButton(page, "No estimate"), TimeSpan.FromSeconds(5));
        FindButton(page, "No estimate").Click();

        page.WaitForAssertion(
            () => Assert.Contains("Resolve before submit", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.True(FindButton(page, "Submit job").HasAttribute("disabled"));
    }

    [Fact]
    public void AnalysisBuilder_OpenPlanWithResultArtifact_RendersArtifactPanelAndDownstreamTargets()
    {
        var plan = ReadyPlan();
        plan.SubmittedJob = new StudioAnalysisJobView(
            "job-1",
            "Succeeded",
            2,
            Artifact: new StudioAnalysisArtifactView(
                "artifact-1",
                "FeatureLayer",
                "Buffered hydrants",
                "https://server/artifacts/artifact-1",
                "application/geo+json",
                2048,
                "retained",
                ["layer", "content", "workflow"]));
        var data = new FakeAnalysisDataSource
        {
            Workspace = new StudioAnalysisWorkspace(
                [new StudioAnalysisPlanListItem("analysis-1", "Hydrant buffer", "buffer", "standard", 2, null, DateTimeOffset.UtcNow)],
                []),
            EditorLoad = new StudioAnalysisEditorLoad(plan, [])
        };
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioAnalysisPackageDataSource>(data);

        var page = ctx.RenderComponent<StudioAnalysisBuilderPage>();
        page.WaitForAssertion(() => FindButton(page, "Hydrant buffer"), TimeSpan.FromSeconds(5));
        FindButton(page, "Hydrant buffer").Click();

        page.WaitForAssertion(
            () => Assert.Contains("data-analysis-result", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        // The result-artifact panel surfaces the resolved artifact (AC#1) and the downstream content
        // families it can become (AC#3): content item, layer, report/dashboard input, or workflow input.
        Assert.Contains("data-analysis-artifact", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Buffered hydrants", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Map layer", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Workflow input", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalysisBuilder_OpenPlanWithFailedJob_RendersFailureClassification()
    {
        var plan = ReadyPlan();
        plan.SubmittedJob = new StudioAnalysisJobView(
            "job-2",
            "Failed",
            2,
            Failure: new StudioAnalysisJobFailureView("validationFailed", "The plan failed validation."));
        var data = new FakeAnalysisDataSource
        {
            Workspace = new StudioAnalysisWorkspace(
                [new StudioAnalysisPlanListItem("analysis-1", "Hydrant buffer", "buffer", "standard", 2, null, DateTimeOffset.UtcNow)],
                []),
            EditorLoad = new StudioAnalysisEditorLoad(plan, [])
        };
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioAnalysisPackageDataSource>(data);

        var page = ctx.RenderComponent<StudioAnalysisBuilderPage>();
        page.WaitForAssertion(() => FindButton(page, "Hydrant buffer"), TimeSpan.FromSeconds(5));
        FindButton(page, "Hydrant buffer").Click();

        page.WaitForAssertion(
            () => Assert.Contains("data-analysis-result-failure", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("validationFailed", page.Markup, StringComparison.Ordinal);
        // A failed job must NOT show a result-artifact binding.
        Assert.DoesNotContain("data-analysis-artifact", page.Markup, StringComparison.Ordinal);
    }

    private static IElement FindButton(IRenderedComponent<StudioAnalysisBuilderPage> page, string label) =>
        page.FindAll("button").First(button => button.TextContent.Contains(label, StringComparison.Ordinal));

    private static StudioAnalysisPlanEditor ReadyPlan()
    {
        var plan = new StudioAnalysisPlanEditor
        {
            AnalysisId = "analysis-1",
            Version = 2,
            Title = "Hydrant buffer",
            Method = "buffer",
            ComputeProfile = "standard",
            ETag = "etag-2",
            Estimate = new StudioAnalysisComputeEstimate(12.5, 4_200, "standard", "~0.01 credit")
        };
        plan.Inputs.Add(new StudioAnalysisInputEditor { Role = "primary", ServiceId = "hydrants", LayerId = 0 });
        plan.OutputSchema.Add(new StudioAnalysisOutputFieldEditor { Name = "buffer_distance", Type = "double" });
        return plan;
    }

    private static readonly StudioAnalysisCapabilityState MissingBinding = new(
        "Analysis builder",
        "Missing binding",
        "Honua:Server:BaseUrl",
        "Configure Honua:Server:BaseUrl so the analysis builder can bind the server-owned analysis content lifecycle.");

    private sealed class FakeAnalysisDataSource : IStudioAnalysisPackageDataSource
    {
        public StudioAnalysisWorkspace Workspace { get; set; } = new([], []);

        public StudioAnalysisEditorLoad EditorLoad { get; set; } = new(null, []);

        public Task<StudioAnalysisWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Workspace);

        public Task<StudioAnalysisEditorLoad> LoadAsync(string? analysisId, CancellationToken cancellationToken = default) =>
            Task.FromResult(EditorLoad);

        public Task<StudioAnalysisCommandResult> SaveDraftAsync(StudioAnalysisPlanEditor plan, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioAnalysisCommandResult(true, "Saved.", plan));

        public Task<StudioAnalysisCommandResult> EstimateAsync(StudioAnalysisPlanEditor plan, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioAnalysisCommandResult(true, "Estimated.", plan));

        public Task<StudioAnalysisCommandResult> PreviewAsync(StudioAnalysisPlanEditor plan, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioAnalysisCommandResult(true, "Previewed.", plan));

        public Task<StudioAnalysisCommandResult> SubmitAsync(StudioAnalysisPlanEditor plan, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioAnalysisCommandResult(true, "Submitted.", plan));
    }
}
