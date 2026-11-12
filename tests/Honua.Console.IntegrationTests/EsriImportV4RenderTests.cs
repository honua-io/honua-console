using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class EsriImportV4RenderTests
{
    public static TheoryData<string, bool> ContentPages()
    {
        var data = new TheoryData<string, bool>();
        foreach (var page in new[] { "dashboard", "instant-app", "notebook", "web-map", "storymap" })
        {
            data.Add(page, false);
            data.Add(page, true);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ContentPages))]
    public void ContentImportPages_UseV4PanelsOnlyBehindTheFlag(string slug, bool v4)
    {
        using var ctx = NewContext(v4);
        var (type, title, crumb, noun) = Page(slug);
        var page = ctx.Render(builder => { builder.OpenComponent(0, type); builder.CloseComponent(); });

        if (v4) CatalogAdminPreview.Capture($"data-esri-{slug}-empty", page.Markup);
        page.Find("[data-intake-sample]").Click();

        Assert.Equal(title, page.Find("h1").TextContent);
        var mapping = page.Find("[data-esri-mapping]");
        var preview = page.Find("[data-esri-target-preview]");
        if (v4)
        {
            Assert.Single(page.FindAll(".console-page-heading-v4"));
            Assert.Empty(page.FindAll(".console-detail-heading, .console-page-heading-v4 .console-kicker, .esri-import__preview-head"));
            var intake = page.Find("section.console-panel[aria-labelledby='esri-intake-heading']");
            Assert.Equal($"Source {noun}", intake.QuerySelector("h2")!.TextContent);
            Assert.NotNull(intake.QuerySelector("[data-esri-intake]"));
            Assert.Contains("console-panel", mapping.ClassList);
            Assert.Equal("esri-mapping-heading", mapping.GetAttribute("aria-labelledby"));
            Assert.NotNull(mapping.QuerySelector("h2#esri-mapping-heading"));
            Assert.Empty(mapping.QuerySelectorAll("h3"));
            Assert.NotNull(preview.QuerySelector(":scope > section.console-panel > .console-panel-heading > h2"));
            Assert.Empty(page.FindAll(".console-panel .console-panel"));
            CatalogAdminPreview.Capture($"data-esri-{slug}", page.Markup);
        }
        else
        {
            Assert.Empty(page.FindAll(".console-page-heading-v4, .console-panel"));
            Assert.EndsWith($"/ {crumb}", Normalize(page.Find(".console-detail-heading .console-kicker").TextContent));
            Assert.Equal("ASIDE", preview.TagName);
            Assert.NotNull(preview.QuerySelector(".esri-import__preview-head .console-kicker"));
            Assert.NotNull(mapping.QuerySelector("h3"));
            Assert.Null(mapping.GetAttribute("aria-labelledby"));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WizardPage_UsesV4HeadingOnlyBehindTheFlag(bool v4)
    {
        using var ctx = NewContext(v4);
        ctx.Services.AddSingleton<IEsriMigrationRunDataSource>(new UnsupportedEsriMigrationRunDataSource());
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("operate/import/esri?step=0");
        var page = ctx.Render<ImportEsriWizardPage>();

        Assert.Equal("Import source content", page.Find("h1").TextContent);
        Assert.Single(page.FindAll("[data-publish-wizard]"));
        if (v4)
        {
            Assert.NotNull(page.Find(".console-page-heading-v4 .console-page-heading-actions .esri-wizard__context"));
            Assert.Empty(page.FindAll(".console-detail-heading"));
            CatalogAdminPreview.Capture("data-esri-wizard", page.Markup);
        }
        else
        {
            Assert.Equal("Operate / Import source content", Normalize(page.Find(".console-detail-heading .console-kicker").TextContent));
            Assert.NotNull(page.Find(".console-detail-heading .esri-wizard__context"));
        }
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
