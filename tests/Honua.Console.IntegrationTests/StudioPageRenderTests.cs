using Bunit;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class StudioPageRenderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CapabilityCannotRestoreRetiredAuthoringProduct(bool oldCapability)
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<IConsoleCapabilityManifest>(oldCapability ? ConsoleCapabilityTestManifest.All : new ConsoleCapabilityManifest());
        var page = context.Render<StudioPage>();
        Assert.Single(page.FindAll("[data-studio-unavailable]"));
        Assert.Empty(page.FindAll("[data-authoring-contract], [data-studio-home], textarea"));
        Assert.DoesNotContain("studio-builders", page.Markup, StringComparison.Ordinal);
        Assert.Contains("/environments", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfiguredRealtimeWorkspaceUsesHostWithoutLegacyShell()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Studio:EmbedJsStudio"] = "true" }).Build());
        var page = context.Render<StudioPage>();
        Assert.Single(page.FindAll("[data-studio-embed]"));
        Assert.Single(page.FindAll("[data-studio-mount]"));
        Assert.Empty(page.FindAll("[data-authoring-contract], textarea"));
    }

    [Theory]
    [InlineData("/studio/proof?prompt=do-not-submit")]
    [InlineData("/studio/apps/old-app/preview")]
    public void RetiredDeepLinkOffersExplicitWorkspaceWithoutSubmittingPrompt(string path)
    {
        using var context = new BunitContext();
        context.Services.GetRequiredService<NavigationManager>().NavigateTo(path);
        var page = context.Render<StudioPage>();
        Assert.Single(page.FindAll("[data-legacy-studio-retired]"));
        Assert.NotEmpty(page.FindAll("a[href='/studio']"));
        Assert.Empty(page.FindAll("[data-studio-embed], textarea"));
        Assert.DoesNotContain("do-not-submit", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyBuilderRoutesCannotBeResurrectedByCapability()
    {
        Type[] legacy = [typeof(StudioMapBuilderPage),typeof(StudioAppBuilderPage),typeof(StudioDashboardBuilderPage),
            typeof(StudioAnalysisBuilderPage),typeof(StudioQueryBuilderPage),typeof(StudioFormAiPage),
            typeof(StudioReportAiPage),typeof(StudioWorkflowAiPage)];
        Assert.All(legacy, component => Assert.Empty(component.GetCustomAttributes(typeof(RouteAttribute), false)));
        var routes = typeof(RetiredStudioBuilderPage).GetCustomAttributes(typeof(RouteAttribute), false).Cast<RouteAttribute>().ToArray();
        Assert.Equal(8, routes.Length);
        using var context = new BunitContext();
        var page = context.Render<RetiredStudioBuilderPage>();
        Assert.Single(page.FindAll("[data-legacy-studio-retired]"));
        Assert.Empty(page.FindAll("textarea, button"));
    }
}
