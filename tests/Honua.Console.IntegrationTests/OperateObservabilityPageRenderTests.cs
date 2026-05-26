using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render regression for the Operate observability deep-link surfaces.
/// Regression target: an empty event/alert/job page must not swallow a
/// <c>/operate/events|alerts|jobs/{id}</c> deep link. The previous markup nested every
/// detail/missing surface inside the "list has rows" branch, so a deep link landing on an
/// empty section silently showed only the generic empty-list panel. Each section must now
/// surface the missing-detail state (events/alerts) or the independently-read job detail (jobs)
/// even when the list page is empty.
/// </summary>
public sealed class OperateObservabilityPageRenderTests
{
    [Fact]
    public void EventDeepLink_WhenEventPageEmpty_StillRendersMissingDetailSurface()
    {
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IConsoleOperateObservabilityClient>(new StubOperateClient());

        var page = ctx.RenderComponent<OperateObservabilityPage>(parameters =>
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
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IConsoleOperateObservabilityClient>(new StubOperateClient());

        var page = ctx.RenderComponent<OperateObservabilityPage>(parameters =>
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

        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IConsoleOperateObservabilityClient>(stub);

        var page = ctx.RenderComponent<OperateObservabilityPage>(parameters =>
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

    /// <summary>
    /// Test double whose sections all read as allowed-but-empty by default. Each test overrides
    /// only the read it exercises; <see cref="JobDetail"/> resolves the deep-linked job id.
    /// </summary>
    private sealed class StubOperateClient : IConsoleOperateObservabilityClient
    {
        public Func<string, OperateSectionResult<OperateJobRun>> JobDetail { get; init; } =
            _ => OperateSectionResult<OperateJobRun>.Denied(OperateSectionStatus.Missing, "Job not found.");

        public Task<OperateSectionResult<OperateFleetOverview>> GetOverviewAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<OperateFleetOverview>.Allowed(OperateFleetOverview.Empty));

        public Task<OperateSectionResult<IReadOnlyList<OperateEventRow>>> QueryEventsAsync(OperateEventQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<OperateEventRow>>.Allowed([]));

        public Task<OperateSectionResult<OperateLogsView>> GetLogsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<OperateLogsView>.Allowed(OperateLogsView.Empty));

        public Task<OperateSectionResult<IReadOnlyList<OperateAlertRecord>>> GetAlertsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<OperateAlertRecord>>.Allowed([]));

        public Task<OperateSectionResult<OperateRulesView>> GetRulesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<OperateRulesView>.Allowed(OperateRulesView.Empty));

        public Task<OperateSectionResult<IReadOnlyList<OperateJobRun>>> GetJobsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<OperateJobRun>>.Allowed([]));

        public Task<OperateSectionResult<OperateJobRun>> GetJobDetailAsync(string jobRunId, CancellationToken cancellationToken = default) =>
            Task.FromResult(JobDetail(jobRunId));

        public Task<OperateSectionResult<IReadOnlyList<OperateInvestigation>>> GetInvestigationsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<OperateInvestigation>>.Allowed([]));
    }
}
