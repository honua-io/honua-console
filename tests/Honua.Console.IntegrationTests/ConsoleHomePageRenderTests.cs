using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render regression for the repositioned console home (#193). The landing is
/// no longer a dead link grid: it leads with the approval-inbox summary (pending agent
/// work) and a recent-activity panel, then the area work surfaces. These tests assert the
/// inbox band binds the aggregated queue and deep-links into the full inbox, and that the
/// four area cards still render as entry points.
/// </summary>
public sealed class ConsoleHomePageRenderTests
{
    private static BunitContext NewContext(FakeConsoleProposalsClient proposals)
    {
        var ctx = new BunitContext();
        ctx.Services.AddSingleton<IConsoleEnvironmentProfileStore>(
            new InMemoryConsoleEnvironmentProfileStore([]));
        ctx.Services.AddSingleton<IConsoleProposalsClient>(proposals);
        ctx.Services.AddSingleton<IConsoleApprovalInboxClient>(new ConsoleApprovalInboxClient(proposals));
        ctx.Services.AddSingleton<IConsoleHostCapabilities>(
            new BrowserConsoleHostCapabilities());
        return ctx;
    }

    [Fact]
    public void Home_LeadsWithInboxSummary_AndDeepLinksToInbox()
    {
        using var ctx = NewContext(new FakeConsoleProposalsClient(proposals: []));

        var page = ctx.Render<ConsoleHomePage>();

        page.WaitForAssertion(
            () =>
            {
                Assert.Equal("0", page.Find("[data-home-awaiting-count] strong").TextContent.Trim());
                Assert.Equal("/inbox", page.Find("[data-open-inbox]").GetAttribute("href"));
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Home_StillRendersTheFourAreaWorkSurfaces()
    {
        using var ctx = NewContext(new FakeConsoleProposalsClient(proposals: []));

        var page = ctx.Render<ConsoleHomePage>();

        page.WaitForAssertion(
            () =>
            {
                foreach (var area in ConsoleRouteMap.Areas)
                {
                    Assert.Contains(area.Name, page.Markup, StringComparison.Ordinal);
                }
            },
            TimeSpan.FromSeconds(5));
    }
}
