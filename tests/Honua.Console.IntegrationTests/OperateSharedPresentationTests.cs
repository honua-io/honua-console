using Bunit;
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
        context.Services.AddSingleton<IConsoleProposalsClient>(new ProposalClient());
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

    private sealed class ProposalClient(ConsoleProposalStatus initialStatus = ConsoleProposalStatus.AwaitingApproval)
        : IConsoleProposalsClient
    {
        public Task<OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>> ListAsync(
            string? status = null,
            string? kind = null,
            string? requestedBy = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>.Allowed([]));

        public Task<OperateSectionResult<ConsoleProposalDetail>> GetAsync(
            string proposalId,
            CancellationToken cancellationToken = default) => Task.FromResult(Allowed(initialStatus));

        public Task<OperateSectionResult<ConsoleProposalDetail>> ApproveAsync(
            string proposalId,
            CancellationToken cancellationToken = default) => Task.FromResult(Allowed(ConsoleProposalStatus.Submitted));

        public Task<OperateSectionResult<ConsoleProposalDetail>> RejectAsync(
            string proposalId,
            string reason,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<ConsoleProposalDetail>.Denied(OperateSectionStatus.Unsupported, "not used"));

        private static OperateSectionResult<ConsoleProposalDetail> Allowed(ConsoleProposalStatus status) =>
            OperateSectionResult<ConsoleProposalDetail>.Allowed(new(
                "proposal-1",
                ConsoleProposalKind.MetadataRelease,
                status,
                "agent",
                "agent",
                "Publish candidate map",
                ["item-1", "version-1", "draft-1", "map-route"],
                [],
                ConsoleProposalRisk.Low,
                [],
                [],
                "human",
                status == ConsoleProposalStatus.AwaitingApproval ? null : "operator",
                null,
                status == ConsoleProposalStatus.AwaitingApproval ? null : "operation-1",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                status == ConsoleProposalStatus.AwaitingApproval ? null : DateTimeOffset.UtcNow));
    }

}
