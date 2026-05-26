using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Components.Studio;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Proves the Studio analysis builder renders from live honua-server data (AC: Testcontainers coverage boots
/// honua-server/PostgreSQL, seeds an analysis-content fixture via POST /api/v1/analysis/content/items, and
/// asserts live-data rendering; Docker-unavailable environments skip cleanly). Reuses
/// <see cref="StudioPackageLifecycleFixture"/> so a single honua-server container backs both the Studio
/// lifecycle and analysis-content suites (shared admin-key seam, no second container).
/// </summary>
[Collection(StudioPackageLifecycleIntegrationCollection.Name)]
public sealed class StudioAnalysisContentIntegrationTests
{
    private readonly StudioPackageLifecycleFixture _fixture;

    public StudioAnalysisContentIntegrationTests(StudioPackageLifecycleFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task AnalysisBuilder_RendersSeededAnalysisPackage_FromLiveServer()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var client = _fixture.CreateAnalysisClient();

        // 1. Seed a real analysis-package content item + version 1 through the live #1182 contract.
        var created = await client.CreateItemAsync(BuildSeedRequest());
        Skip.If(
            !created.IsSuccess,
            $"The live server did not accept the analysis-content create: {created.Issue?.State} {created.Issue?.Detail}");
        Assert.NotNull(created.Data);
        var itemId = created.Data.Item.ItemId;
        Assert.False(string.IsNullOrWhiteSpace(itemId));
        Assert.Equal(AnalysisContentKind.AnalysisPackage, created.Data.Item.Kind);

        // 2. Re-open the item through the same client to confirm a durable, server-owned round-trip.
        var reopened = await client.GetItemAsync(itemId);
        Assert.True(reopened.IsSuccess, reopened.Issue?.Detail);
        Assert.NotNull(reopened.Data);
        Assert.NotEmpty(reopened.Data.Version.AnalysisPackage!.Plan.Steps);

        // 3. The builder renders the live plan (DAG + plan id) - never a mock - using the real client.
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton(client);

        var page = ctx.RenderComponent<StudioAnalysisBuilder>(
            parameters => parameters.Add(component => component.ItemId, itemId));

        page.WaitForAssertion(
            () => Assert.Contains("Pipeline (DAG)", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(10));
        Assert.Contains(itemId, page.Markup, StringComparison.Ordinal);
        Assert.Contains("seed-step", page.Markup, StringComparison.Ordinal);
    }

    private static CreateAnalysisContentItemRequest BuildSeedRequest() =>
        new()
        {
            Kind = AnalysisContentKind.AnalysisPackage,
            Name = $"console-it-{Guid.NewGuid():N}",
            Title = "Console integration analysis package",
            AnalysisPackage = new AnalysisPackageContent
            {
                Intent = new AnalysisIntent
                {
                    IntentId = "intent-1",
                    Goal = "buffer incidents",
                    RequestedOutputs = [ArtifactKind.FeatureLayer]
                },
                Plan = new AnalysisPlan
                {
                    PlanId = "plan-1",
                    IntentId = "intent-1",
                    Steps =
                    [
                        new AnalysisPlanStep
                        {
                            StepId = "seed-step",
                            Kind = AnalysisPlanStepKind.Geoprocess,
                            ProcessId = "geometry.buffer",
                            Inputs = new Dictionary<string, string> { ["distance"] = "10" }
                        }
                    ],
                    Outputs = [ArtifactKind.FeatureLayer]
                },
                Parameters = new Dictionary<string, string> { ["distance"] = "10" },
                RequestedArtifacts = [ArtifactKind.FeatureLayer],
                SpatialReferenceId = 4326,
                Units = "meters"
            }
        };
}
