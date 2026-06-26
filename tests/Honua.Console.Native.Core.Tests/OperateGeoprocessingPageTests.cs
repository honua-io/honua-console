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
    public async Task DetailModeRendersStepsGlassBoxPanelLazily()
    {
        const string sanitizedCommand = "gdalwarp -t_srs EPSG:3857 <path>/in.tif <scratch>/out.tif";
        var stub = new StubGeoprocessingClient
        {
            Detail = OperateSectionResult<OperateJobRun>.Allowed(
                Job("gp-run-1", "Succeeded", "Done", 100, "analyst.live")),
            Steps = OperateSectionResult<OperateJobStepsView>.Allowed(
                new OperateJobStepsView(
                    "gp-run-1",
                    "corr-1",
                    new OperateStatus("Succeeded", "Done"),
                    [
                        new OperateJobStep(
                            Ordinal: 1,
                            Phase: "Reproject",
                            Status: new OperateStatus("Succeeded", "Reprojected"),
                            Timing: "2026-05-24 20:00 UTC - 2026-05-24 20:00 UTC",
                            Duration: "3 s",
                            Message: "Reprojected to Web Mercator",
                            Command: sanitizedCommand,
                            Artifacts: [new OperateJobStepArtifact("out.tif", "raster", "2 MB")],
                            Metadata: [])
                    ]))
        };

        var html = await RenderAsync(stub, selectedJobRunId: "gp-run-1");

        // The steps panel is fetched lazily when the detail opens.
        Assert.Equal(1, stub.StepFetchCount);
        Assert.Contains("Glass-box", html);
        Assert.Contains("Reproject", html);
        // Command rendered verbatim (server already sanitized it).
        Assert.Contains("gdalwarp -t_srs EPSG:3857 &lt;path&gt;/in.tif &lt;scratch&gt;/out.tif", html);
        Assert.Contains("out.tif", html);
        Assert.Contains("3 s", html);
    }

    [Fact]
    public async Task DetailModeRendersStepsForbiddenState()
    {
        var stub = new StubGeoprocessingClient
        {
            Detail = OperateSectionResult<OperateJobRun>.Allowed(
                Job("gp-run-1", "Succeeded", "Done", 100, "analyst.live")),
            Steps = OperateSectionResult<OperateJobStepsView>.Denied(
                OperateSectionStatus.Forbidden,
                "The active environment profile is not permitted to read job steps.")
        };

        var html = await RenderAsync(stub, selectedJobRunId: "gp-run-1");

        Assert.Contains("Permission required", html);
        Assert.Contains("not permitted to read job steps", html);
    }

    [Fact]
    public async Task ListModeDoesNotFetchSteps()
    {
        var stub = new StubGeoprocessingClient
        {
            Jobs = OperateSectionResult<IReadOnlyList<OperateJobRun>>.Allowed(
                [Job("gp-run-1", "Running", "Executing", 40, "analyst.live")])
        };

        _ = await RenderAsync(stub);

        Assert.Equal(0, stub.StepFetchCount);
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
        public OperateSectionResult<IReadOnlyList<OperateJobRun>> Jobs { get; init; } =
            OperateSectionResult<IReadOnlyList<OperateJobRun>>.Allowed([]);

        public OperateSectionResult<OperateJobRun>? Detail { get; init; }

        public OperateSectionResult<OperateJobStepsView>? Steps { get; init; }

        public int StepFetchCount { get; private set; }

        public Task<OperateSectionResult<IReadOnlyList<OperateJobRun>>> GetGeoprocessingJobsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Jobs);

        public Task<OperateSectionResult<IReadOnlyList<OperateJobRun>>> GetJobsAsync(string? kind, CancellationToken cancellationToken = default) =>
            Task.FromResult(Jobs);

        public Task<OperateSectionResult<IReadOnlyList<OperateJobRun>>> GetJobsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Jobs);

        public Task<OperateSectionResult<OperateJobRun>> GetJobDetailAsync(string jobRunId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Detail ?? OperateSectionResult<OperateJobRun>.Denied(OperateSectionStatus.Missing, "not found"));

        public Task<OperateSectionResult<OperateJobStepsView>> GetJobStepsAsync(string jobRunId, CancellationToken cancellationToken = default)
        {
            StepFetchCount++;
            return Task.FromResult(Steps ?? OperateSectionResult<OperateJobStepsView>.Allowed(OperateJobStepsView.Empty));
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
