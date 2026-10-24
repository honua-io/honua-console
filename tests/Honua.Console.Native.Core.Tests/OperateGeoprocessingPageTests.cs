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
    public async Task DetailModeRendersEsriAliasCanonicalProcessAndComparableAuditIdentifiers()
    {
        var detail = Job("gp-buffer-1", "Succeeded", "Done", 100, "agent.release") with
        {
            Identity = new OperateJobIdentity(
                CorrelationId: "corr-buffer-1",
                TraceId: "trace-buffer-1",
                ProviderOperationId: "provider-buffer-1",
                DefinitionId: "gpserver-buffer",
                ProcessId: "geometry.buffer",
                GpServerServiceId: "geoprocessing",
                GpServerTaskName: "Buffer",
                ResultPackageId: "result-gp-buffer-1",
                SelectedMetadata:
                [
                    new OperateJobStepMetadata("gpserver.taskName", "Buffer"),
                    new OperateJobStepMetadata("honua.geoprocessing.process_definitions", "geometry.buffer"),
                    new OperateJobStepMetadata("resultPackageId", "result-gp-buffer-1")
                ])
        };

        var html = await RenderAsync(
            new StubGeoprocessingClient
            {
                Detail = OperateSectionResult<OperateJobRun>.Allowed(detail)
            },
            selectedJobRunId: "gp-buffer-1");

        Assert.Contains("Esri GPServer identity", html);
        Assert.Contains("Buffer", html);
        Assert.Contains("geometry.buffer", html);
        Assert.Contains("result-gp-buffer-1", html);
        Assert.Contains("corr-buffer-1", html);
        Assert.Contains("trace-buffer-1", html);
        Assert.Contains("provider-buffer-1", html);
        Assert.Contains("/rest/services/geoprocessing/GPServer/Buffer/jobs/gp-buffer-1", html);
        Assert.DoesNotContain("Run a process", html);
    }

    [Fact]
    public async Task DetailFailureRendersTechnicalProblemDiagnostics()
    {
        var html = await RenderAsync(
            new StubGeoprocessingClient
            {
                Detail = OperateSectionResult<OperateJobRun>.Denied(
                    OperateSectionStatus.Unavailable,
                    "The job detail could not be loaded.",
                    "HTTP 500; correlationId=corr-failed-gp; operationId=gp-failed")
            },
            selectedJobRunId: "gp-failed");

        Assert.Contains("The job detail could not be loaded.", html);
        Assert.Contains("Technical detail", html);
        Assert.Contains("corr-failed-gp", html);
        Assert.Contains("gp-failed", html);
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
            Diagnostics = OperateSectionResult<OperateJobDiagnosticsView>.Allowed(
                Projection("gp-failed", retry: true, phase: "failed")),
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
            Diagnostics = OperateSectionResult<OperateJobDiagnosticsView>.Allowed(
                Projection("gp-run", cancel: true)),
            CancelResult = OperateSectionResult<OperateJobControlOutcome>.Allowed(
                new OperateJobControlOutcome("gp-run", "cancel", "Cancelled", "Cancellation requested.", "corr-2"))
        };
        ctx.Services.AddSingleton<IConsoleOperateObservabilityClient>(client);

        var page = ctx.Render<OperateGeoprocessingPage>(p => p
            .Add(x => x.SelectedJobRunId, "gp-run"));

        // Cancel job now requires a confirmation step (ConsoleConfirmDialog was added to the page).
        // First click opens the confirm dialog; the accept button fires the actual cancel call.
        var cancel = page.FindAll("button").Single(b => b.TextContent.Contains("Cancel job", StringComparison.Ordinal));
        cancel.Click();

        page.Find("[data-console-confirm-accept]").Click();

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
            Diagnostics = OperateSectionResult<OperateJobDiagnosticsView>.Allowed(
                Projection("gp-run", cancel: true)),
            CancelResult = OperateSectionResult<OperateJobControlOutcome>.Denied(
                OperateSectionStatus.Forbidden,
                "This action requires approval. The server's approval gate is holding it pending an approver; it has not run.")
        };
        ctx.Services.AddSingleton<IConsoleOperateObservabilityClient>(client);

        var page = ctx.Render<OperateGeoprocessingPage>(p => p
            .Add(x => x.SelectedJobRunId, "gp-run"));

        // Cancel job now requires a confirmation step before the call goes through.
        page.FindAll("button").Single(b => b.TextContent.Contains("Cancel job", StringComparison.Ordinal)).Click();
        page.Find("[data-console-confirm-accept]").Click();

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

    [Fact]
    public void DetailTimelineOffersRetryOnlyWhenTheProjectionAllowsIt()
    {
        using var ctx = new BunitContext();
        var client = new StubGeoprocessingClient
        {
            Detail = OperateSectionResult<OperateJobRun>.Allowed(
                Job("gp-failed", "Failed", "Errored", 60, "analyst.live")),
            Diagnostics = OperateSectionResult<OperateJobDiagnosticsView>.Allowed(
                Projection("gp-failed", retry: false, phase: "failed", disabled: "Attempt budget is spent."))
        };
        ctx.Services.AddSingleton<IConsoleOperateObservabilityClient>(client);

        var page = ctx.Render<OperateGeoprocessingPage>(p => p.Add(x => x.SelectedJobRunId, "gp-failed"));

        Assert.Contains("data-job-timeline", page.Markup);
        Assert.Contains("Attempt budget is spent.", page.Markup);
        Assert.DoesNotContain(page.FindAll("button"), b => b.TextContent.Contains("Retry job", StringComparison.Ordinal));
        Assert.DoesNotContain(page.FindAll("button"), b => b.TextContent.Contains("Cancel job", StringComparison.Ordinal));
    }

    [Fact]
    public void DetailTimelineHidesCancelWhenTheProjectionHasNoCancelLink()
    {
        using var ctx = new BunitContext();
        var client = new StubGeoprocessingClient
        {
            Detail = OperateSectionResult<OperateJobRun>.Allowed(
                Job("gp-run", "Running", "Executing", 40, "analyst.live")),
            Diagnostics = OperateSectionResult<OperateJobDiagnosticsView>.Allowed(Projection("gp-run", cancel: false))
        };
        ctx.Services.AddSingleton<IConsoleOperateObservabilityClient>(client);

        var page = ctx.Render<OperateGeoprocessingPage>(p => p.Add(x => x.SelectedJobRunId, "gp-run"));

        Assert.Contains("data-job-diagnostics-phase", page.Markup);
        Assert.DoesNotContain(page.FindAll("button"), b => b.TextContent.Contains("Cancel job", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DetailTimelineKeepsMeasuredStepsRedactionAndEvidenceMarkers()
    {
        var stub = new StubGeoprocessingClient
        {
            Detail = OperateSectionResult<OperateJobRun>.Allowed(
                Job("gp-run-1", "Failed", "failed", 80, "analyst.live")),
            Diagnostics = OperateSectionResult<OperateJobDiagnosticsView>.Allowed(
                Projection(
                    "gp-run-1",
                    phase: "failed",
                    timing: "measured",
                    message: "worker said [REDACTED] under <scratch>",
                    truncated: true,
                    first: "start",
                    next: "cursor-2",
                    missing: ["release.lockDigest"]))
        };

        var html = await RenderAsync(stub, selectedJobRunId: "gp-run-1");

        Assert.Contains("data-job-timeline", html);
        Assert.Contains("data-job-diagnostics-phase", html);
        Assert.Contains(">failed<", html);
        Assert.Contains("data-job-diagnostics-timing", html);
        Assert.Contains(">measured<", html);
        Assert.Contains("[REDACTED]", html);
        Assert.Contains("&lt;scratch&gt;", html);
        Assert.Contains("data-job-diagnostics-truncated=\"true\"", html);
        Assert.Contains("data-job-diagnostics-first-cursor=\"start\"", html);
        Assert.Contains("data-job-diagnostics-next-cursor=\"cursor-2\"", html);
        Assert.Contains("release.lockDigest", html);
        Assert.Contains("events", html);
        Assert.DoesNotContain("super-secret-token", html);
    }

    [Fact]
    public void DetailTimelineAppendsTheNextEventPageWithoutDroppingTheCursor()
    {
        using var ctx = new BunitContext();
        var client = new StubGeoprocessingClient
        {
            Detail = OperateSectionResult<OperateJobRun>.Allowed(
                Job("gp-run", "Running", "executing", 40, "analyst.live")),
            DiagnosticsByCursor = cursor => cursor switch
            {
                null => OperateSectionResult<OperateJobDiagnosticsView>.Allowed(
                    Projection("gp-run", message: "tail event", next: "cursor-2")),
                "cursor-2" => OperateSectionResult<OperateJobDiagnosticsView>.Allowed(
                    Projection("gp-run", message: "older event", next: null)),
                _ => OperateSectionResult<OperateJobDiagnosticsView>.Denied(OperateSectionStatus.Rejected, "bad cursor")
            }
        };
        ctx.Services.AddSingleton<IConsoleOperateObservabilityClient>(client);

        var page = ctx.Render<OperateGeoprocessingPage>(p => p.Add(x => x.SelectedJobRunId, "gp-run"));
        page.FindAll("button").Single(b => b.TextContent.Contains("Read next events", StringComparison.Ordinal)).Click();

        Assert.Equal([null, "cursor-2"], client.DiagnosticCursors);
        Assert.Contains("tail event", page.Markup);
        Assert.Contains("older event", page.Markup);
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

    private static OperateJobDiagnosticsView Projection(
        string id,
        bool retry = false,
        bool cancel = false,
        string phase = "executing",
        string timing = "measured",
        string disabled = "",
        string message = "worker said [REDACTED] under <scratch>",
        bool truncated = false,
        string? first = null,
        string? next = null,
        IReadOnlyList<string>? missing = null) =>
        new(
            Schema: "honua.job-diagnostics/v1",
            JobRunId: id,
            Phase: phase,
            TimingSource: timing,
            AttemptNumber: 2,
            MaxAttempts: 3,
            RetryAllowed: retry,
            RetryRequiresApproval: false,
            RetryDisabledReason: disabled,
            WouldRepeat: retry ? "Repeats the whole job on postgis as attempt 3." : string.Empty,
            CancelOffered: cancel,
            FailureClassification: phase == "failed" ? "execution_failed" : null,
            FailureRecovery: phase == "failed" ? "Retry the job after the process is corrected." : null,
            FailurePhase: phase == "failed" ? "executing" : null,
            Steps: [new OperateJobDiagnosticStep(1, phase, "running", timing, "3 s", message)],
            Events: [new OperateJobDiagnosticEvent("2026-05-24 20:00 UTC", "info", message, phase)],
            EventsTruncated: truncated,
            EventsFirstCursor: first,
            EventsNextCursor: next,
            EventsTotalCount: 4,
            EvidenceMissing: missing ?? ["release.lockDigest"],
            EvidenceTruncated: truncated ? ["events"] : []);

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

        public OperateSectionResult<OperateJobStepsView>? Steps { get; init; }

        public OperateSectionResult<OperateJobDiagnosticsView>? Diagnostics { get; init; }

        public Func<string?, OperateSectionResult<OperateJobDiagnosticsView>>? DiagnosticsByCursor { get; init; }

        public List<string?> DiagnosticCursors { get; } = [];

        public int StepFetchCount { get; private set; }

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

        public Task<OperateSectionResult<OperateJobStepsView>> GetJobStepsAsync(string jobRunId, CancellationToken cancellationToken = default)
        {
            StepFetchCount++;
            return Task.FromResult(Steps ?? OperateSectionResult<OperateJobStepsView>.Allowed(OperateJobStepsView.Empty));
        }

        public Task<OperateSectionResult<OperateJobDiagnosticsView>> GetJobDiagnosticsAsync(
            string jobRunId,
            string? eventCursor = null,
            CancellationToken cancellationToken = default)
        {
            DiagnosticCursors.Add(eventCursor);
            if (DiagnosticsByCursor is not null)
            {
                return Task.FromResult(DiagnosticsByCursor(eventCursor));
            }

            return Task.FromResult(Diagnostics ?? OperateSectionResult<OperateJobDiagnosticsView>.Allowed(Projection(jobRunId)));
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
