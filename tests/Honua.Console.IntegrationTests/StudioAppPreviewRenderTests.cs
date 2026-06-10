using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for the APP family's live app-shell preview
/// (<see cref="StudioAppPreview"/>) — the generated app package's FINAL visible output. Asserts the
/// shell renders the app title + nav and each generated page as its real panel: a map page mounts the
/// live <see cref="MapPreview"/> schematic, a dashboard/report page mounts <see cref="ChartPreview"/>, a
/// table page renders a structural table of its binding, and a form page renders its bound form. An
/// empty package (or one with no bound page) renders an honest empty/unbound state — never fabricated
/// sample panels (Charter §11). The shell renders fully without any map/chart backend or JS runtime.
/// </summary>
public sealed class StudioAppPreviewRenderTests
{
    [Fact]
    public void AppPreview_GeneratedApp_RendersShellTitleNavAndEachPanel()
    {
        var app = new StudioAppEditorState
        {
            Title = "Field operations app",
            Pages =
            [
                new StudioAppPageState { Route = "/map", Title = "Site map", ComponentKind = "map", ContentBinding = "content:parcels@v3" },
                new StudioAppPageState { Route = "/insights", Title = "Insights", ComponentKind = "dashboard", ContentBinding = "content:metrics@v2" },
                new StudioAppPageState { Route = "/permits", Title = "Permit list", ComponentKind = "table", ContentBinding = "content:permits@v5" },
            ]
        };

        using var ctx = new Bunit.TestContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = ctx.RenderComponent<StudioAppPreview>(parameters => parameters.Add(p => p.App, app));

        // The shell renders as a real app: a header with the app title and a nav of the pages.
        Assert.Contains("data-app-shell=\"true\"", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Field operations app", cut.Find("[data-app-shell-header]").TextContent, StringComparison.Ordinal);
        Assert.Contains("Site map", cut.Find(".studio-app-shell-nav").TextContent, StringComparison.Ordinal);
        Assert.Contains("Insights", cut.Find(".studio-app-shell-nav").TextContent, StringComparison.Ordinal);
        Assert.Contains("Permit list", cut.Find(".studio-app-shell-nav").TextContent, StringComparison.Ordinal);

        // One real panel per generated page, in order.
        var panels = cut.FindAll("[data-app-shell-panel]");
        Assert.Equal(3, panels.Count);

        // The map page mounts the live MapPreview (its schematic placeholder + scale chrome are the markers).
        var mapPanel = cut.Find("[data-app-shell-panel=\"map\"]");
        Assert.Contains("map-preview-schematic", mapPanel.InnerHtml, StringComparison.Ordinal);
        Assert.Contains("content:parcels@v3", mapPanel.TextContent, StringComparison.Ordinal);

        // The dashboard page mounts the ChartPreview (its schematic bar-chart is the marker; no spec is
        // carried on an app page, so it honestly degrades — no fabricated chart data).
        var chartPanel = cut.Find("[data-app-shell-panel=\"dashboard\"]");
        Assert.Contains("chart-preview-schematic", chartPanel.InnerHtml, StringComparison.Ordinal);

        // The table page renders a real structural table of its declared binding.
        var tablePanel = cut.Find("[data-app-shell-panel=\"table\"]");
        Assert.Single(tablePanel.QuerySelectorAll("[data-app-shell-table] table"));
        Assert.Contains("content:permits@v5", tablePanel.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void AppPreview_FormPage_RendersBoundFormControls()
    {
        var app = new StudioAppEditorState
        {
            Title = "Inspections",
            Pages =
            [
                new StudioAppPageState { Route = "/inspect", Title = "Submit inspection", ComponentKind = "form", ContentBinding = "content:inspection@v4" },
            ]
        };

        using var ctx = new Bunit.TestContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = ctx.RenderComponent<StudioAppPreview>(parameters => parameters.Add(p => p.App, app));

        var formPanel = cut.Find("[data-app-shell-panel=\"form\"]");
        Assert.Single(formPanel.QuerySelectorAll("[data-app-shell-form]"));
        Assert.Contains("content:inspection@v4", formPanel.TextContent, StringComparison.Ordinal);
        // The bound form renders a submit affordance.
        Assert.Contains(formPanel.QuerySelectorAll("button"), b => b.TextContent.Contains("Submit", StringComparison.Ordinal));
    }

    [Fact]
    public void AppPreview_EmptyPackage_RendersHonestEmptyState()
    {
        var app = new StudioAppEditorState { Title = "Untitled app", Pages = [] };

        using var ctx = new Bunit.TestContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = ctx.RenderComponent<StudioAppPreview>(parameters => parameters.Add(p => p.App, app));

        // No pages → honest empty state, no fabricated sample panels.
        Assert.Single(cut.FindAll("[data-app-shell-empty]"));
        Assert.Empty(cut.FindAll("[data-app-shell-panel]"));
        Assert.Contains("No pages yet", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AppPreview_NoPageBound_RendersUnboundResultState()
    {
        var app = new StudioAppEditorState
        {
            Title = "Draft app",
            Pages =
            [
                new StudioAppPageState { Route = "/map", Title = "Map", ComponentKind = "map", ContentBinding = string.Empty },
            ]
        };

        using var ctx = new Bunit.TestContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = ctx.RenderComponent<StudioAppPreview>(parameters => parameters.Add(p => p.App, app));

        // The page still renders its panel (the honest unbound MapPreview placeholder), and the shell
        // surfaces an explicit unbound notice rather than inventing bound data.
        Assert.Single(cut.FindAll("[data-app-shell-unbound]"));
        var mapPanel = cut.Find("[data-app-shell-panel=\"map\"]");
        Assert.Contains("map-preview-schematic", mapPanel.InnerHtml, StringComparison.Ordinal);
        Assert.Contains("unbound", mapPanel.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void AppPreview_NullApp_RendersEmptyStateWithoutThrowing()
    {
        using var ctx = new Bunit.TestContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = ctx.RenderComponent<StudioAppPreview>();

        Assert.Single(cut.FindAll("[data-app-shell-empty]"));
        Assert.Empty(cut.FindAll("[data-app-shell-panel]"));
    }
}
