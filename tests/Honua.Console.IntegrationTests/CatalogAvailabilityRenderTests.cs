using Bunit;
using Honua.Console.Shell.DependencyInjection;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class CatalogAvailabilityRenderTests
{
    [Fact]
    public void WithoutAPersistentCatalog_CatalogPagesPointToDataAndLayers()
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddHonuaConsoleShell();
        ctx.Services.AddSingleton<IConsoleCapabilityManifest>(new ConsoleCapabilityManifest());

        var page = ctx.Render<CatalogPage>();

        Assert.Equal(CatalogUnavailable.Title, page.Find("h1").TextContent);
        Assert.Equal("/operate/data", page.Find("a.console-button-link").GetAttribute("href"));
    }

    [Theory]
    [InlineData(false, new[] { "studio", "operate" })]
    [InlineData(true, new[] { "studio", "catalog", "operate", "share" })]
    public void CatalogAndShareAreasAppearOnlyWithAPersistentCatalog(bool persistent, string[] expected)
    {
        var capabilities = new ConsoleCapabilityManifest(persistent ? [ConsoleCapabilityKeys.PersistentCatalog] : []);

        var visible = ConsoleRouteMap.Areas.Where(area => area.IsAvailable(capabilities)).Select(area => area.Id);

        Assert.Equal(expected, visible);
    }
}
