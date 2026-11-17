using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Tests;

public sealed class JobDiagnosticsTimelineTests
{
    private const string ReadEarlier = "Read earlier events";

    [Fact]
    public void EmptyTimelineDoesNotOfferEarlierEventsForTheServerStartCursor()
    {
        // The server reports firstCursor "start" even when the job recorded no events.
        var markup = Render(Diagnostics(eventCount: 0, totalCount: 0));

        Assert.DoesNotContain(ReadEarlier, markup, StringComparison.Ordinal);
        Assert.Contains("0 recorded.", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void CompleteTimelineDoesNotOfferEarlierEvents()
    {
        var markup = Render(Diagnostics(eventCount: 2, totalCount: 2));

        Assert.DoesNotContain(ReadEarlier, markup, StringComparison.Ordinal);
    }

    [Fact]
    public void TruncatedTimelineOffersEarlierEventsFromTheFirstCursor()
    {
        var markup = Render(Diagnostics(eventCount: 2, totalCount: 9));

        Assert.Contains(ReadEarlier, markup, StringComparison.Ordinal);
    }

    private static string Render(OperateJobDiagnosticsView view)
    {
        using var ctx = new BunitContext();
        return ctx.Render<JobDiagnosticsTimeline>(p => p.Add(x => x.View, view)).Markup;
    }

    // The server marks a page truncated when more events are recorded than returned.
    private static OperateJobDiagnosticsView Diagnostics(int eventCount, int totalCount) =>
        new(
            Schema: "honua.job-diagnostics/v1",
            JobRunId: "gp-run",
            Phase: "succeeded",
            TimingSource: "inferred",
            AttemptNumber: 1,
            MaxAttempts: 1,
            RetryAllowed: false,
            RetryRequiresApproval: false,
            RetryDisabledReason: string.Empty,
            WouldRepeat: string.Empty,
            CancelOffered: false,
            FailureClassification: null,
            FailureRecovery: null,
            FailurePhase: null,
            Steps: [],
            Events: Enumerable.Range(1, eventCount)
                .Select(i => new OperateJobDiagnosticEvent($"2026-11-17 06:3{i} UTC", "info", $"event {i}", "executing"))
                .ToArray(),
            EventsTruncated: totalCount > eventCount,
            EventsFirstCursor: "start",
            EventsNextCursor: null,
            EventsTotalCount: totalCount,
            EvidenceMissing: [],
            EvidenceTruncated: []);
}
