using Bunit;
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
        Assert.Equal(v4Enabled, page.FindAll(".console-studio-v4").Count == 1);
        Assert.Empty(page.FindAll("[data-authoring-contract], [data-studio-home], textarea"));
        Assert.DoesNotContain("studio-builders", page.Markup, StringComparison.Ordinal);
        Assert.Contains("/environments", page.Markup, StringComparison.Ordinal);
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
        Assert.Equal(v4Enabled, page.FindAll(".console-studio-v4").Count == 1);
        Assert.Empty(page.FindAll("[data-authoring-contract], textarea"));
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
            Assert.Equal(v4Enabled, page.FindAll(".console-studio-v4").Count == 1);
            Assert.Empty(page.FindAll("[data-legacy-studio-retired]"));
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
        Assert.Equal(v4Enabled, page.FindAll(".console-studio-v4").Count == 1);
        Assert.Single(page.FindAll("a[href='/studio']"));
        Assert.Single(page.FindAll("a[href='/studio/processes']"));
        Assert.Empty(page.FindAll("textarea, button"));
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
            Assert.Single(page.FindAll("[data-processes-builtin]"));
            Assert.Single(page.FindAll("[data-processes-custom]"));
            Assert.Contains("Connecting", page.Find("[data-processes-status]").TextContent, StringComparison.Ordinal);
            Assert.Equal(v4Enabled, page.FindAll(".console-studio-v4").Count == 1);
        }
        else
        {
            Assert.Contains("Realtime Studio is not enabled", page.Markup, StringComparison.Ordinal);
            Assert.Empty(page.FindAll("[data-processes-builtin], [data-processes-custom]"));
        }
    }
}
