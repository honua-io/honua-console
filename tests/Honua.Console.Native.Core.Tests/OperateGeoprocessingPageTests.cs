using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Honua.Console.Native.Core.Tests;

public sealed class OperateGeoprocessingPageTests
{
    [Fact]
    public async Task ListModeRendersGeoprocessingJobs()
    {
        var html = await RenderAsync(
            new StubGeoprocessingClient
            {
                Jobs = OperateSectionResult<IReadOnlyList<OperateJobRun>>.Allowed(
                [
                    Job("gp-run-1", "Running", "Executing", 40, "analyst.live"),
                    Job("gp-run-2", "Succeeded", "Done", 100, "analyst.live"),
                ])
            });

        Assert.Contains("Geoprocessing Jobs", html);
        Assert.Contains("gp-run-1", html);
        Assert.Contains("gp-run-2", html);
        Assert.Contains("/operate/geoprocessing/gp-run-1", html);
        Assert.Contains("Executing", html);
        Assert.Contains("analyst.live", html);
    }

    [Fact]
    public async Task ListModeRendersEmptyStateWhenNoJobs()
    {
        var html = await RenderAsync(
            new StubGeoprocessingClient
            {
                Jobs = OperateSectionResult<IReadOnlyList<OperateJobRun>>.Allowed([])
            });

        Assert.Contains("No geoprocessing jobs", html);
    }

    [Fact]
    public async Task ListModeRendersForbiddenState()
    {
        var html = await RenderAsync(
            new StubGeoprocessingClient
            {
                Jobs = OperateSectionResult<IReadOnlyList<OperateJobRun>>.Denied(
                    OperateSectionStatus.Forbidden,
                    "The active environment profile is not permitted to read jobs.")
            });

        Assert.Contains("Permission required", html);
        Assert.Contains("not permitted", html);
    }

    [Fact]
    public async Task DetailModeRendersJobDetailPanel()
    {
        var html = await RenderAsync(
            new StubGeoprocessingClient
            {
                Detail = OperateSectionResult<OperateJobRun>.Allowed(
                    Job("gp-run-1", "Succeeded", "Done", 100, "analyst.live"))
            },
            selectedJobRunId: "gp-run-1");

        Assert.Contains("Job gp-run-1", html);
        Assert.Contains("Back to jobs", html);
        // JobDetailPanel surface markers.
        Assert.Contains("Job detail", html);
        Assert.Contains("Stages", html);
    }

    [Fact]
    public async Task DetailModeRendersMissingState()
    {
        var html = await RenderAsync(
            new StubGeoprocessingClient
            {
                Detail = OperateSectionResult<OperateJobRun>.Denied(
                    OperateSectionStatus.Missing,
                    "Job 'gp-missing' was not found.")
            },
            selectedJobRunId: "gp-missing");

        Assert.Contains("Not found", html);
        Assert.Contains("was not found", html);
    }

    [Fact]
    public void DetailModeOffersRetryOnlyForFailedOrCancelled()
    {
        // Succeeded is terminal but not retryable: no Retry, no Cancel.
        using var ctx = new BunitContext();
        var client = new StubGeoprocessingClient
        {
            Detail = OperateSectionResult<OperateJobRun>.Allowed(
                Job("gp-done", "Succeeded", "Done", 100, "analyst.live"))
        };
        ctx.Services.AddSingleton<IConsoleOperateObservabilityClient>(client);

        var page = ctx.Render<OperateGeoprocessingPage>(p => p
            .Add(x => x.SelectedJobRunId, "gp-done"));

        var buttons = page.FindAll("button");
        Assert.DoesNotContain(buttons, b => b.TextContent.Contains("Retry job", StringComparison.Ordinal));
        Assert.DoesNotContain(buttons, b => b.TextContent.Contains("Cancel job", StringComparison.Ordinal));
    }

    [Fact]
    public void DetailModeRetryOnFailedJobCallsRetryEndpointAndSurfacesResult()
    {
        using var ctx = new BunitContext();
        var client = new StubGeoprocessingClient
        {
            Detail = OperateSectionResult<OperateJobRun>.Allowed(
                Job("gp-failed", "Failed", "Errored", 60, "analyst.live")),
            RetryResult = OperateSectionResult<OperateJobControlOutcome>.Allowed(
                new OperateJobControlOutcome("gp-failed", "retry", "Queued", "Job requeued for retry.", "corr-1"))
        };
        ctx.Services.AddSingleton<IConsoleOperateObservabilityClient>(client);

        var page = ctx.Render<OperateGeoprocessingPage>(p => p
            .Add(x => x.SelectedJobRunId, "gp-failed"));

        var retry = page.FindAll("button").Single(b => b.TextContent.Contains("Retry job", StringComparison.Ordinal));
        retry.Click();

        Assert.Equal("gp-failed", Assert.Single(client.RetryCalls));
        Assert.Empty(client.CancelCalls);
        Assert.Contains("Job requeued for retry.", page.Markup);
    }

    [Fact]
    public void DetailModeCancelOnRunningJobCallsCancelEndpoint()
    {
        using var ctx = new BunitContext();
        var client = new StubGeoprocessingClient
        {
            Detail = OperateSectionResult<OperateJobRun>.Allowed(
                Job("gp-run", "Running", "Executing", 40, "analyst.live")),
            CancelResult = OperateSectionResult<OperateJobControlOutcome>.Allowed(
                new OperateJobControlOutcome("gp-run", "cancel", "Cancelled", "Cancellation requested.", "corr-2"))
        };
        ctx.Services.AddSingleton<IConsoleOperateObservabilityClient>(client);

        var page = ctx.Render<OperateGeoprocessingPage>(p => p
            .Add(x => x.SelectedJobRunId, "gp-run"));

        var cancel = page.FindAll("button").Single(b => b.TextContent.Contains("Cancel job", StringComparison.Ordinal));
        cancel.Click();

        Assert.Equal("gp-run", Assert.Single(client.CancelCalls));
        Assert.Contains("Cancellation requested.", page.Markup);
    }

