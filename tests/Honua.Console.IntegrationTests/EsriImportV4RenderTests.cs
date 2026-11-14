using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class EsriImportV4RenderTests
{
    public static TheoryData<string> ContentPages() => new("dashboard", "instant-app", "notebook", "web-map", "storymap");

    [Theory]
    [MemberData(nameof(ContentPages))]
    public void ContentImportPages_V4RenderHeadingIntakeMappingAndPreviewAsPanels(string slug)
    {
        using var ctx = NewContext(v4: true);
        var (type, title, _, noun) = Page(slug);
        var page = ctx.Render(builder => { builder.OpenComponent(0, type); builder.CloseComponent(); });
        CatalogAdminPreview.Capture($"data-esri-{slug}-empty", page.Markup);
        page.Find("[data-intake-sample]").Click();

        Assert.Equal(title, page.Find(".console-page-heading-v4 h1").TextContent);
        Assert.Empty(page.FindAll(".console-detail-heading, .console-page-heading-v4 .console-kicker, .esri-import__preview-head"));
        var intake = page.Find("section.console-panel[aria-labelledby='esri-intake-heading']");
        Assert.Equal($"Source {noun}", intake.QuerySelector("h2")!.TextContent);
        Assert.NotNull(intake.QuerySelector("[data-esri-intake]"));
        var mapping = page.Find("section[data-esri-mapping].console-panel[aria-labelledby='esri-mapping-heading']");
        Assert.NotNull(mapping.QuerySelector(".console-panel-heading h2#esri-mapping-heading"));
        Assert.Empty(mapping.QuerySelectorAll("h3"));
        Assert.NotNull(page.Find("[data-esri-target-preview] > section.console-panel[aria-labelledby='esri-target-preview-heading']"));
        Assert.Empty(page.FindAll(".console-panel .console-panel"));
        CatalogAdminPreview.Capture($"data-esri-{slug}", page.Markup);
    }

    [Theory]
    [MemberData(nameof(ContentPages))]
    public void ContentImportPages_LegacyKeepBreadcrumbHeadingAsideAndNestedMappingHeading(string slug)
    {
        using var ctx = NewContext(v4: false);
        var (type, title, crumb, _) = Page(slug);
        var page = ctx.Render(builder => { builder.OpenComponent(0, type); builder.CloseComponent(); });
        page.Find("[data-intake-sample]").Click();

        Assert.Empty(page.FindAll(".console-page-heading-v4, .console-panel"));
        var heading = page.Find(".console-page > .console-detail-heading");
        Assert.Equal($"Operate / Import source content / {crumb}", Normalize(heading.QuerySelector(".console-kicker")!.TextContent));
        Assert.Equal(title, heading.QuerySelector("h1")!.TextContent);
        Assert.NotNull(heading.QuerySelector(":scope > div > p.console-muted"));
        Assert.NotEmpty(heading.QuerySelectorAll(":scope > .console-button-row > :is(a, button)"));
        Assert.Empty(page.FindAll("[aria-labelledby='esri-intake-heading']"));
        var preview = page.Find("[data-esri-target-preview]");
        Assert.Equal("ASIDE", preview.TagName);
        Assert.NotNull(preview.QuerySelector(":scope > .esri-import__preview-head > .console-kicker"));
        var mapping = page.Find("[data-esri-mapping]");
        Assert.Equal("DIV", mapping.TagName);
        Assert.NotNull(mapping.QuerySelector(".esri-mapping__heading > h3"));
        Assert.Null(mapping.GetAttribute("aria-labelledby"));
    }

    [Fact]
    public void WizardPage_V4PutsTheSourceContextBesideTheHeading()
    {
        using var ctx = NewWizardContext(v4: true);
        var page = ctx.Render<ImportEsriWizardPage>();

        Assert.Equal("Import source content", page.Find(".console-page-heading-v4 h1").TextContent);
        Assert.NotNull(page.Find(".console-page-heading-v4 .console-page-heading-actions .esri-wizard__context"));
        Assert.Empty(page.FindAll(".console-detail-heading"));
        Assert.Single(page.FindAll("[data-publish-wizard]"));
        CatalogAdminPreview.Capture("data-esri-wizard", page.Markup);
    }

    [Fact]
    public void WizardPage_LegacyKeepsTheBreadcrumbHeading()
    {
        using var ctx = NewWizardContext(v4: false);
        var page = ctx.Render<ImportEsriWizardPage>();

        var heading = page.Find(".console-page > .console-detail-heading");
        Assert.Equal("Operate / Import source content", Normalize(heading.QuerySelector(".console-kicker")!.TextContent));
        Assert.Equal("Import source content", heading.QuerySelector("h1")!.TextContent);
        Assert.NotNull(heading.QuerySelector(":scope > .esri-wizard__context"));
        Assert.Single(page.FindAll("[data-publish-wizard]"));
    }

    private static BunitContext NewWizardContext(bool v4)
    {
        var ctx = NewContext(v4);
        ctx.Services.AddSingleton<IEsriMigrationRunDataSource>(new UnsupportedEsriMigrationRunDataSource());
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("operate/import/esri?step=0");
        return ctx;
    }

    private static (Type Type, string Title, string Crumb, string Noun) Page(string slug) => slug switch
    {
        "dashboard" => (typeof(ImportEsriDashboardPage), "Import source dashboard", "Dashboard", "Dashboard"),
        "instant-app" => (typeof(ImportEsriInstantAppPage), "Import source Instant App", "Instant App", "Instant App"),
        "notebook" => (typeof(ImportEsriNotebookPage), "Import source notebook", "Notebook", "Notebook"),
        "web-map" => (typeof(ImportEsriWebMapPage), "Import source Web Map", "Web Map", "Web Map"),
        "storymap" => (typeof(ImportStoryMapPage), "Import StoryMap / Hub site", "StoryMap / Hub", "StoryMap"),
        _ => throw new ArgumentOutOfRangeException(nameof(slug)),
    };

    private static string Normalize(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static BunitContext NewContext(bool v4)
    {
        var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = v4 });
        ctx.Services.AddSingleton<IPublishingWorkspaceDataSource>(new UnsupportedPublishingWorkspaceDataSource());
        return ctx;
    }
}
