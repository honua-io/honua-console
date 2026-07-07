using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;

namespace Honua.Console.Native.Core.Tests.Components;

/// <summary>
/// Coverage for the shared operate timeline (console#293): idempotent append keyed on eventId
/// (falling back to operationId+transitionKind), and the shared severity pill rendering.
/// </summary>
public sealed class OperateTimelineTests : ConsoleComponentTestBase
{
    private static OperateTimelineEntry Entry(
        string eventId,
        string kind = "job",
        string severity = "warning",
        string message = "Something happened",
        string correlationId = "corr-1",
        string timestamp = "2026-07-07 09:00 UTC") =>
        new(kind, severity, message, timestamp, correlationId, EventId: eventId, DetailHref: $"/operate/events/{eventId}");

    [Fact]
    public void DuplicateEventId_IsAppendedOnce()
    {
        // The server's transition/event feed is at-least-once (honua-server PR #2577 review
        // note): the same eventId can legitimately appear twice in the Entries the page passes
        // (an initial read plus a live push of the same event, or two live pushes). The timeline
        // must render it exactly once.
        var entries = new[]
        {
            Entry("evt-1", message: "First delivery"),
            Entry("evt-2", message: "Unrelated event"),
            Entry("evt-1", message: "Duplicate delivery of the same event"),
        };

        var cut = Render<OperateTimeline>(p => p.Add(c => c.Entries, entries));

        var rows = cut.FindAll("[data-timeline-row]");
        Assert.Equal(2, rows.Count);

        // The first occurrence wins; the duplicate delivery's payload is dropped, not merged.
        Assert.Contains("First delivery", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Duplicate delivery of the same event", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingEventId_DedupesOnOperationIdAndTransitionKind()
    {
        var entries = new[]
        {
            new OperateTimelineEntry(
                Kind: "deploy", Severity: "info", Message: "Reconciling",
                Timestamp: "2026-07-07 09:00 UTC", CorrelationId: "corr-2",
                OperationId: "op-1", TransitionKind: "Reconciling"),
            new OperateTimelineEntry(
                Kind: "deploy", Severity: "info", Message: "Reconciling (re-delivered)",
                Timestamp: "2026-07-07 09:00 UTC", CorrelationId: "corr-2",
                OperationId: "op-1", TransitionKind: "Reconciling"),
        };

        var cut = Render<OperateTimeline>(p => p.Add(c => c.Entries, entries));

        Assert.Single(cut.FindAll("[data-timeline-row]"));
    }

    [Fact]
    public void DistinctTransitionKinds_ForSameOperation_AreNotCollapsed()
    {
        var entries = new[]
        {
            new OperateTimelineEntry(
                Kind: "deploy", Severity: "info", Message: "Submitted",
                Timestamp: "2026-07-07 09:00 UTC", CorrelationId: "corr-3",
                OperationId: "op-2", TransitionKind: "Submitted"),
            new OperateTimelineEntry(
                Kind: "deploy", Severity: "healthy", Message: "Succeeded",
                Timestamp: "2026-07-07 09:05 UTC", CorrelationId: "corr-3",
                OperationId: "op-2", TransitionKind: "Succeeded"),
        };

        var cut = Render<OperateTimeline>(p => p.Add(c => c.Entries, entries));

        Assert.Equal(2, cut.FindAll("[data-timeline-row]").Count);
    }

    [Fact]
    public void EmptyEntries_RendersEmptyMessage()
    {
        var cut = Render<OperateTimeline>(p => p
            .Add(c => c.Entries, Array.Empty<OperateTimelineEntry>())
            .Add(c => c.EmptyMessage, "Nothing here yet."));

        Assert.Equal("Nothing here yet.", cut.Find("[data-timeline-empty]").TextContent.Trim());
    }

    [Fact]
    public void SelectedEntryKey_HighlightsTheMatchingRow()
    {
        var entries = new[] { Entry("evt-1"), Entry("evt-2") };
        var target = entries[1].DedupeKey;

        var cut = Render<OperateTimeline>(p => p
            .Add(c => c.Entries, entries)
            .Add(c => c.SelectedEntryKey, target));

        var selected = cut.Find($"[data-timeline-key=\"{target}\"]");
        Assert.Contains("operate-selected-row", selected.ClassList);
    }

    [Fact]
    public void RowRendersSeverityThroughTheSharedStatusPill()
    {
        var cut = Render<OperateTimeline>(p => p
            .Add(c => c.Entries, new[] { Entry("evt-1", severity: "critical") }));

        var pill = cut.Find("[data-timeline-row] .console-status");
        Assert.Contains("console-state-danger", pill.ClassList);
    }
}
