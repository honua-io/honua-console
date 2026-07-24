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
    public void Home_WhenEveryProposalSourceIsUnavailable_DoesNotShowAZeroQueue()
    {
        const string serverMessage = "No active environment profile is selected.";
        var proposals = new FakeConsoleProposalsClient(
            deniedListStatus: OperateSectionStatus.Unavailable,
            deniedListMessage: serverMessage);
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<IConsoleEnvironmentProfileStore>(
            new InMemoryConsoleEnvironmentProfileStore([]));
        ctx.Services.AddSingleton<IConsoleApprovalInboxClient>(
            new ConsoleApprovalInboxClient(
            [
                new ServerConsoleProposalSource(proposals),
                new DevOpsConsoleProposalSource(new UnavailableConsoleDevOpsProposalsClient()),
            ]));
        ctx.Services.AddSingleton<IConsoleHostCapabilities>(new BrowserConsoleHostCapabilities());

        var page = ctx.Render<ConsoleHomePage>();

        page.WaitForAssertion(
            () =>
            {
                Assert.Contains(serverMessage, page.Markup, StringComparison.Ordinal);
                Assert.DoesNotContain("data-home-awaiting-count", page.Markup, StringComparison.Ordinal);
                Assert.DoesNotContain("data-home-total-count", page.Markup, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Home_WhenServerIsReachableAndDevOpsIsUnavailable_DisclosesPartialQueue()
    {
        var proposals = new FakeConsoleProposalsClient(
            proposals:
            [
                FakeProposalFactory.Summary(
                    "server-op",
                    ConsoleProposalKind.MetadataRelease,
                    summary: "Promote parcels")
            ]);
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<IConsoleEnvironmentProfileStore>(
            new InMemoryConsoleEnvironmentProfileStore([]));
        ctx.Services.AddSingleton<IConsoleApprovalInboxClient>(
            new ConsoleApprovalInboxClient(
            [
                new ServerConsoleProposalSource(proposals),
                new DevOpsConsoleProposalSource(new UnavailableConsoleDevOpsProposalsClient()),
            ]));
        ctx.Services.AddSingleton<IConsoleHostCapabilities>(new BrowserConsoleHostCapabilities());

        var page = ctx.Render<ConsoleHomePage>();

        page.WaitForAssertion(
            () =>
            {
                Assert.Equal("1", page.Find("[data-home-awaiting-count] strong").TextContent.Trim());
                Assert.Equal("1", page.Find("[data-home-total-count] strong").TextContent.Trim());
                Assert.Contains(
                    "DevOps proposal source is unavailable",
                    page.Find("[data-home-inbox-partial]").TextContent,
                    StringComparison.Ordinal);
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

    [Fact]
    public void HomeInboxBand_ErrorRead_ShowsExplicitErrorWithRetry_AndLastRefreshed()
    {
        // console#308: the home approval band uses the same bounded-loading error state as the
        // inbox — an Unavailable read renders an explicit error naming the source, a Retry, and the
        // persistent last-refreshed marker (never loaded), distinct from an empty-success band.
        var ctx = new BunitContext();
        ctx.Services.AddSingleton<IConsoleEnvironmentProfileStore>(new InMemoryConsoleEnvironmentProfileStore([]));
        ctx.Services.AddSingleton<IConsoleApprovalInboxClient>(new ScriptedApprovalInboxClient
        {
            Result = OperateSectionResult<ApprovalInboxSnapshot>.Denied(
                OperateSectionStatus.Unavailable, "The honua-server admin API returned 500."),
        });
        ctx.Services.AddSingleton<IConsoleHostCapabilities>(new BrowserConsoleHostCapabilities());

        var page = ctx.Render<ConsoleHomePage>();

        page.WaitForAssertion(
            () =>
            {
                var error = page.Find("[data-home-inbox-error]");
                Assert.Contains("Couldn't read the approval queue", error.TextContent, StringComparison.Ordinal);
                Assert.NotNull(page.Find("[data-home-inbox-retry]"));
                Assert.Contains("Never loaded", page.Find("[data-home-inbox-last-refreshed]").TextContent, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));

        ctx.Dispose();
    }

    [Fact]
    public void Home_SurvivesWithoutRealtimeOrOpsSummaryServices_AndRendersTheirDegradedStates()
    {
        // console#292 regression (PR #295 CI): the home page embeds the ops-summary strip and a
        // live inbox band, but this harness registers neither the realtime client nor the strip's
        // health/findings clients. The page must resolve those optionally and render the honest
        // degraded states — Manual pill, unavailable health, em-dashed counts — never throw
        // during render.
        using var ctx = NewContext(new FakeConsoleProposalsClient(proposals: []));

        var page = ctx.Render<ConsoleHomePage>();

        page.WaitForAssertion(
            () =>
            {
                // Inbox band: no realtime client registered -> honest paused freshness signal
                // (console#309), never a fake Live pill.
                Assert.Contains("Updates paused", page.Find("[data-home-live-state]").TextContent, StringComparison.Ordinal);

                // Ops-summary strip: health data source is unregistered -> unavailable, not a
                // fabricated value; findings count is em-dashed; approvals still binds through
                // the registered inbox client (an allowed empty read -> 0).
                Assert.Equal("Unavailable", page.Find("[data-summary-unavailable='health']").TextContent.Trim());
                Assert.Equal("—", page.Find("[data-summary-value='findings']").TextContent.Trim());
                Assert.Equal("—", page.Find("[data-summary-value='breaches']").TextContent.Trim());
                Assert.Equal("0", page.Find("[data-summary-value='approvals']").TextContent.Trim());

                // Approvals liveness stays honest: no realtime client means manual refresh.
                Assert.DoesNotContain("is-live", page.Find("[data-summary-liveness]").ClassList);
            },
            TimeSpan.FromSeconds(5));
    }
}
