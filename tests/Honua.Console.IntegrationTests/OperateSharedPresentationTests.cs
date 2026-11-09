using Bunit;
using Honua.Console.TestFixtures;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class OperateSharedPresentationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedSurfacesKeepEventIdentityAndProposalEvidence(bool enabled)
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        context.Services.AddSingleton<IConsoleProposalsClient>(new ReceiptProposalClient());
        context.SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var entry = new OperateTimelineEntry("Publish", "info", "Published candidate map", "2026-11-09 09:36 UTC",
            "correlation-map-publish-1", EventId: "event-1", DetailHref: "/operate/events/event-1");
        var timeline = context.Render<OperateTimeline>(p => p.Add(c => c.Entries, new[] { entry, entry }));
        var approval = context.Render<ProposalApprovalPanel>(p => p.Add(c => c.ProposalId, "proposal-1"));
        approval.WaitForAssertion(() => Assert.Equal("AwaitingApproval",
            approval.Find("[data-proposal-status]").GetAttribute("data-proposal-status")));
        Assert.Single(timeline.FindAll("[data-timeline-row]"));
        Assert.Equal("/operate/events/event-1", timeline.Find("td a").GetAttribute("href"));
        Assert.Contains("Publish candidate map", approval.Markup, StringComparison.Ordinal);
        Assert.NotEmpty(approval.FindAll("[data-proposal-approve]:disabled"));
        var name = $"operate-shared-v4-{enabled.ToString().ToLowerInvariant()}";
        CatalogAdminPreview.Capture(name, $"<div class=\"console-shell\" data-console-v4=\"{enabled.ToString().ToLowerInvariant()}\"><main class=\"console-main\"><h1>Activity and approvals</h1><h2>Events</h2>{timeline.Markup}<h2>Proposal review</h2>{approval.Markup}</main></div>");
    }
}
