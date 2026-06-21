using Bunit;
using Microsoft.AspNetCore.Components.Forms;
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
    private static Bunit.BunitContext NewContext()
    {
        var ctx = new Bunit.BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        return ctx;
    }

    // [SupplyParameterFromQuery] parameters cannot be set directly in bUnit; navigate the wizard to the
    // ?step= query before rendering so the page reads the step from the route, as it does in the host.
    private static IRenderedComponent<ImportEsriWizardPage> RenderWizardAtStep(Bunit.BunitContext ctx, int step)
    {
        var nav = ctx.Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();
        nav.NavigateTo($"operate/import/esri?step={step}");
        return ctx.Render<ImportEsriWizardPage>();
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

        var cut = ctx.Render<FidelityBadge>(p => p.Add(c => c.Fidelity, fidelity));

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

        var cut = ctx.Render<SourceTargetMappingTable>(p => p
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

        var page = ctx.Render<ImportEsriWebMapPage>();

        // Four intake modes.
        Assert.Contains("Paste JSON", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Upload file", page.Markup, StringComparison.Ordinal);
        Assert.Contains("From URL / item ID", page.Markup, StringComparison.Ordinal);
        Assert.Contains("From connected ArcGIS", page.Markup, StringComparison.Ordinal);

        // Default state is the empty intake — no preloaded sample masquerading as the user's import.
        Assert.DoesNotContain("Layer mapping", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Paste or upload a Web Map JSON", page.Markup, StringComparison.Ordinal);

        // Loading the sample parses it through the real parser and populates the surface.
        page.Find("[data-intake-sample]").Click();

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
    public void WebMapPage_DefaultState_IsEmptyIntake_NoPreloadedSampleAsUserContent()
    {
        using var ctx = NewContext();
        ctx.Services.AddSingleton<IPublishingWorkspaceDataSource>(new UnsupportedPublishingWorkspaceDataSource());

        var page = ctx.Render<ImportEsriWebMapPage>();

        // The bundled sample must not auto-populate as if it were the user's own import.
        Assert.Contains("Paste or upload a Web Map JSON", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Layer mapping", page.Markup, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("[data-esri-binding-banner]"));
        // The Create CTA is disabled until something is parsed.
        var create = page.FindAll("button.console-button").First(b => b.TextContent.Contains("Create map package", StringComparison.Ordinal));
        Assert.True(create.HasAttribute("disabled"));
    }

    [Fact]
    public void WebMapPage_CreatePackage_WhenPublishUnbound_RendersMissingBinding()
    {
        using var ctx = NewContext();
        ctx.Services.AddSingleton<IPublishingWorkspaceDataSource>(new UnsupportedPublishingWorkspaceDataSource());

        var page = ctx.Render<ImportEsriWebMapPage>();
        page.Find("[data-intake-sample]").Click();
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

        var page = ctx.Render<ImportEsriDashboardPage>();
        page.Find("[data-intake-sample]").Click();

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

        var page = ctx.Render<ImportStoryMapPage>();
        page.Find("[data-intake-sample]").Click();

        Assert.Contains("Section → content mapping", page.Markup, StringComparison.Ordinal);
        // Swipe / sidecar degrade, external embed drops.
        Assert.Contains("esri-mapping__row--degrade", page.Markup, StringComparison.Ordinal);
        Assert.Contains("esri-mapping__row--drop", page.Markup, StringComparison.Ordinal);
        Assert.Contains("esri-story-preview", page.Markup, StringComparison.Ordinal);
    }

    // ---- #158 Instant App ----

    [Fact]
    public void InstantAppPage_RendersCapabilityMappingShellPreviewAndBindingBanner()
    {
        using var ctx = NewContext();

        var page = ctx.Render<ImportEsriInstantAppPage>();

        // Default state is the empty intake — the sample is not preloaded as the user's import.
        Assert.Contains("Paste or upload an Instant App configuration", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Capability → app element mapping", page.Markup, StringComparison.Ordinal);

        // Loading the bundled sample parses it through the real parser and populates the surface.
        page.Find("[data-intake-sample]").Click();

        Assert.Contains("Capability → app element mapping", page.Markup, StringComparison.Ordinal);
        Assert.Contains("honua.app-package.v1", page.Markup, StringComparison.Ordinal);
        Assert.Contains("esri-app-preview", page.Markup, StringComparison.Ordinal);

        // The Sidebar sample exercises each Fid state: theme/Arcade degrade, splash drops, primary map manual.
        Assert.Contains("esri-mapping__row--clean", page.Markup, StringComparison.Ordinal);
        Assert.Contains("esri-mapping__row--degrade", page.Markup, StringComparison.Ordinal);
        Assert.Contains("esri-mapping__row--drop", page.Markup, StringComparison.Ordinal);
        Assert.Contains("esri-mapping__row--manual", page.Markup, StringComparison.Ordinal);

        // The primary web map imports separately -> inline binding banner.
        var banner = page.Find("[data-esri-binding-banner]");
        Assert.Contains("binding required", banner.TextContent, StringComparison.Ordinal);
        Assert.Contains("Web Map", banner.TextContent, StringComparison.Ordinal);
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

    // ---- Wave 6: intake validation + dirty-guard wiring ----

    [Fact]
    public void Intake_PasteInvalidJson_RendersInlineErrorAndAriaInvalid_NoParse()
    {
        using var ctx = NewContext();
        ctx.Services.AddSingleton<IPublishingWorkspaceDataSource>(new UnsupportedPublishingWorkspaceDataSource());

        var page = ctx.Render<ImportEsriWebMapPage>();

        // Paste structurally-broken JSON and ask to parse it.
        page.Find("textarea.esri-intake__textarea").Input("{ not json");
        page.FindAll("button.console-button-sm").First(b => b.TextContent.Contains("Parse JSON", StringComparison.Ordinal)).Click();

        // Inline finding + aria-invalid on the control; no mapping table is produced (the parse is gated).
        Assert.Contains("Not valid JSON", page.Markup, StringComparison.Ordinal);
        var textarea = page.Find("textarea.esri-intake__textarea");
        Assert.Equal("true", textarea.GetAttribute("aria-invalid"));
        Assert.DoesNotContain("Layer mapping", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Intake_PasteJsonArray_RendersNotAnObjectError()
    {
        using var ctx = NewContext();
        ctx.Services.AddSingleton<IPublishingWorkspaceDataSource>(new UnsupportedPublishingWorkspaceDataSource());

        var page = ctx.Render<ImportEsriWebMapPage>();
        page.Find("textarea.esri-intake__textarea").Input("[1, 2, 3]");

        Assert.Contains("must be a JSON object", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Intake_UrlMode_RendersBoundUrlInputAndValidatesBadUrl()
    {
        using var ctx = NewContext();

        var page = ctx.Render<ImportEsriDashboardPage>();

        // Switch to URL / item id mode — a bound URL input replaces the static binding note.
        page.FindAll("button.esri-intake__mode").First(b => b.TextContent.Contains("URL", StringComparison.Ordinal)).Click();
        var urlInput = page.Find("input[data-intake-url]");

        // A garbage value (spaces) is neither an absolute URL nor an item id -> inline error + aria-invalid.
        urlInput.Input("not a url");
        Assert.Contains("absolute http(s) URL or an ArcGIS item id", page.Markup, StringComparison.Ordinal);
        Assert.Equal("true", page.Find("input[data-intake-url]").GetAttribute("aria-invalid"));

        // A valid absolute https URL clears the finding.
        page.Find("input[data-intake-url]").Input("https://org.maps.arcgis.com/home/item.html?id=abc");
        Assert.DoesNotContain("absolute http(s) URL or an ArcGIS item id", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Intake_UploadMode_SelectingFile_ClearsSourceRequired()
    {
        using var ctx = NewContext();
        ctx.Services.AddSingleton<IPublishingWorkspaceDataSource>(new UnsupportedPublishingWorkspaceDataSource());

        var page = ctx.Render<ImportEsriWebMapPage>();

        // Switch to Upload mode: the source-required finding shows until a file is staged.
        page.FindAll("button.esri-intake__mode").First(b => b.TextContent.Contains("Upload file", StringComparison.Ordinal)).Click();
        Assert.Contains("Provide a source", page.Markup, StringComparison.Ordinal);

        // Stage a file that fails to parse (empty content). The source-required finding must clear — the
        // operator did provide a source — leaving only the parse error, not misleading "provide a source".
        var file = InputFileContent.CreateFromText(string.Empty, "broken.json");
        page.FindComponent<InputFile>().UploadFiles(file);

        page.WaitForAssertion(
            () => Assert.DoesNotContain("Provide a source", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("data-intake-error", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Intake_ValidPaste_Parses_NoInlineErrors()
    {
        using var ctx = NewContext();

        var page = ctx.Render<ImportStoryMapPage>();
        page.Find("textarea.esri-intake__textarea").Input("""{ "nodes": { "n1": { "type": "text", "title": "Intro" } } }""");
        page.FindAll("button.console-button-sm").First(b => b.TextContent.Contains("Parse JSON", StringComparison.Ordinal)).Click();

        // A valid paste parses to the section mapping with no inline finding remaining.
        Assert.Contains("Section → content mapping", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Not valid JSON", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("must be a JSON object", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void DirtyGuard_EditingIntake_PreventsNavigation()
    {
        using var ctx = NewContext();
        // confirm() returns false => operator chose to stay on the page when leaving with unsaved intake.
        ctx.JSInterop.Setup<bool>("confirm", _ => true).SetResult(false);
        ctx.Services.AddSingleton<IPublishingWorkspaceDataSource>(new UnsupportedPublishingWorkspaceDataSource());
        var nav = ctx.Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();

        var page = ctx.Render<ImportEsriWebMapPage>();

        // Editing the intake marks the form dirty (the inline JSON finding appears once edited); the
        // <UnsavedChangesGuard/> intercepts internal SPA navigation and (confirm() => false) keeps the
        // operator on the page.
        page.Find("textarea.esri-intake__textarea").Input("{ \"title\": \"draft\"");
        page.WaitForAssertion(
            () => Assert.Contains("data-field-key", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.True(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
        nav.NavigateTo("/operate");
        Assert.Equal(Bunit.TestDoubles.NavigationState.Prevented, nav.History.Last().State);
    }

    [Fact]
    public void DirtyGuard_SuccessfulParse_ClearsDirty_AllowsNavigation()
    {
        using var ctx = NewContext();
        ctx.JSInterop.Setup<bool>("confirm", _ => true).SetResult(false);
        ctx.Services.AddSingleton<IPublishingWorkspaceDataSource>(new UnsupportedPublishingWorkspaceDataSource());
        var nav = ctx.Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();

        var page = ctx.Render<ImportEsriWebMapPage>();

        // Stage an edit, then parse a valid Web Map. A successful parse marks the intake clean.
        page.Find("textarea.esri-intake__textarea").Input(EsriImportSampleDocumentsValidWebMap);
        page.FindAll("button.console-button-sm").First(b => b.TextContent.Contains("Parse JSON", StringComparison.Ordinal)).Click();
        page.WaitForAssertion(
            () => Assert.Contains("Layer mapping", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // The clean baseline lets navigation proceed without a prompt.
        nav.NavigateTo("/operate/after-parse");
        page.WaitForAssertion(
            () => Assert.Equal(Bunit.TestDoubles.NavigationState.Succeeded, nav.History.Last().State),
            TimeSpan.FromSeconds(5));
    }

    private const string EsriImportSampleDocumentsValidWebMap =
        """{ "title": "Draft Map", "operationalLayers": [ { "id": "p", "title": "Parcels", "layerType": "ArcGISFeatureLayer", "url": "https://services.arcgis.com/x/FeatureServer/0" } ] }""";

    // ---- Wave 6: wizard step gating ----

    [Fact]
    public void WizardPage_SourceStep_ConnectedSource_AllowsAdvance()
    {
        using var ctx = NewContext();
        ctx.Services.AddSingleton<IEsriMigrationRunDataSource>(new UnsupportedEsriMigrationRunDataSource());

        var page = RenderWizardAtStep(ctx, 0);

        // A connected source gates the Source step valid -> the Continue control is enabled.
        var next = page.Find("button.publish-wizard-next");
        Assert.False(next.HasAttribute("disabled"));
    }

    [Fact]
    public void WizardPage_MapStep_ResolvableMappings_AllowsRun()
    {
        using var ctx = NewContext();
        ctx.Services.AddSingleton<IEsriMigrationRunDataSource>(new UnsupportedEsriMigrationRunDataSource());

        var page = RenderWizardAtStep(ctx, 2);

        // The Map step resolves at least one non-dropped mapping -> the "Run migration →" control is enabled.
        var run = page.Find("button.publish-wizard-next");
        Assert.False(run.HasAttribute("disabled"));
        Assert.Contains("Run migration", run.TextContent, StringComparison.Ordinal);
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
