using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;

namespace Honua.Console.Native.Core.Tests.Components;

/// <summary>
/// Coverage for the shared correlation-id chip (console#292 incident golden path): it deep-links
/// every correlationId / operationId / eventId / findingId / proposalId onto the Console surface
/// that owns it, so an operator never hand-copies an id between health, copilot, inbox, deploy,
/// and timeline surfaces.
/// </summary>
public sealed class CorrelationIdChipTests : ConsoleComponentTestBase
{
    [Fact]
    public void EventId_LinksToEventDetail()
    {
        var cut = Render<CorrelationIdChip>(p => p
            .Add(c => c.Value, "evt-alert-900")
            .Add(c => c.Kind, CorrelationIdKind.EventId));

        var link = cut.Find("[data-correlation-chip]");
        Assert.Equal("/operate/events/evt-alert-900", link.GetAttribute("href"));
        Assert.Equal("evt-alert-900", link.TextContent.Trim());
    }

    [Fact]
    public void CorrelationId_LinksToTheFilteredEvidenceTimeline()
    {
        var cut = Render<CorrelationIdChip>(p => p
            .Add(c => c.Value, "corr-rel-20260524")
            .Add(c => c.Kind, CorrelationIdKind.CorrelationId));

        var link = cut.Find("[data-correlation-chip]");
        Assert.Equal("/operate/observability?correlationId=corr-rel-20260524#events", link.GetAttribute("href"));
    }

    [Fact]
    public void FindingId_LinksToTheAnchoredFindingOnCopilotFindings()
    {
        var cut = Render<CorrelationIdChip>(p => p
            .Add(c => c.Value, "platform-release-skew-abc123")
            .Add(c => c.Kind, CorrelationIdKind.FindingId));

        var link = cut.Find("[data-correlation-chip]");
        Assert.Equal("/operate/copilot#finding-platform-release-skew-abc123", link.GetAttribute("href"));
    }

    [Fact]
    public void ProposalId_LinksToTheApprovalInboxPreselected()
    {
        var cut = Render<CorrelationIdChip>(p => p
            .Add(c => c.Value, "prop-42")
            .Add(c => c.Kind, CorrelationIdKind.ProposalId));

        var link = cut.Find("[data-correlation-chip]");
        Assert.Equal("/inbox?proposalId=prop-42", link.GetAttribute("href"));
    }

    [Fact]
    public void OperationId_LinksToTheDeployPageTrackingTheOperation()
    {
        var cut = Render<CorrelationIdChip>(p => p
            .Add(c => c.Value, "deploy-op-2f8c")
            .Add(c => c.Kind, CorrelationIdKind.OperationId));

        var link = cut.Find("[data-correlation-chip]");
        Assert.Equal("/operate/deploy?operationId=deploy-op-2f8c#deploy-approvals", link.GetAttribute("href"));
    }

    [Fact]
    public void ExplicitHref_OverridesTheKindDerivedRoute()
    {
        var cut = Render<CorrelationIdChip>(p => p
            .Add(c => c.Value, "evt-1")
            .Add(c => c.Kind, CorrelationIdKind.EventId)
            .Add(c => c.Href, "/operate/events/evt-1#custom"));

        var link = cut.Find("[data-correlation-chip]");
        Assert.Equal("/operate/events/evt-1#custom", link.GetAttribute("href"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingValue_RendersMutedDashNotADeadLink(string? value)
    {
        var cut = Render<CorrelationIdChip>(p => p.Add(c => c.Value, value));

        Assert.Empty(cut.FindAll("[data-correlation-chip]"));
        Assert.Equal("—", cut.Find("[data-correlation-chip-empty]").TextContent.Trim());
    }
}
