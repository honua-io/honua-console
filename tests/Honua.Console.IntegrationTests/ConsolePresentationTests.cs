using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Layout;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class ConsolePresentationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public void LayoutOnlyEnablesV4StylesWhenTheSingleRolloutSwitchIsEnabled(bool? enabled)
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.AddConsoleNotifications();
        context.Services.AddSingleton(new CatalogAdminOptions());
        context.Services.AddSingleton<IConsoleProductMode>(new ConfiguredConsoleProductMode(ConsoleProductMode.Full));
        context.Services.AddSingleton<IConsoleCapabilityManifest>(ConsoleCapabilityManifest.FromConfigurationList(null));
        context.AddAuthorization().SetNotAuthorized();
        if (enabled.HasValue)
        {
            context.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled.Value });
        }

        var page = context.Render<ConsoleLayout>();

        Assert.Equal(enabled == true ? "true" : null, page.Find(".console-shell").GetAttribute("data-console-v4"));
        Assert.Single(page.FindAll("#console-main"));
        Assert.Equal("#console-main", page.Find(".console-skip-link").GetAttribute("href"));
    }

    [Theory]
    [InlineData("full")]
    [InlineData("witness")]
    public void ModesKeepFocusedNavigationAndShowPreviewBannerOutsideFocusedRoutes(string mode)
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.AddConsoleNotifications();
        context.Services.AddSingleton(new CatalogAdminOptions());
        context.Services.AddSingleton<IConsoleProductMode>(
            new ConfiguredConsoleProductMode(ConsoleProductModeParser.Parse(mode)));
        context.Services.AddSingleton<IConsoleCapabilityManifest>(ConsoleCapabilityManifest.FromConfigurationList(null));
        context.AddAuthorization().SetAuthorized("synthetic-operator");
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/operate/settings");
        var page = context.Render<ConsoleLayout>();
        Assert.Equal(mode, page.Find("[data-console-mode]").GetAttribute("data-console-mode"));
        Assert.NotEmpty(page.FindAll("a[href='/inbox']"));
        Assert.NotEmpty(page.FindAll("a[href='/operate/connections']"));
        Assert.Contains("Preview", page.Find("[data-focused-preview]").TextContent);
    }

    [Theory]
    [InlineData("/operate/geoprocessing/job-1", true)]
    [InlineData("/operate/deploy?operationId=op-1", true)]
    [InlineData("/catalog/map-1?tab=versions", true)]
    [InlineData("/studio/map", false)]
    [InlineData("/operate/scenes", false)]
    [InlineData("/operate/settings", false)]
    [InlineData("/operate/services-unrelated", false)]
    public void FocusedPresentationUsesRouteBoundaries(string path, bool expected) =>
        Assert.Equal(expected, ConsolePresentation.IsFocusedRoute(path));

    [Fact]
    public void InvalidModeIsAConfigurationError() => Assert.Throws<ArgumentException>(() => new ConsolePresentation("admin"));
}
