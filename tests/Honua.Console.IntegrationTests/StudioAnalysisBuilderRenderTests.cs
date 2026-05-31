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
    public void AnalysisBuilder_WhenListAndEstimateGatedOn1237_RendersDegradedCapabilityStates()
    {
        // honua-server#1182 (core analysis content/artifacts) is live-bound, but honua-server#1237
        // (analysis content API: list + cost-estimate) is still open. Those two capabilities degrade to
        // explicit, labelled capability states instead of fabricated data. Drive the page with the same
        // capability states the live HonuaServerStudioAnalysisContentDataSource emits.
        var listGated = new StudioAnalysisCapabilityState(
            "Analysis builder",
            "Unsupported",
            "GET /api/v1/analysis/content/items (list)",
            "honua-server does not yet expose an analysis-package list endpoint, so existing packages cannot "
            + "be enumerated from live data. This list binds automatically once honua-server#1237 adds a list route.");
        var estimateGated = new StudioAnalysisCapabilityState(
            "Analysis builder",
            "Unsupported",
            "POST /api/v1/analysis/content/items/{itemId}/versions/{contentVersion}/estimate",
            "Estimate unavailable from server: honua-server does not yet expose a runtime/cost estimate "
            + "route, so this estimate is a local Console projection. It binds once honua-server#1237 adds the route.");

        var data = new FakeAnalysisDataSource
        {
            Workspace = new StudioAnalysisWorkspace([], [listGated, estimateGated])
        };
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioAnalysisPackageDataSource>(data);

        var page = ctx.RenderComponent<StudioAnalysisBuilderPage>();

        // The degraded list and estimate states render as explicit server capability states (no mock list,
        // no fabricated server estimate) referencing the open server issue.
        page.WaitForAssertion(
            () => Assert.Equal(2, page.FindAll("[data-analysis-capability]").Count),
            TimeSpan.FromSeconds(5));
        Assert.Contains(
            "[data-analysis-capability=\"GET /api/v1/analysis/content/items (list)\"]",
            page.FindAll("[data-analysis-capability]").Select(e => $"[data-analysis-capability=\"{e.GetAttribute("data-analysis-capability")}\"]"));
        Assert.Contains("honua-server#1237", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Estimate unavailable from server", page.Markup, StringComparison.Ordinal);
        // No analysis-package list was fabricated.
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

        // The design-handoff StudioAnalysisEditor lays the editor out as a header bar over a
        // three-region body: a step rail, the pipeline canvas + run preview, and the plan inspector.
        var editor = page.Find("[data-analysis-builder]");
        Assert.NotNull(editor.QuerySelector(".studio-analysis-editor-bar"));
        Assert.NotNull(editor.QuerySelector(".studio-analysis-editor-body"));
        Assert.NotNull(editor.QuerySelector(".studio-analysis-steps"));
        Assert.NotNull(editor.QuerySelector(".studio-analysis-canvas"));
        Assert.NotNull(editor.QuerySelector(".studio-analysis-inspector"));
        Assert.NotNull(editor.QuerySelector("[data-analysis-run-preview]"));

        // The step rail and the pipeline canvas project from the same plan pipeline and stay in sync.
        var railSteps = page.FindAll("[data-analysis-step-list] .studio-analysis-step").Count;
        var canvasNodes = page.FindAll("[data-analysis-pipeline] .studio-analysis-pipeline-node").Count;
        Assert.True(railSteps > 0);
        Assert.Equal(railSteps, canvasNodes);
        Assert.Equal(railSteps.ToString(), page.Find("[data-analysis-step-count]").GetAttribute("data-analysis-step-count"));
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
    public void AnalysisBuilder_RunningJob_ResolvesArtifactByIdAndRendersDownstreamTargets()
    {
        // A submitted job runs asynchronously on the execution engine; the bound contract has no
        // list-job-artifacts route, so the operator resolves the produced artifact by id, which binds the
        // result-artifact panel + downstream families (AC#3) through the live artifacts route.
        var plan = ReadyPlan();
        plan.SubmittedJob = new StudioAnalysisJobView("job-1", "Running", 2);
        var data = new FakeAnalysisDataSource
        {
            Workspace = new StudioAnalysisWorkspace(
                [new StudioAnalysisPlanListItem("analysis-1", "Hydrant buffer", "buffer", "standard", 2, null, DateTimeOffset.UtcNow)],
                []),
            EditorLoad = new StudioAnalysisEditorLoad(plan, []),
            ResolveArtifactResult = new StudioAnalysisArtifactView(
                "artifact-1", "FeatureLayer", "Buffered hydrants", null, null, null, "retained",
                ["layer", "content", "workflow"])
        };
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioAnalysisPackageDataSource>(data);

        var page = ctx.RenderComponent<StudioAnalysisBuilderPage>();
        page.WaitForAssertion(() => FindButton(page, "Hydrant buffer"), TimeSpan.FromSeconds(5));
        FindButton(page, "Hydrant buffer").Click();

        // The running-job panel exposes the resolve-artifact affordance; resolve stays disabled until an id is
        // entered, so the operator never resolves an empty id.
        page.WaitForAssertion(
            () => Assert.Contains("data-analysis-resolve-artifact", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.True(FindButton(page, "Resolve artifact").HasAttribute("disabled"));

        page.Find("[data-analysis-resolve-artifact] input").Change("artifact-1");
        FindButton(page, "Resolve artifact").Click();

        page.WaitForAssertion(
            () => Assert.Contains("data-analysis-artifact", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Equal("artifact-1", data.ResolvedArtifactId);
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

        public string? ResolvedArtifactId { get; private set; }

        public StudioAnalysisArtifactView? ResolveArtifactResult { get; set; }

        public Task<StudioAnalysisCommandResult> ResolveArtifactAsync(
            string artifactId,
            StudioAnalysisPlanEditor plan,
            CancellationToken cancellationToken = default)
        {
            ResolvedArtifactId = artifactId;
            if (ResolveArtifactResult is not null && plan.SubmittedJob is { } job)
            {
                plan.SubmittedJob = job with { Artifact = ResolveArtifactResult };
            }

            return Task.FromResult(new StudioAnalysisCommandResult(true, $"Resolved {artifactId}.", plan));
        }
    }
}
