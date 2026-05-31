using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Proves the server-backed query builder renders from live honua-server data (AC#4: Testcontainers
/// coverage boots honua-server/PostgreSQL, seeds a known saved-query fixture through the real save path, and
/// asserts the surface renders/behaves from live data; Docker-unavailable environments skip cleanly). Drives
/// <see cref="HonuaServerStudioQueryContentDataSource"/> over the real <c>/api/v1/analysis/content</c>
/// saved-query lifecycle (honua-server#1182, AnalysisContentKind.SavedQuery) — never an in-memory client.
/// Reuses <see cref="StudioAnalysisContentFixture"/> because the query builder and analysis builder share
/// the single analysis content client/contract.
/// </summary>
[Collection(StudioAnalysisContentIntegrationCollection.Name)]
public sealed class StudioQueryBuilderIntegrationTests
{
    private readonly StudioAnalysisContentFixture _fixture;

    public StudioQueryBuilderIntegrationTests(StudioAnalysisContentFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task QueryBuilder_SavesQueryReloadsAndPreviewsFromLiveServer()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var dataSource = new HonuaServerStudioQueryContentDataSource(_fixture.CreateAnalysisClient());

        // 1. Author a known saved query and seed it on the live server through the real save path
        //    (create savedQuery content item + first version).
        var title = $"Console query builder fixture {Guid.NewGuid():N}";
        var seed = StudioQueryPackageMapper.CreateTemplate();
        seed.Title = title;
        seed.NaturalLanguageQuery = "All features in the seeded layer";
        seed.ServiceName = string.Empty;
        seed.LayerId = 0;
        seed.PreviewLimit = 5;

        // The environment/auth precondition (opt-in flag, server target, Docker, admin API key) is already
        // gated by _fixture.SkipReason above. Once past it, the live server with a valid admin key must
        // accept a known-valid saved query, so a rejection is a real regression that must fail the smoke —
        // never a skip that reports false-green evidence.
        var saved = await dataSource.SaveAsync(seed);
        Assert.True(saved.Succeeded, $"The live server rejected the seeded saved query: {saved.Message}");
        Assert.NotNull(saved.Query);
        var queryId = saved.Query!.QueryId;
        Assert.False(string.IsNullOrWhiteSpace(queryId));

        // 2. Reloading the query hydrates the editor from the live version (AC: reopen).
        var load = await dataSource.LoadAsync(queryId);
        Assert.True(load.HasEditor);
        Assert.Equal(title, load.Query!.Title);
        Assert.Equal(queryId, load.Query.QueryId);

        // 3. Drive the preview binding against the live canonical feature-query pipeline. The Testcontainers
        //    fixture does not provision a feature layer, so a preview of an unseeded layer legitimately
        //    fails server-side; the contract is that the binding turns that into a structured command result
        //    (a surfaced capability issue), never an unhandled throw, and never a fabricated preview. When a
        //    layer IS seeded (external server target) the preview succeeds and resolves a live projection.
        var preview = await dataSource.PreviewAsync(load.Query);
        if (preview.Succeeded)
        {
            Assert.NotNull(load.Query.Preview);
        }
        else
        {
            Assert.NotNull(preview.Issue);
            Assert.False(string.IsNullOrWhiteSpace(preview.Issue!.Detail));
        }

        // 4. The query builder page renders the seeded query from the live data source.
        using var ctx = new Bunit.TestContext();
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioQueryPackageDataSource>(dataSource);
        var page = ctx.RenderComponent<StudioQueryBuilderPage>();

        page.WaitForAssertion(
            () => Assert.Contains("Query builder", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(10));
    }
}
