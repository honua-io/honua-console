using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Proves the server-backed analysis builder renders from live honua-server data (AC#4: Testcontainers
/// coverage boots honua-server/PostgreSQL, seeds a known analysis package fixture, and asserts the surface
/// renders/behaves from live data; Docker-unavailable environments skip cleanly). Drives
/// <see cref="HonuaServerStudioAnalysisContentDataSource"/> over the real
/// <c>/api/v1/analysis/content</c> + <c>/artifacts</c> lifecycle (honua-server#1182) and the closed
/// execution engine (honua-server#681/#721/#724) — never an in-memory client.
/// </summary>
[Collection(StudioAnalysisContentIntegrationCollection.Name)]
public sealed class StudioAnalysisBuilderIntegrationTests
{
    private readonly StudioAnalysisContentFixture _fixture;

    public StudioAnalysisBuilderIntegrationTests(StudioAnalysisContentFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task AnalysisBuilder_SeedsPackageAndRendersFromLiveServer()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var dataSource = new HonuaServerStudioAnalysisContentDataSource(_fixture.CreateAnalysisClient());

        // 1. Author a known analysis plan and seed it on the live server through the real save path
        //    (create analysis content item + first version).
        var title = $"Console analysis builder fixture {Guid.NewGuid():N}";
        var seed = StudioAnalysisPackageMapper.CreateTemplate();
        seed.Title = title;
        seed.Goal = "Buffer hydrants by 250m";
        seed.Method = "buffer";
        seed.ComputeProfile = "standard";
        seed.OutputContentType = "layer";
        seed.Inputs.Add(new StudioAnalysisInputEditor { Role = "primary", ServiceId = "hydrants", LayerId = 0 });
        seed.Parameters.Add(new StudioAnalysisParameterEditor { Name = "distanceMeters", Value = "250" });
        seed.OutputSchema.Add(new StudioAnalysisOutputFieldEditor { Name = "buffer_distance", Type = "double" });

        // The environment/auth precondition (opt-in flag, server target, Docker, admin API key) is already
        // gated by _fixture.SkipReason above. Once past it, the live server with a valid admin key must
        // accept a known-valid analysis package seed, so a rejection is a real regression that must fail the
        // smoke — never a skip that reports false-green evidence.
        var saved = await dataSource.SaveDraftAsync(seed);
        Assert.True(saved.Succeeded, $"The live server rejected the seeded analysis package: {saved.Message}");
        Assert.NotNull(saved.Plan);
        var analysisId = saved.Plan!.AnalysisId;
        Assert.False(string.IsNullOrWhiteSpace(analysisId));

        // 2. Loading the package hydrates the editor from the live version, including the server-compiled
        //    DAG/pipeline steps (AC#2).
        var load = await dataSource.LoadAsync(analysisId);
        Assert.True(load.HasEditor);
        Assert.Equal("buffer", load.Plan!.Method);
        Assert.NotEmpty(load.Plan.ServerPipeline);
        Assert.Contains(load.Plan.ServerPipeline, node => node.Method == "buffer");

        // 3. The merged list endpoint (honua-server#1237) enumerates the seeded package from live data.
        var workspace = await dataSource.GetWorkspaceAsync();
        Assert.Empty(workspace.CapabilityStates);
        Assert.Contains(workspace.Plans, plan => plan.AnalysisId == analysisId);

        // 4. The merged estimate endpoint (honua-server#1237) returns a server-computed runtime/cost figure.
        var estimateResult = await dataSource.EstimateAsync(load.Plan);
        Assert.True(estimateResult.Succeeded, $"The live server rejected the estimate request: {estimateResult.Message}");
        Assert.NotNull(load.Plan.Estimate);
        Assert.Contains("server estimate", load.Plan.Estimate!.CostNote, StringComparison.OrdinalIgnoreCase);

        // 5. The analysis builder page renders the seeded plan from the live data source (live list row).
        using var ctx = new Bunit.TestContext();
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioAnalysisPackageDataSource>(dataSource);
        var page = ctx.RenderComponent<StudioAnalysisBuilderPage>();

        page.WaitForAssertion(
            () => Assert.Contains(title, page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(10));
    }
}
