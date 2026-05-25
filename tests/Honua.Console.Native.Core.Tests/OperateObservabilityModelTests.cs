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
            Assert.True(alert.AiAdvisory.IsEvidenceLinked);
        });
    }

    [Fact]
    public void InvalidAlertRulesCannotBeEnabled()
    {
        var snapshot = OperateObservabilityFixture.Default;
        var invalidRule = Assert.Single(snapshot.Rules, rule => rule.Status.Label == "invalid");

        Assert.False(invalidRule.IsValid);
        Assert.False(invalidRule.CanEnable);
        Assert.NotEmpty(invalidRule.ValidationMessages);

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
}
