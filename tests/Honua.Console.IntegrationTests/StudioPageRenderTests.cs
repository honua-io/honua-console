using Bunit;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class StudioPageRenderTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CapabilityCannotRestoreRetiredAuthoringProduct(bool oldCapability, bool v4Enabled)
    {
        using var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = v4Enabled });
        context.Services.AddSingleton<IConsoleCapabilityManifest>(oldCapability ? ConsoleCapabilityTestManifest.All : new ConsoleCapabilityManifest());
        var page = context.Render<StudioPage>();
        Assert.Single(page.FindAll("[data-studio-unavailable]"));
        Assert.Empty(page.FindAll("[data-authoring-contract], [data-studio-home], textarea"));
        Assert.DoesNotContain("studio-builders", page.Markup, StringComparison.Ordinal);
        Assert.Contains("/environments", page.Markup, StringComparison.Ordinal);
        AssertPresentation(page, v4Enabled ? 1 : 0);
        if (v4Enabled) Capture("studio-unavailable", page.Markup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfiguredRealtimeWorkspaceUsesHostWithoutLegacyShell(bool v4Enabled)
    {
        using var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = v4Enabled });
        context.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Studio:EmbedJsStudio"] = "true" }).Build());
        var page = context.Render<StudioPage>();
        Assert.Single(page.FindAll("[data-studio-embed]"));
        Assert.Single(page.FindAll("[data-studio-mount]"));
        Assert.Single(page.FindAll("[data-studio-embed] > [data-studio-status]"));
        Assert.Empty(page.FindAll("[data-authoring-contract], textarea"));
        AssertPresentation(page, v4Enabled ? 1 : 0);
        if (v4Enabled) Capture("studio-connected", page.Markup);
    }

    [Theory]
    [InlineData("/studio/proof?prompt=do-not-submit", false)]
    [InlineData("/studio/proof?prompt=do-not-submit", true)]
    [InlineData("/studio/apps/old-app/preview", false)]
    [InlineData("/studio/apps/old-app/preview", true)]
    public void RetiredDeepLinkOffersExplicitWorkspaceWithoutSubmittingPrompt(string path, bool v4Enabled)
    {
        using var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = v4Enabled });
        context.Services.GetRequiredService<NavigationManager>().NavigateTo(path);
        var page = context.Render<StudioPage>();
        Assert.Single(page.FindAll("[data-legacy-studio-retired]"));
        Assert.NotEmpty(page.FindAll("a[href='/studio']"));
        Assert.Single(page.FindAll("a[href='/catalog']"));
        AssertPresentation(page, v4Enabled ? 1 : 0);
        Assert.Empty(page.FindAll("[data-studio-embed], textarea"));
        Assert.DoesNotContain("do-not-submit", page.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/studio/proof", false)]
    [InlineData("/studio/proof", true)]
    [InlineData("/studio/apps/old-app/preview", false)]
    [InlineData("/studio/apps/old-app/preview", true)]
    public void InteractiveReadinessSurvivesNavigationFromRetiredRoute(string path, bool v4Enabled)
    {
        using var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = v4Enabled });
        context.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Studio:EmbedJsStudio"] = "true" }).Build());
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(path);
        var page = context.Render<StudioPage>();
        Assert.Single(page.FindAll("[data-legacy-studio-retired]"));

        navigation.NavigateTo("/studio");
        page.WaitForAssertion(() =>
        {
            Assert.Single(page.FindAll("[data-studio-embed]"));
            Assert.Single(page.FindAll("[data-studio-mount]"));
            Assert.Empty(page.FindAll("[data-legacy-studio-retired]"));
            AssertPresentation(page, v4Enabled ? 1 : 0);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyBuilderRoutesRenderTheRetiredNotice(bool v4Enabled)
    {
        var routes = typeof(RetiredStudioBuilderPage).GetCustomAttributes(typeof(RouteAttribute), false).Cast<RouteAttribute>().ToArray();
        Assert.Equal(8, routes.Length);
        using var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = v4Enabled });
        var page = context.Render<RetiredStudioBuilderPage>();
        Assert.Single(page.FindAll("[data-legacy-studio-retired]"));
        Assert.Single(page.FindAll("a[href='/studio']"));
        Assert.Single(page.FindAll("a[href='/studio/processes']"));
        Assert.Empty(page.FindAll("textarea, button"));
        AssertPresentation(page, v4Enabled ? 1 : 0);
        if (v4Enabled) Capture("studio-retired", page.Markup);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ProcessesWorkspacePreservesItsDeploymentGateAndMounts(bool embedEnabled, bool v4Enabled)
    {
        using var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = v4Enabled });
        context.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Studio:EmbedJsStudio"] = embedEnabled.ToString() }).Build());

        var page = context.Render<ProcessesWorkspacePage>();

        if (embedEnabled)
        {
            Assert.Equal("Processes", page.Find("h1").TextContent);
            Assert.Single(page.FindAll("[data-processes-embed] > [data-processes-status]"));
            Assert.Single(page.FindAll("[data-processes-builtin]"));
            Assert.Single(page.FindAll("[data-processes-custom]"));
            Assert.Contains("Connecting", page.Find("[data-processes-status]").TextContent, StringComparison.Ordinal);
            AssertPresentation(page, v4Enabled ? 1 : 0);
        }
        else
        {
            Assert.Contains("Realtime Studio is not enabled", page.Markup, StringComparison.Ordinal);
            Assert.Empty(page.FindAll("[data-processes-builtin], [data-processes-custom]"));
            Assert.Single(page.FindAll("h1"));
        }
        if (v4Enabled) Capture(embedEnabled ? "processes-connected" : "processes-disabled", page.Markup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrerenderPreservesConnectingStatusBeforeInteractiveMount(bool v4Enabled)
    {
        using var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = v4Enabled });
        context.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Studio:EmbedJsStudio"] = "true" }).Build());
        await using var renderer = new HtmlRenderer(context.Services, context.Services.GetRequiredService<ILoggerFactory>());
        var markup = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<StudioPage>()).ToHtmlString());
        var document = new HtmlParser().ParseDocument(markup);

        Assert.Empty(document.QuerySelectorAll("[data-studio-embed]"));
        if (v4Enabled)
        {
            Assert.Equal("Studio", Assert.Single(document.QuerySelectorAll("h1")).TextContent);
            Assert.Single(document.QuerySelectorAll("p[role=status]"));
        }
        else
        {
            Assert.Single(document.QuerySelectorAll("section.console-page[role=status]"));
            Assert.Empty(document.QuerySelectorAll("h1"));
        }
    }

    private static void AssertPresentation<T>(IRenderedComponent<T> page, int expectedV4Headings) where T : IComponent
    {
        Assert.Single(page.FindAll("h1"));
        Assert.Equal(expectedV4Headings, page.FindAll(".console-page-heading-v4").Count);
    }

    private static void Capture(string name, string markup) =>
        CatalogAdminPreview.Capture(name, $"<div class=\"console-shell\" data-console-v4=\"true\">{markup}</div>");
}
