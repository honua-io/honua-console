using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class OperateOverviewPresentationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void OverviewPreservesMissingBindingAndCapabilityGates(bool v4Enabled, bool advertiseCapabilities)
    {
        using var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = v4Enabled });
        context.Services.AddSingleton<IConsoleCapabilityManifest>(advertiseCapabilities
            ? ConsoleCapabilityTestManifest.All
            : new ConsoleCapabilityManifest());
        context.Services.AddSingleton<IOperateTransitionDataSource>(new UnsupportedOperateTransitionDataSource());

        var page = context.Render<OperatePage>();

        page.WaitForAssertion(() => Assert.Contains("Missing binding", page.Markup));
        Assert.Equal("Operate", Assert.Single(page.FindAll("h1")).TextContent);
        Assert.Equal(v4Enabled, page.FindAll(".console-operate-overview-v4").Count == 1);
        Assert.Equal(v4Enabled, page.FindAll(".console-page-heading-v4").Count == 1);
        Assert.Equal(v4Enabled ? 1 : 0, page.FindAll("section > link[href='_content/Honua.Console.Shell/css/operate-overview-v4.css']").Count);
        if (!v4Enabled)
        {
            Assert.Contains("Operator workflow", page.Markup);
            Assert.Contains("control-plane settings live under Operate", page.Markup);
        }
        Assert.Single(page.FindAll("[data-ops-summary-strip]"));
        foreach (var route in new[] { "connections", "resources", "services", "publishing", "settings", "releases" })
            Assert.Single(page.FindAll($"a[href='/operate/{route}']"));
        foreach (var route in new[] { "temporal", "sync", "alerts/rules" })
            Assert.Equal(advertiseCapabilities ? 1 : 0, page.FindAll($"a[href='/operate/{route}']").Count);
        Assert.Contains("Honua:Server:BaseUrl", page.Markup);
        Assert.DoesNotContain("County SFTP", page.Markup);
        if (v4Enabled) CatalogAdminPreview.Capture(advertiseCapabilities ? "operate-overview-capabilities" : "operate-overview-missing", page.Markup);
    }
}
