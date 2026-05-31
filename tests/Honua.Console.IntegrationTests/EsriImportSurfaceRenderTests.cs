using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for the four Esri content-import surfaces (issue #122 / #100, #101, #104,
/// #102) and their shared components. Asserts the design-handoff landmarks from screens-import-esri.jsx and
/// screens-import-wizard.jsx — the intake modes, the source -> target mapping table with each Fid badge
/// state, the target preview, the inline + full-screen missing-binding states, the wizard steps, and the
/// scorecard counts / parity bar. The Esri-JSON parse is deterministic Console-side work; the migration-run
/// engine binds honua-devops, else the missing-binding state.
/// </summary>
public sealed class EsriImportSurfaceRenderTests
{
    private static Bunit.TestContext NewContext()
    {
        var ctx = new Bunit.TestContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        return ctx;
    }

    // [SupplyParameterFromQuery] parameters cannot be set directly in bUnit; navigate the wizard to the
    // ?step= query before rendering so the page reads the step from the route, as it does in the host.
    private static IRenderedComponent<ImportEsriWizardPage> RenderWizardAtStep(Bunit.TestContext ctx, int step)
    {
        var nav = ctx.Services.GetRequiredService<Bunit.TestDoubles.FakeNavigationManager>();
        nav.NavigateTo($"operate/import/esri?step={step}");
        return ctx.RenderComponent<ImportEsriWizardPage>();
    }

    // ---- shared components ----

    [Theory]
    [InlineData(ImportFidelity.Clean, "clean", "converts clean", "console-state-success")]
    [InlineData(ImportFidelity.Degrade, "degrade", "degrades", "console-state-warning")]
    [InlineData(ImportFidelity.Drop, "drop", "dropped", "console-state-danger")]
    [InlineData(ImportFidelity.Manual, "manual", "needs review", "console-state-info")]
    public void FidelityBadge_RendersEachStateWithLabelAndStateClass(ImportFidelity fidelity, string slug, string label, string stateClass)
    {
        using var ctx = NewContext();

        var cut = ctx.RenderComponent<FidelityBadge>(p => p.Add(c => c.Fidelity, fidelity));

        Assert.Contains($"data-fidelity=\"{slug}\"", cut.Markup, StringComparison.Ordinal);
        Assert.Contains(label, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(stateClass, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void MappingTable_RendersEveryFidelityRowStateAndIssuesOnlyFilter()
    {
        using var ctx = NewContext();
        IReadOnlyList<EsriMappingRow> rows =
        [
            new("Parcels", "FeatureLayer", "parcels/fill", ImportFidelity.Clean, "matched by URL", BoundResource: "parcels_2024"),
            new("Land use", "FeatureLayer", "landuse/fill", ImportFidelity.Degrade, "Arcade label expr → static label"),
            new("Hydrants", "FeatureLayer", "hydrants/circle", ImportFidelity.Manual, "pick a resource"),
            new("Heatmap", "FeatureLayer", null, ImportFidelity.Drop, "heatmap renderer not in map-package.v1", Included: false),
        ];

        var cut = ctx.RenderComponent<SourceTargetMappingTable>(p => p
            .Add(c => c.Rows, rows)
            .Add(c => c.ShowBoundColumn, true)
            .Add(c => c.Heading, "Layer mapping"));

        // Every fidelity row state is rendered with its row class and badge.
        Assert.Contains("esri-mapping__row--clean", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("esri-mapping__row--degrade", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("esri-mapping__row--manual", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("esri-mapping__row--drop", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("parcels_2024", cut.Markup, StringComparison.Ordinal);

        // All four rows visible by default.
        Assert.Equal(4, cut.FindAll("tbody tr.esri-mapping__row").Count);

        // "issues only" hides the clean row.
        cut.FindAll("button.esri-mapping__filter").Single(b => b.TextContent.Contains("issues only", StringComparison.Ordinal)).Click();
        Assert.Equal(3, cut.FindAll("tbody tr.esri-mapping__row").Count);
        Assert.DoesNotContain("esri-mapping__row--clean", cut.Markup, StringComparison.Ordinal);
    }

    // ---- #100 Web Map ----

    [Fact]
    public void WebMapPage_RendersIntakeModesMappingTablePreviewAndBindingBanner()
    {
        using var ctx = NewContext();
        ctx.Services.AddSingleton<IPublishingWorkspaceDataSource>(new UnsupportedPublishingWorkspaceDataSource());

        var page = ctx.RenderComponent<ImportEsriWebMapPage>();

        // Four intake modes.
        Assert.Contains("Paste JSON", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Upload file", page.Markup, StringComparison.Ordinal);
        Assert.Contains("From URL / item ID", page.Markup, StringComparison.Ordinal);
        Assert.Contains("From connected ArcGIS", page.Markup, StringComparison.Ordinal);

        // Mapping table + target preview + schema.
        Assert.Contains("Layer mapping", page.Markup, StringComparison.Ordinal);
        Assert.Contains("honua.map-package.v1", page.Markup, StringComparison.Ordinal);
        Assert.Contains("map-preview-schematic", page.Markup, StringComparison.Ordinal);

        // The bundled sample exercises each Fid state including a manual (Hydrants) -> inline binding banner.
        Assert.Contains("esri-mapping__row--manual", page.Markup, StringComparison.Ordinal);
        Assert.Contains("esri-mapping__row--drop", page.Markup, StringComparison.Ordinal);
        var banner = page.Find("[data-esri-binding-banner]");
        Assert.Contains("binding required", banner.TextContent, StringComparison.Ordinal);
        Assert.Contains("Hydrants", banner.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void WebMapPage_CreatePackage_WhenPublishUnbound_RendersMissingBinding()
    {
        using var ctx = NewContext();
        ctx.Services.AddSingleton<IPublishingWorkspaceDataSource>(new UnsupportedPublishingWorkspaceDataSource());

        var page = ctx.RenderComponent<ImportEsriWebMapPage>();
        page.FindAll("button.console-button").First(b => b.TextContent.Contains("Create map package", StringComparison.Ordinal)).Click();

        page.WaitForAssertion(
            () => Assert.Contains("No publish target is bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
    }

    // ---- #101 Dashboard ----

    [Fact]
    public void DashboardPage_RendersElementWidgetGridAndLayoutPreview()
    {
        using var ctx = NewContext();

        var page = ctx.RenderComponent<ImportEsriDashboardPage>();

        Assert.Contains("Element → widget mapping", page.Markup, StringComparison.Ordinal);
        Assert.Contains("KPI", page.Markup, StringComparison.Ordinal);
        // Gauge degrades, richText drops.
        Assert.Contains("esri-mapping__row--degrade", page.Markup, StringComparison.Ordinal);
        Assert.Contains("esri-mapping__row--drop", page.Markup, StringComparison.Ordinal);
        Assert.Contains("esri-dashboard-preview", page.Markup, StringComparison.Ordinal);
    }

    // ---- #104 StoryMap ----

    [Fact]
    public void StoryMapPage_RendersSectionBlockMappingAndContentPreview()
    {
        using var ctx = NewContext();

        var page = ctx.RenderComponent<ImportStoryMapPage>();

        Assert.Contains("Section → content mapping", page.Markup, StringComparison.Ordinal);
        // Swipe / sidecar degrade, external embed drops.
        Assert.Contains("esri-mapping__row--degrade", page.Markup, StringComparison.Ordinal);
        Assert.Contains("esri-mapping__row--drop", page.Markup, StringComparison.Ordinal);
        Assert.Contains("esri-story-preview", page.Markup, StringComparison.Ordinal);
    }

    // ---- #102 Wizard / Run / Scorecard ----

    [Fact]
    public void WizardPage_MapStep_RendersStepsConversionSummaryAndHonuaDevopsOwner()
    {
        using var ctx = NewContext();
        ctx.Services.AddSingleton<IEsriMigrationRunDataSource>(new UnsupportedEsriMigrationRunDataSource());

        var page = RenderWizardAtStep(ctx, 2);

        // Five-step stepper.
        var stepper = page.Find("ol.publish-stepper");
        Assert.Contains("Source", stepper.TextContent, StringComparison.Ordinal);
        Assert.Contains("Select content", stepper.TextContent, StringComparison.Ordinal);
        Assert.Contains("Map", stepper.TextContent, StringComparison.Ordinal);
        Assert.Contains("Run", stepper.TextContent, StringComparison.Ordinal);
        Assert.Contains("Scorecard", stepper.TextContent, StringComparison.Ordinal);

        // Map step shows the mixed-content mapping + conversion summary + the honua-devops run owner.
        Assert.Contains("Map selected content", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Conversion summary", page.Markup, StringComparison.Ordinal);
        Assert.Contains("honua-devops", page.Markup, StringComparison.Ordinal);
        // Every fidelity class appears in the mixed-content rows.
        Assert.Contains("esri-mapping__row--degrade", page.Markup, StringComparison.Ordinal);
        Assert.Contains("esri-mapping__row--manual", page.Markup, StringComparison.Ordinal);
        Assert.Contains("esri-mapping__row--drop", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void WizardPage_RunStep_WhenRunEngineUnbound_RendersMissingBinding()
    {
        using var ctx = NewContext();
        ctx.Services.AddSingleton<IEsriMigrationRunDataSource>(new UnsupportedEsriMigrationRunDataSource());

        var page = RenderWizardAtStep(ctx, 3);

        page.WaitForAssertion(
            () => Assert.NotNull(page.Find("[data-esri-missing-binding]")),
            TimeSpan.FromSeconds(5));
        Assert.Contains("No honua-server is configured", page.Markup, StringComparison.Ordinal);
        Assert.Contains("honua-devops migration-run API", page.Markup, StringComparison.Ordinal);
        // No fabricated run progress.
        Assert.DoesNotContain("data-run-table", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void WizardPage_ScorecardStep_WhenUnbound_RendersMissingBindingNoParityNumbers()
    {
        using var ctx = NewContext();
        ctx.Services.AddSingleton<IEsriMigrationRunDataSource>(new UnsupportedEsriMigrationRunDataSource());

        var page = RenderWizardAtStep(ctx, 4);

        page.WaitForAssertion(
            () => Assert.Contains("No honua-server is configured", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("data-scorecard-table", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("esri-parity-bar", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void WizardPage_RunAndScorecard_WhenBound_RenderProgressAndParity()
    {
        using var ctx = NewContext();
        ctx.Services.AddSingleton<IEsriMigrationRunDataSource>(new StubMigrationRunDataSource());

        var run = RenderWizardAtStep(ctx, 3);
        run.WaitForAssertion(
            () => Assert.Contains("data-run-table", run.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("esri-run-bar", run.Markup, StringComparison.Ordinal);
        Assert.Contains("Roads FeatureServer", run.Markup, StringComparison.Ordinal);
        Assert.Contains("console-state-danger", run.Markup, StringComparison.Ordinal); // failed item

        var card = RenderWizardAtStep(ctx, 4);
        card.WaitForAssertion(
            () => Assert.Contains("data-scorecard-table", card.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("esri-parity-bar", card.Markup, StringComparison.Ordinal);
        Assert.Contains("Overall parity", card.Markup, StringComparison.Ordinal);
        Assert.Contains("Export report (PDF)", card.Markup, StringComparison.Ordinal);
        Assert.Contains("Export findings (CSV)", card.Markup, StringComparison.Ordinal);
        // Counts surface.
        Assert.Contains("Passed", card.Markup, StringComparison.Ordinal);
        Assert.Contains("Degraded", card.Markup, StringComparison.Ordinal);
        Assert.Contains("Needs binding", card.Markup, StringComparison.Ordinal);
        Assert.Contains("Failed", card.Markup, StringComparison.Ordinal);
    }

    private sealed class StubMigrationRunDataSource : IEsriMigrationRunDataSource
    {
        public Task<MigrationPlanLoad> LoadPlanAsync(string migrationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MigrationPlanLoad(null, []));

        public Task<MigrationRunLoad> LoadRunAsync(string migrationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MigrationRunLoad(
                new MigrationRunView(
                    "mig_4830", "honua-devops", "dev", "jamie", true, 11, 18, 1, "~4 min remaining",
                    [
                        new MigrationRunItem("Public Works Web Map", "map", MigrationItemState.Done, "public-works-overview"),
                        new MigrationRunItem("Roads FeatureServer", "data", MigrationItemState.Failed, "geometry repair error · row 1.2M", Retryable: true),
                        new MigrationRunItem("Hydrants Web Map", "map", MigrationItemState.Queued, "waiting"),
                    ]),
                []));

        public Task<MigrationScorecardLoad> LoadScorecardAsync(string migrationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MigrationScorecardLoad(
                new MigrationScorecardView(
                    "mig_4830", 18, "8m 14s", 11, 4, 2, 1,
                    [
                        new MigrationScorecardItem("Public Works Web Map", MigrationParityResult.Pass, "7 layers · 0 issues", "public-works-overview"),
                        new MigrationScorecardItem("Q3 Ops Dashboard", MigrationParityResult.Degraded, "gauge → radial KPI · 1 iframe dropped", "q3-operations"),
                        new MigrationScorecardItem("Roads FeatureServer", MigrationParityResult.Failed, "geometry repair error · row 1.2M", "—"),
                    ],
                    [new MigrationLandedCount("Map packages", 4)],
                    ["Bind 2 unbound layers", "Retry 1 failed data migration"]),
                []));
    }
}
