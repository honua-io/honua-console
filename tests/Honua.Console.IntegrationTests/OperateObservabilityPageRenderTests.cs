using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

// Nesting the detail inside the populated-list branch swallows a deep link, including a job loaded by its own detail read.
public sealed class OperateObservabilityPageRenderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EventTimeline_PreservesDeduplicationAndSelectedEventEvidence(bool v4Enabled)
    {
        var first = BuildEvent("evt-first", "First event", "corr-first");
        var selected = BuildEvent("evt-selected", "Selected event", "corr-selected");
        using var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = v4Enabled });
        context.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        context.Services.AddSingleton<IConsoleOperateObservabilityClient>(new StubOperateClient
        {
            Events = OperateSectionResult<IReadOnlyList<OperateEventRow>>.Allowed([first, selected, selected]),
        });

        var page = context.Render<OperateObservabilityPage>(parameters =>
            parameters.Add(p => p.SelectedEventId, selected.EventId));

        page.WaitForAssertion(() =>
        {
            Assert.Equal(2, page.FindAll("[data-timeline-row]").Count);
            Assert.Equal(selected.DetailHref, page.Find(".operate-selected-row a").GetAttribute("href"));
            Assert.Equal(selected.Message, page.Find("#event-detail-heading").TextContent);
            Assert.Contains(selected.CorrelationId, page.Find("[aria-labelledby='event-detail-heading']").TextContent);
        });
        if (v4Enabled) CatalogAdminPreview.Capture("activity-selected", page.Markup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EventFilters_SendTheSelectedTypeAndCorrelationToTheServer(bool v4Enabled)
    {
        var client = new StubOperateClient
        {
            Events = OperateSectionResult<IReadOnlyList<OperateEventRow>>.Allowed(
                [BuildEvent("evt-first", "First event", "corr-first")]),
        };
        using var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = v4Enabled });
        context.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        context.Services.AddSingleton<IConsoleOperateObservabilityClient>(client);
        var page = context.Render<OperateObservabilityPage>();
        var filters = page.Find("form[aria-label='Operate evidence filters']");

        filters.QuerySelectorAll("select")[v4Enabled ? 1 : 2].Change("release");
        filters.QuerySelector("input")!.Input("corr-selected");
        filters.Submit();

        page.WaitForAssertion(() =>
        {
            Assert.Equal("release", client.LastEventQuery?.EventType);
            Assert.Equal("corr-selected", client.LastEventQuery?.CorrelationId);
        });
    }

    [Theory]
    [InlineData(false, "Operate Observability")]
    [InlineData(true, "Activity")]
    public void RolloutFlag_SelectsPresentationWithoutDroppingOperationalSections(bool enabled, string heading)
    {
        using var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        context.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        context.Services.AddSingleton<IConsoleOperateObservabilityClient>(new StubOperateClient());

        var page = context.Render<OperateObservabilityPage>();

        Assert.Equal(heading, page.Find("h1").TextContent);
        foreach (var id in new[] { "fleet", "events", "logs", "alerts", "rules", "jobs", "investigations" })
        {
            Assert.Single(page.FindAll($"section#{id}"));
        }

        var sections = page.FindAll("section[id]").Select(section => section.Id).ToArray();
        Assert.True(enabled
            ? Array.IndexOf(sections, "events") < Array.IndexOf(sections, "fleet")
            : Array.IndexOf(sections, "fleet") < Array.IndexOf(sections, "events"));
        Assert.Equal(enabled, page.FindAll(".activity-sync-notice").Count == 1);
        Assert.Equal(enabled, page.FindAll(".console-activity-v4").Count == 1);
        if (enabled) CatalogAdminPreview.Capture("activity-empty", page.Markup);
    }

    private static OperateEventRow BuildEvent(string id, string message, string correlation) =>
        new(id, "2026-11-09T17:30:00Z", "info", "release", "publication", message,
            "production", "server-1", correlation, "trace-1", "request-1", null, null, [], [], [], null);

    [Fact]
    public void UnadvertisedInvestigationSectionPreservesThePageHeading()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<IConsoleCapabilityManifest>(new ConsoleCapabilityManifest());
        context.Services.AddSingleton<IConsoleOperateObservabilityClient>(new StubOperateClient());

        var page = context.Render<OperateObservabilityPage>();

        Assert.Single(page.FindAll("h1"));
        Assert.Contains(page.FindAll("h2"), heading => heading.TextContent == "Investigations are not available in this release");
        Assert.Empty(page.FindAll("#investigations"));
    }

    [Fact]
    public void EventDeepLink_WhenEventPageEmpty_StillRendersMissingDetailSurface()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        ctx.Services.AddSingleton<IConsoleOperateObservabilityClient>(new StubOperateClient());

        var page = ctx.Render<OperateObservabilityPage>(parameters =>
            parameters.Add(p => p.SelectedEventId, "evt-unknown"));

        page.WaitForAssertion(
            () =>
            {
                // The empty-list context is still shown...
                Assert.Contains("No matching events", page.Markup, StringComparison.Ordinal);
                // ...but the deep link is no longer swallowed: the missing-detail surface renders.
                Assert.Contains(
                    "Event 'evt-unknown' was not found in the loaded server event page.",
                    page.Markup,
                    StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void AlertDeepLink_WhenAlertPageEmpty_StillRendersMissingDetailSurface()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        ctx.Services.AddSingleton<IConsoleOperateObservabilityClient>(new StubOperateClient());

        var page = ctx.Render<OperateObservabilityPage>(parameters =>
            parameters.Add(p => p.SelectedAlertId, "alert-unknown"));

        page.WaitForAssertion(
            () =>
            {
                Assert.Contains("No active alerts", page.Markup, StringComparison.Ordinal);
                Assert.Contains(
                    "Alert 'alert-unknown' was not found in the loaded server alert page.",
                    page.Markup,
                    StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void JobDeepLink_WhenJobListEmpty_StillRendersDetailFromIndependentRead()
    {
        // The job list is empty, but the deep-linked job resolves through its own server read.
        var stub = new StubOperateClient
        {
            JobDetail = jobRunId => OperateSectionResult<OperateJobRun>.Allowed(BuildJob(jobRunId)),
        };

        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        ctx.Services.AddSingleton<IConsoleOperateObservabilityClient>(stub);
        // The unified job-run deep link also embeds the workflow job-evidence panel, which binds
        // IStudioWorkflowPackageClient. The unsupported client returns null evidence (missing-binding).
        ctx.Services.AddSingleton<IStudioWorkflowPackageClient>(new UnsupportedStudioWorkflowPackageClient());

        var page = ctx.Render<OperateObservabilityPage>(parameters =>
            parameters.Add(p => p.SelectedJobRunId, "job-deep-001"));

        page.WaitForAssertion(
            () =>
            {
                // The empty job list is still shown as context...
                Assert.Contains("No jobs", page.Markup, StringComparison.Ordinal);
                // ...and the independently-read job detail renders despite the empty list.
                Assert.Contains("job-deep-001", page.Markup, StringComparison.Ordinal);
                Assert.Contains("Deep-linked job is running.", page.Markup, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void JobDeepLink_WhenJobListReadFails_StillRendersDetailFromIndependentRead()
    {
        // The job list read is forbidden, but the deep-linked job detail is a separate read
        // that the active profile is allowed to make.
        var stub = new StubOperateClient
        {
            Jobs = OperateSectionResult<IReadOnlyList<OperateJobRun>>.Denied(
                OperateSectionStatus.Forbidden,
                "Jobs list is forbidden for this profile."),
            JobDetail = jobRunId => OperateSectionResult<OperateJobRun>.Allowed(BuildJob(jobRunId)),
        };

        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        ctx.Services.AddSingleton<IConsoleOperateObservabilityClient>(stub);
        ctx.Services.AddSingleton<IStudioWorkflowPackageClient>(new UnsupportedStudioWorkflowPackageClient());

        var page = ctx.Render<OperateObservabilityPage>(parameters =>
            parameters.Add(p => p.SelectedJobRunId, "job-deep-002"));

        page.WaitForAssertion(
            () =>
            {
                // The failed list read is still surfaced as context...
                Assert.Contains("Jobs list is forbidden for this profile.", page.Markup, StringComparison.Ordinal);
                // ...and the independently-read job detail is no longer hidden by the list failure.
                Assert.Contains("job-deep-002", page.Markup, StringComparison.Ordinal);
                Assert.Contains("Deep-linked job is running.", page.Markup, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
    }

    private static OperateJobRun BuildJob(string jobRunId) => new(
        JobRunId: jobRunId,
        Source: "Publishing",
        JobType: "publish",
        Queue: "operator-publishing",
        Status: new OperateStatus("running", "Deep-linked job is running."),
        SubmittedBy: "tester@honua.example",
        SubmittedAt: "2026-05-26 10:00 HST",
        EnvironmentId: "prod",
        ServerId: "honua-prod-01",
        ProgressPercent: 50,
        FailureClassification: "none",
        ResourceRefs: [],
        Stages: [],
        Logs: [],
        Artifacts: [],
        Metrics: [],
        AllowedActions: [],
        RelatedObjects: []);

    private sealed class StubOperateClient : IConsoleOperateObservabilityClient
    {
        public OperateSectionResult<IReadOnlyList<OperateEventRow>> Events { get; init; } =
            OperateSectionResult<IReadOnlyList<OperateEventRow>>.Allowed([]);

        public OperateEventQuery? LastEventQuery { get; private set; }

        public OperateSectionResult<IReadOnlyList<OperateJobRun>> Jobs { get; init; } =
            OperateSectionResult<IReadOnlyList<OperateJobRun>>.Allowed([]);

        public Func<string, OperateSectionResult<OperateJobRun>> JobDetail { get; init; } =
            _ => OperateSectionResult<OperateJobRun>.Denied(OperateSectionStatus.Missing, "Job not found.");

        public Task<OperateSectionResult<OperateFleetOverview>> GetOverviewAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<OperateFleetOverview>.Allowed(OperateFleetOverview.Empty));

        public Task<OperateSectionResult<IReadOnlyList<OperateEventRow>>> QueryEventsAsync(OperateEventQuery query, CancellationToken cancellationToken = default)
        {
            LastEventQuery = query;
            return Task.FromResult(Events);
        }

        public Task<OperateSectionResult<OperateLogsView>> GetLogsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<OperateLogsView>.Allowed(OperateLogsView.Empty));

        public Task<OperateSectionResult<IReadOnlyList<OperateAlertRecord>>> GetAlertsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<OperateAlertRecord>>.Allowed([]));

        public Task<OperateSectionResult<OperateRulesView>> GetRulesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<OperateRulesView>.Allowed(OperateRulesView.Empty));

        public Task<OperateSectionResult<IReadOnlyList<OperateJobRun>>> GetJobsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Jobs);

        public Task<OperateSectionResult<IReadOnlyList<OperateJobRun>>> GetJobsAsync(string? kind, CancellationToken cancellationToken = default) =>
            GetJobsAsync(cancellationToken);

        public Task<OperateSectionResult<IReadOnlyList<OperateJobRun>>> GetGeoprocessingJobsAsync(CancellationToken cancellationToken = default) =>
            GetJobsAsync(cancellationToken);

        public Task<OperateSectionResult<OperateJobRun>> GetJobDetailAsync(string jobRunId, CancellationToken cancellationToken = default) =>
            Task.FromResult(JobDetail(jobRunId));

        public Task<OperateSectionResult<OperateJobControlOutcome>> CancelJobAsync(string jobRunId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<OperateJobControlOutcome>.Denied(OperateSectionStatus.Unavailable, "n/a"));

        public Task<OperateSectionResult<OperateJobControlOutcome>> RetryJobAsync(string jobRunId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<OperateJobControlOutcome>.Denied(OperateSectionStatus.Unavailable, "n/a"));

        public Task<OperateSectionResult<OperateJobStepsView>> GetJobStepsAsync(string jobRunId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<OperateJobStepsView>.Allowed(OperateJobStepsView.Empty));

        public Task<OperateSectionResult<IReadOnlyList<OperateInvestigation>>> GetInvestigationsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<OperateInvestigation>>.Allowed([]));

        public Task<OperateSectionResult<IReadOnlyList<OperateRecentError>>> GetRecentErrorsAsync(int limit = 10, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<OperateRecentError>>.Allowed([]));
    }
}
