using Honua.Console.Shell.Models;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Pure-function coverage for the shared timeline dedup primitive (console#293) independent of
/// any rendering: <see cref="OperateTimelineEntries.Deduplicate"/> and
/// <see cref="OperateTimelineEntries.Append"/> both key on
/// <see cref="OperateTimelineEntry.DedupeKey"/>, matching the honua-server at-least-once
/// transition/event delivery guarantee (PR #2577 review note).
/// </summary>
public sealed class OperateTimelineModelsTests
{
    private static OperateTimelineEntry Entry(string eventId, string message) =>
        new("job", "info", message, "2026-07-07 09:00 UTC", "corr-1", EventId: eventId);

    [Fact]
    public void Deduplicate_KeepsFirstOccurrence_ForADuplicateEventId()
    {
        var first = Entry("evt-1", "first");
        var duplicate = Entry("evt-1", "duplicate delivery");
        var other = Entry("evt-2", "other");

        var deduped = OperateTimelineEntries.Deduplicate([first, other, duplicate]);

        Assert.Equal(2, deduped.Count);
        Assert.Same(first, deduped[0]);
        Assert.Same(other, deduped[1]);
    }

    [Fact]
    public void Append_IsANoOp_WhenTheDedupeKeyAlreadyExists()
    {
        IReadOnlyList<OperateTimelineEntry> buffer = [Entry("evt-1", "first")];

        var result = OperateTimelineEntries.Append(buffer, Entry("evt-1", "re-delivered"));

        Assert.Same(buffer, result);
        Assert.Single(result);
        Assert.Equal("first", result[0].Message);
    }

    [Fact]
    public void Append_AddsANewEntry_WhenTheDedupeKeyIsNew()
    {
        IReadOnlyList<OperateTimelineEntry> buffer = [Entry("evt-1", "first")];

        var result = OperateTimelineEntries.Append(buffer, Entry("evt-2", "second"));

        Assert.Equal(2, result.Count);
        Assert.Equal("evt-2", result[1].EventId);
    }

    [Fact]
    public void DedupeKey_FallsBackToOperationIdAndTransitionKind_WhenEventIdIsMissing()
    {
        var a = new OperateTimelineEntry("deploy", "info", "reconciling", "t", "c", OperationId: "op-1", TransitionKind: "Reconciling");
        var b = new OperateTimelineEntry("deploy", "info", "reconciling again", "t", "c", OperationId: "op-1", TransitionKind: "Reconciling");
        var different = new OperateTimelineEntry("deploy", "info", "succeeded", "t", "c", OperationId: "op-1", TransitionKind: "Succeeded");

        Assert.Equal(a.DedupeKey, b.DedupeKey);
        Assert.NotEqual(a.DedupeKey, different.DedupeKey);

        var deduped = OperateTimelineEntries.Deduplicate([a, b, different]);
        Assert.Equal(2, deduped.Count);
    }

    [Fact]
    public void ObserveabilityEventRow_ProjectsOntoATimelineEntry_PreservingItsEventId()
    {
        var snapshot = OperateObservabilityFixture.Default;
        var row = snapshot.Events[0];

        var entry = row.ToTimelineEntry();

        Assert.Equal(row.EventId, entry.EventId);
        Assert.Equal(row.EventType, entry.Kind);
        Assert.Equal(row.Severity, entry.Severity);
        Assert.Equal(row.Message, entry.Message);
        Assert.Equal(row.CorrelationId, entry.CorrelationId);
        Assert.Equal(row.DetailHref, entry.DetailHref);
        Assert.Equal($"event:{row.EventId}", entry.DedupeKey);
    }
}