    [Fact]
    public void DetailModeSurfacesApprovalRequiredFromForbiddenResult()
    {
        using var ctx = new BunitContext();
        var client = new StubGeoprocessingClient
        {
            Detail = OperateSectionResult<OperateJobRun>.Allowed(
                Job("gp-run", "Running", "Executing", 40, "analyst.live")),
            CancelResult = OperateSectionResult<OperateJobControlOutcome>.Denied(
                OperateSectionStatus.Forbidden,
                "This action requires approval. The server's approval gate is holding it pending an approver; it has not run.")
        };
        ctx.Services.AddSingleton<IConsoleOperateObservabilityClient>(client);

        var page = ctx.Render<OperateGeoprocessingPage>(p => p
            .Add(x => x.SelectedJobRunId, "gp-run"));

        page.FindAll("button").Single(b => b.TextContent.Contains("Cancel job", StringComparison.Ordinal)).Click();

        Assert.Contains("requires approval", page.Markup);
    }

    [Fact]
    public void ListModeShowsTruncationIndicatorWhenMoreJobsExist()
    {
        using var ctx = new BunitContext();
        var client = new StubGeoprocessingClient
        {
            Jobs = OperateSectionResult<IReadOnlyList<OperateJobRun>>.Allowed(
                [Job("gp-1", "Succeeded", "Done", 100, "analyst.live")],
                partialResult: true,
                message: "Showing the most recent 250 jobs; more exist on the server.")
        };
        ctx.Services.AddSingleton<IConsoleOperateObservabilityClient>(client);

        var page = ctx.Render<OperateGeoprocessingPage>();

        Assert.Contains("more exist on the server", page.Markup);
    }

    private static async Task<string> RenderAsync(
        IConsoleOperateObservabilityClient client,
        string? selectedJobRunId = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(client);
        var provider = services.BuildServiceProvider();

        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = selectedJobRunId is null
                ? ParameterView.Empty
                : ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(OperateGeoprocessingPage.SelectedJobRunId)] = selectedJobRunId
                });
            var output = await renderer.RenderComponentAsync<OperateGeoprocessingPage>(parameters);
            return output.ToHtmlString();
        });
    }

    private static OperateJobRun Job(string id, string status, string phase, int percent, string requestedBy) =>
        new(
            JobRunId: id,
            Source: "Geoprocessing",
            JobType: "postgis",
            Queue: "default",
            Status: new OperateStatus(status, phase),
            SubmittedBy: requestedBy,
            SubmittedAt: "2026-05-24 20:00 UTC",
            EnvironmentId: "live",
            ServerId: "server.example",
            ProgressPercent: percent,
            FailureClassification: "none",
            ResourceRefs: ["dataset:roads"],
            Stages: [],
            Logs: [],
            Artifacts: [],
            Metrics: [],
            AllowedActions: [new OperateJobAction("Cancel", false, "Server policy disabled in test")],
            RelatedObjects: []);

    private sealed class StubGeoprocessingClient : IConsoleOperateObservabilityClient
    {
        public OperateSectionResult<IReadOnlyList<OperateJobRun>> Jobs { get; set; } =
            OperateSectionResult<IReadOnlyList<OperateJobRun>>.Allowed([]);

        public OperateSectionResult<OperateJobRun>? Detail { get; set; }

        public OperateSectionResult<OperateJobControlOutcome> CancelResult { get; init; } =
            OperateSectionResult<OperateJobControlOutcome>.Denied(OperateSectionStatus.Unavailable, "n/a");

        public OperateSectionResult<OperateJobControlOutcome> RetryResult { get; init; } =
            OperateSectionResult<OperateJobControlOutcome>.Denied(OperateSectionStatus.Unavailable, "n/a");

        public List<string> CancelCalls { get; } = [];

        public List<string> RetryCalls { get; } = [];

        public int GeoprocessingJobReads { get; private set; }

        public Task<OperateSectionResult<IReadOnlyList<OperateJobRun>>> GetGeoprocessingJobsAsync(CancellationToken cancellationToken = default)
        {
            GeoprocessingJobReads++;
            return Task.FromResult(Jobs);
        }

        public Task<OperateSectionResult<IReadOnlyList<OperateJobRun>>> GetJobsAsync(string? kind, CancellationToken cancellationToken = default) =>
            Task.FromResult(Jobs);

        public Task<OperateSectionResult<IReadOnlyList<OperateJobRun>>> GetJobsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Jobs);

        public Task<OperateSectionResult<OperateJobRun>> GetJobDetailAsync(string jobRunId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Detail ?? OperateSectionResult<OperateJobRun>.Denied(OperateSectionStatus.Missing, "not found"));

        public Task<OperateSectionResult<OperateJobControlOutcome>> CancelJobAsync(string jobRunId, CancellationToken cancellationToken = default)
        {
            CancelCalls.Add(jobRunId);
            return Task.FromResult(CancelResult);
        }

        public Task<OperateSectionResult<OperateJobControlOutcome>> RetryJobAsync(string jobRunId, CancellationToken cancellationToken = default)
        {
            RetryCalls.Add(jobRunId);
            return Task.FromResult(RetryResult);
        }

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

        public Task<OperateSectionResult<IReadOnlyList<OperateInvestigation>>> GetInvestigationsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<OperateInvestigation>>.Allowed([]));

        public Task<OperateSectionResult<IReadOnlyList<OperateRecentError>>> GetRecentErrorsAsync(int limit = 10, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<OperateRecentError>>.Allowed([]));
    }
}
