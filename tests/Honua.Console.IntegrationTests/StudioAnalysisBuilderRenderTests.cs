using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Components.Studio;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render regression for the Studio analysis builder. Drives the component against a fake
/// <see cref="IStudioAnalysisContentClient"/> so the carve-out renders the live-shaped plan card + DAG, the
/// honest "unavailable" states for the unbacked slivers (compute estimate, analysis-package preview), and the
/// shared missing-binding surface when the client is unbound — never a fabricated success.
/// </summary>
public sealed class StudioAnalysisBuilderRenderTests
{
    [Fact]
    public void AuthoringMode_WithoutItemId_RendersAuthoringForm()
    {
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioAnalysisContentClient>(new FakeAnalysisContentClient());

        var page = ctx.RenderComponent<StudioAnalysisBuilder>();

        Assert.Contains("Author analysis content", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Plan steps", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-analysis-builder", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadedAnalysisPackage_RendersPlanDagAndHonestUnavailableStates()
    {
        var client = new FakeAnalysisContentClient
        {
            ItemResult = AnalysisEndpointResult<AnalysisContentSnapshot>.FromData(BuildPackageSnapshot())
        };
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioAnalysisContentClient>(client);

        var page = ctx.RenderComponent<StudioAnalysisBuilder>(
            parameters => parameters.Add(component => component.ItemId, "analysis-content-abc"));

        page.WaitForAssertion(
            () => Assert.Contains("Pipeline (DAG)", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // Plan DAG renders both steps and the dependency edge.
        Assert.Contains("step-1", page.Markup, StringComparison.Ordinal);
        Assert.Contains("step-2", page.Markup, StringComparison.Ordinal);
        Assert.Contains("depends on: step-1", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-analysis-item-id=\"analysis-content-abc\"", page.Markup, StringComparison.Ordinal);

        // Declared output schema chip is visible before submit.
        Assert.Contains("FeatureLayer", page.Markup, StringComparison.Ordinal);

        // The unbacked slivers render explicit unavailable states, not mock values.
        Assert.Contains("Estimate unavailable", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Package preview unsupported", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingBinding_RendersSharedErrorSurfaceWithoutFabricatingData()
    {
        var client = new FakeAnalysisContentClient
        {
            ItemResult = AnalysisEndpointResult<AnalysisContentSnapshot>.FromIssue(new AnalysisEndpointIssue(
                "Missing binding",
                "honua-server Analysis Content API (/api/v1/analysis)",
                "Configure Honua:Server:BaseUrl so the Studio analysis builder can bind the server-owned analysis content lifecycle."))
        };
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioAnalysisContentClient>(client);

        var page = ctx.RenderComponent<StudioAnalysisBuilder>(
            parameters => parameters.Add(component => component.ItemId, "analysis-content-missing"));

        page.WaitForAssertion(
            () => Assert.Contains("Analysis content is not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("Pipeline (DAG)", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("data-analysis-item-id", page.Markup, StringComparison.Ordinal);
    }

    private static AnalysisContentSnapshot BuildPackageSnapshot()
    {
        var package = new AnalysisPackageContent
        {
            Intent = new AnalysisIntent { IntentId = "intent-1", Goal = "buffer then dissolve" },
            Plan = new AnalysisPlan
            {
                PlanId = "plan-1",
                IntentId = "intent-1",
                Steps =
                [
                    new AnalysisPlanStep { StepId = "step-1", Kind = AnalysisPlanStepKind.Geoprocess, ProcessId = "geometry.buffer", Inputs = new Dictionary<string, string> { ["distance"] = "10" } },
                    new AnalysisPlanStep { StepId = "step-2", Kind = AnalysisPlanStepKind.Aggregate, ProcessId = "geometry.dissolve", DependsOn = ["step-1"] }
                ],
                Outputs = [ArtifactKind.FeatureLayer]
            },
            RequestedArtifacts = [ArtifactKind.FeatureLayer]
        };

        return new AnalysisContentSnapshot
        {
            Item = new AnalysisContentItem
            {
                ItemId = "analysis-content-abc",
                Kind = AnalysisContentKind.AnalysisPackage,
                Name = "buffer-dissolve",
                Title = "Buffer then dissolve",
                CurrentVersion = 1,
                CurrentVersionId = "analysis-content-abc:v1"
            },
            Version = new AnalysisContentVersion
            {
                VersionId = "analysis-content-abc:v1",
                ItemId = "analysis-content-abc",
                Version = 1,
                Kind = AnalysisContentKind.AnalysisPackage,
                AnalysisPackage = package,
                ContentHash = "hash-1"
            }
        };
    }

    /// <summary>Configurable fake; only the reads the builder exercises on load are wired.</summary>
    private sealed class FakeAnalysisContentClient : IStudioAnalysisContentClient
    {
        public AnalysisEndpointResult<AnalysisContentSnapshot> ItemResult { get; set; } =
            AnalysisEndpointResult<AnalysisContentSnapshot>.FromData(new AnalysisContentSnapshot());

        public Uri BaseUri { get; } = new("https://honua.test");

        public Task<AnalysisEndpointResult<AnalysisContentSnapshot>> CreateItemAsync(
            CreateAnalysisContentItemRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(ItemResult);

        public Task<AnalysisEndpointResult<AnalysisContentSnapshot>> GetItemAsync(
            string itemId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ItemResult);

        public Task<AnalysisEndpointResult<AnalysisContentSnapshot>> GetLatestVersionAsync(
            string itemId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ItemResult);

        public Task<AnalysisEndpointResult<AnalysisContentSnapshot>> GetVersionAsync(
            string itemId, int contentVersion, CancellationToken cancellationToken = default) =>
            Task.FromResult(ItemResult);

        public Task<AnalysisEndpointResult<AnalysisContentSnapshot>> CreateVersionAsync(
            string itemId, CreateAnalysisContentVersionRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(ItemResult);

        public Task<AnalysisEndpointResult<SavedQueryPreviewResult>> PreviewSavedQueryAsync(
            string itemId, int contentVersion, PreviewSavedQueryRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(AnalysisEndpointResult<SavedQueryPreviewResult>.FromData(new SavedQueryPreviewResult()));

        public Task<AnalysisEndpointResult<AnalysisContentJobResponse>> SubmitRunAsync(
            string itemId, int contentVersion, RunAnalysisContentVersionRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(AnalysisEndpointResult<AnalysisContentJobResponse>.FromData(new AnalysisContentJobResponse()));

        public Task<AnalysisEndpointResult<AnalysisContentJobResponse>> SubmitRerunAsync(
            string itemId, int contentVersion, RerunAnalysisContentVersionRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(AnalysisEndpointResult<AnalysisContentJobResponse>.FromData(new AnalysisContentJobResponse()));

        public Task<AnalysisEndpointResult<AnalysisArtifactResolution>> GetArtifactAsync(
            string artifactId, CancellationToken cancellationToken = default) =>
            Task.FromResult(AnalysisEndpointResult<AnalysisArtifactResolution>.FromData(new AnalysisArtifactResolution()));

        public Task<AnalysisEndpointResult<AnalysisJobLogs>> GetJobLogsAsync(
            string jobId, int? limit = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(AnalysisEndpointResult<AnalysisJobLogs>.FromData(new AnalysisJobLogs()));

        public Task<AnalysisEndpointResult<AnalysisJobFailure>> GetJobFailureAsync(
            string jobId, CancellationToken cancellationToken = default) =>
            Task.FromResult(AnalysisEndpointResult<AnalysisJobFailure>.FromData(new AnalysisJobFailure()));
    }
}
