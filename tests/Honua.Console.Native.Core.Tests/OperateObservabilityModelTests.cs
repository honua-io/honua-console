using Honua.Console.Shell.Models;

namespace Honua.Console.Native.Core.Tests;

public sealed class OperateObservabilityModelTests
{
    [Fact]
    public void NeutralTelemetryStatesDoNotBecomeFailures()
    {
        var snapshot = OperateObservabilityFixture.Default;
        var neutralTelemetry = snapshot.TelemetryFacts
            .Where(item => item.IsNeutralTelemetry)
            .ToArray();

        Assert.NotEmpty(neutralTelemetry);
        Assert.Contains(neutralTelemetry, item => item.State.Label == "unknown");
        Assert.Contains(neutralTelemetry, item => item.State.Label == "unsupported");
        Assert.Contains(neutralTelemetry, item => item.State.Label == "disabled");
        Assert.Contains(neutralTelemetry, item => item.State.Label == "not configured");
        Assert.All(neutralTelemetry, item =>
        {
            Assert.False(item.State.IsFailure);
            Assert.Equal("console-state-neutral", item.State.CssClass);
        });
    }

    [Fact]
    public void ErrorEventsAndFiringAlertsRenderAsFailures()
    {
        var snapshot = OperateObservabilityFixture.Default;
        var errorEvent = Assert.Single(snapshot.Events, item => item.EventId == "evt-job-301");
        var firingAlert = Assert.Single(snapshot.Alerts, item => item.AlertId == "alert-slo-burn");

        Assert.True(errorEvent.SeverityStatus.IsFailure);
        Assert.Equal("console-state-danger", errorEvent.SeverityStatus.CssClass);
        Assert.True(firingAlert.Status.IsFailure);
        Assert.Equal("console-state-danger", firingAlert.Status.CssClass);
    }

    [Fact]
    public void AiAdvisoriesRemainEvidenceLinkedAndPreserveRawEvidence()
    {
        var snapshot = OperateObservabilityFixture.Default;
        var eventsWithAdvisory = snapshot.Events
            .Where(item => item.AiAdvisory is not null)
            .ToArray();

        Assert.NotEmpty(eventsWithAdvisory);
        Assert.All(eventsWithAdvisory, item =>
        {
            Assert.True(item.PreservesRawEvidenceWithAi);
            Assert.NotEmpty(item.RawEvidence);
            Assert.NotNull(item.AiAdvisory);
            Assert.True(item.AiAdvisory!.IsEvidenceLinked);
        });

        Assert.All(snapshot.Alerts, alert =>
        {
            Assert.NotEmpty(alert.EvidenceLinks);
            Assert.NotNull(alert.AiAdvisory);
            Assert.True(alert.AiAdvisory!.IsEvidenceLinked);
        });
    }

    [Fact]
    public void InvalidAlertRulesCannotBeEnabled()
    {
        var snapshot = OperateObservabilityFixture.Default;
        var invalidRule = Assert.Single(snapshot.Rules, rule => rule.Status.Label == "invalid");
        var enabledRules = snapshot.Rules.Where(rule => rule.Enabled).ToArray();

        Assert.False(invalidRule.IsValid);
        Assert.False(invalidRule.CanEnable);
        Assert.NotEmpty(invalidRule.ValidationMessages);
        Assert.All(enabledRules, rule => Assert.False(rule.CanEnable));

        Assert.Contains(snapshot.Rules, rule => rule.Status.Label == "disabled" && rule.IsValid && rule.CanEnable);
    }

    [Fact]
    public void RequiredJobSourcesDeepLinkToOneOperateJobRoute()
    {
        var snapshot = OperateObservabilityFixture.Default;

        Assert.True(snapshot.HasUnifiedJobDeepLinks);
        Assert.All(OperateObservabilitySnapshot.RequiredJobSources, source =>
            Assert.Contains(snapshot.Jobs, job => string.Equals(job.Source, source, StringComparison.OrdinalIgnoreCase)));
        Assert.All(snapshot.Jobs, job =>
            Assert.Equal($"/operate/jobs/{job.JobRunId}", job.DetailHref));
    }

    [Fact]
    public void EventViewerIncludesExpectedEvidenceCategories()
    {
        var snapshot = OperateObservabilityFixture.Default;
        var eventTypes = snapshot.Events
            .Select(item => item.EventType)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var expectedType in new[] { "log", "audit", "job", "alert", "release", "sync", "data change", "telemetry" })
        {
            Assert.Contains(expectedType, eventTypes);
        }
    }

    [Fact]
    public void EventFilterAppliesEnvironmentTypeAndCorrelation()
    {
        var snapshot = OperateObservabilityFixture.Default;
        var filtered = snapshot.Events
            .Where(new OperateEventFilter("prod", "job", "rel-20260524").Matches)
            .ToArray();

        var eventRow = Assert.Single(filtered);
        Assert.Equal("evt-job-301", eventRow.EventId);
        Assert.All(filtered, item =>
        {
            Assert.Equal("prod", item.EnvironmentId);
            Assert.Equal("job", item.EventType);
            Assert.Contains("rel-20260524", item.CorrelationId, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void EmptyEventFilterPreservesTheFullTimeline()
    {
        var snapshot = OperateObservabilityFixture.Default;

        Assert.Equal(snapshot.Events.Count, snapshot.Events.Count(OperateEventFilter.Empty.Matches));
    }

    [Fact]
    public void JobActionsFollowRetryCancelAndPromoteStateRules()
    {
        var snapshot = OperateObservabilityFixture.Default;

        AssertAction(snapshot, "job-publish-001", "Retry", isAllowed: true);
        AssertAction(snapshot, "job-publish-001", "Cancel", isAllowed: false);
        AssertAction(snapshot, "job-publish-001", "Promote", isAllowed: false);

        AssertAction(snapshot, "job-gitops-001", "Retry", isAllowed: false);
        AssertAction(snapshot, "job-gitops-001", "Cancel", isAllowed: true);
        AssertAction(snapshot, "job-gitops-001", "Promote", isAllowed: true);

        AssertAction(snapshot, "job-alert-001", "Retry", isAllowed: false);
        AssertAction(snapshot, "job-alert-001", "Cancel", isAllowed: true);
        AssertAction(snapshot, "job-alert-001", "Promote", isAllowed: false);

        Assert.All(snapshot.Jobs.Where(job => job.Status.Label is "succeeded" or "blocked"), job =>
        {
            Assert.All(job.AllowedActions, action => Assert.False(action.IsAllowed));
        });
    }

    private static void AssertAction(
        OperateObservabilitySnapshot snapshot,
        string jobRunId,
        string label,
        bool isAllowed)
    {
        var job = Assert.Single(snapshot.Jobs, item => item.JobRunId == jobRunId);
        var action = Assert.Single(job.AllowedActions, item => item.Label == label);

        Assert.Equal(isAllowed, action.IsAllowed);
        Assert.NotEmpty(action.Reason);
    }
}
