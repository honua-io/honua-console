using Bunit;
using Honua.Console.Shell.Layout;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class ConsolePresentationTests
{
    [Theory]
    [InlineData("full", true)]
    [InlineData("witness", false)]
    public void ModesKeepFocusedNavigationAndLabelOrHideBroadAdministration(string mode, bool showsAdministration)
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.AddConsoleNotifications();
        context.Services.AddSingleton(new ConsolePresentation(mode));
        context.Services.AddSingleton<IConsoleHostCapabilities, BrowserConsoleHostCapabilities>();
        context.Services.AddSingleton<IConsoleCapabilityManifest>(ConsoleCapabilityManifest.FromConfigurationList(null));
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/operate/settings");
        var page = context.Render<ConsoleLayout>();
        Assert.Equal(mode, page.Find("[data-console-mode]").GetAttribute("data-console-mode"));
        Assert.NotEmpty(page.FindAll("a[href='/inbox']"));
        Assert.NotEmpty(page.FindAll("a[href='/operate/connections']"));
        Assert.Equal(showsAdministration, page.FindAll("a[href='/operate/settings']").Count > 0);
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
