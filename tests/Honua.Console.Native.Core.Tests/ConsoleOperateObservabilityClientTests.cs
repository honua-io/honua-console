using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Honua.Console.Native.Core.Tests;

public sealed class ConsoleOperateObservabilityClientTests
{
    [Fact]
    public async Task OverviewUsesAdminStatusEndpointsAndServerMetadata()
    {
        var serverTime = DateTimeOffset.Parse("2026-05-24T20:00:00Z");
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/admin/version" => JsonResponse(
                new ConsoleApiEnvelope<AdminVersionResponse>
                {
                    Success = true,
                    Data = new AdminVersionResponse
                    {
                        Version = "2026.05.24+abcdef1234567890",
                        MetadataApiVersion = "metadata-v2",
                        MetadataSchemaVersion = "schema-2",
                        ServerTime = serverTime
                    }
                },
                OperateObservabilityJsonContext.Default.AdminVersionEnvelope),
            "/api/v1/admin/capabilities" => JsonResponse(
                new ConsoleApiEnvelope<AdminCapabilitiesResponse>
                {
                    Success = true,
                    Data = new AdminCapabilitiesResponse
                    {
                        MetadataApiVersion = "metadata-v2",
                        MetadataSchemaVersion = "schema-2",
                        ServerVersion = "2026.05.24+abcdef1234567890"
                    }
                },
                OperateObservabilityJsonContext.Default.AdminCapabilitiesEnvelope),
            "/api/v1/admin/observability/telemetry" => JsonResponse(
                new OperateTelemetryStatusResponse
                {
                    GeneratedAt = serverTime.AddMinutes(5),
                    Realtime = new OperateRealtimeStatusResponse
                    {
                        Supported = true,
                        HubPath = "/api/v1/admin/realtime",
                        Protocol = "signalr",
                        Events = ["statusChanged"]
                    },
                    TracingEnabled = true,
                    MetricsEnabled = true,
                    LogsEnabled = false,
                    OtlpConfigured = true,
                    OtlpEndpointValid = true,
                    OtlpExporterState = "configured",
                    TraceExportState = "configured",
                    MetricsExportState = "configured",
                    LogExportState = "disabled"
                },
                OperateObservabilityJsonContext.Default.OperateTelemetryStatusResponse),
            "/api/v1/admin/observability/migrations" => JsonResponse(
                new OperateMigrationStatusResponse
                {
                    Status = "succeeded",
                    IsReady = true,
                    PlanAvailable = true,
                    GeneratedAt = serverTime.AddMinutes(4)
                },
                OperateObservabilityJsonContext.Default.OperateMigrationStatusResponse),
            "/api/v1/admin/observability/errors" => JsonResponse(
                new OperateRecentErrorsResponse
                {
                    Capacity = 32,
                    InstanceId = "server-live-1",
                    Errors = []
                },
                OperateObservabilityJsonContext.Default.OperateRecentErrorsResponse),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var client = CreateClient(handler);

        var result = await client.GetOverviewAsync();

        Assert.Equal(OperateSectionStatus.Allowed, result.Status);
        var environment = Assert.Single(result.Value!.Environments);
        Assert.Equal("2026.05.24", environment.Version);
        Assert.Equal("abcdef123456", environment.BuildSha);
        Assert.Equal("healthy", environment.Health.Label);
        Assert.Contains("no pending drift", environment.DriftSummary);

        Assert.Contains(result.Value.TelemetryFacts, fact => fact.Signal == "Trace export" && fact.State.Label == "configured");
        Assert.Contains(result.Value.TelemetryFacts, fact => fact.Signal == "Log export" && fact.State.Label == "disabled");
        Assert.Contains(result.Value.TelemetryFacts, fact => fact.Signal == "Admin realtime" && fact.State.Label == "healthy");
        Assert.Contains(result.Value.CompatibilityRows, row => row.Capability == "Metadata compatibility" && row.Current.Contains("metadata-v2", StringComparison.Ordinal));

        var paths = handler.Requests.Select(request => request.RequestUri!.AbsolutePath).ToHashSet(StringComparer.Ordinal);
        var expectedPaths = new HashSet<string>(StringComparer.Ordinal)
        {
            "/api/v1/admin/version",
            "/api/v1/admin/capabilities",
            "/api/v1/admin/observability/telemetry",
            "/api/v1/admin/observability/migrations",
            "/api/v1/admin/observability/errors"
        };
        Assert.True(expectedPaths.SetEquals(paths), string.Join(", ", paths.OrderBy(path => path, StringComparer.Ordinal)));
        Assert.DoesNotContain(handler.Requests, request => request.RequestUri!.AbsolutePath == "/api/v1/admin/observability/events");
        Assert.All(handler.Requests, request => Assert.Equal(new AuthenticationHeaderValue("Bearer", "live-token"), request.Headers.Authorization));
    }

    [Fact]
    public async Task EventQueryUsesAdminRouteServerFiltersAndBearerToken()
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.Equal("/api/v1/admin/observability/events", request.RequestUri?.AbsolutePath);
            return JsonResponse(new OperateEventPageResponse
            {
                Items =
                [
                    new OperateEventResponse
                    {
                        EventId = "job:job-live-1",
                        Kind = "job",
                        Severity = "warning",
                        OccurredAt = DateTimeOffset.Parse("2026-05-24T20:00:00Z"),
                        Title = "Live job warning",
                        OperationId = "job-live-1",
                        CorrelationId = "corr-live",
                        TraceId = "trace-live",
                        RequestId = "req-live",
                        ServiceId = "svc-live",
                        ProviderLinks =
                        [
                            new OperateProviderLinkResponse
                            {
                                Provider = "loki",
                                Label = "Provider log",
                                Url = "https://logs.example.invalid/query"
                            }
                        ]
                    }
                ]
            }, OperateObservabilityJsonContext.Default.OperateEventPageResponse);
        });
        var client = CreateClient(handler);

        var result = await client.QueryEventsAsync(new OperateEventQuery
        {
            EventType = "job",
            MinSeverity = "warning",
            CorrelationId = "corr-live",
            TraceId = "trace-live",
            RequestId = "req-live",
            ServiceId = "svc-live",
            ResourceRef = "job/job-live-1",
            Actor = "ops",
            OperationId = "job-live-1",
            ReleaseId = "rel-live",
            ChangeSetId = "cs-live"
        });

        Assert.Equal(OperateSectionStatus.Allowed, result.Status);
        var row = Assert.Single(result.Value!);
        Assert.Equal("job", row.EventType);
        Assert.Equal("job-live-1", row.JobRunId);
        Assert.Contains(row.RawEvidence, link => link.Kind == "loki");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "live-token"), request.Headers.Authorization);
        var query = ConsoleUrlQuery.Parse(request.RequestUri!.Query);
        Assert.Equal("job", query["kind"]);
        Assert.Equal("warning", query["minSeverity"]);
        Assert.Equal("corr-live", query["correlationId"]);
        Assert.Equal("trace-live", query["traceId"]);
        Assert.Equal("req-live", query["requestId"]);
        Assert.Equal("svc-live", query["serviceId"]);
        Assert.Equal("job/job-live-1", query["resourceRef"]);
        Assert.Equal("ops", query["actor"]);
        Assert.Equal("job-live-1", query["operationId"]);
        Assert.Equal("rel-live", query["releaseId"]);
        Assert.Equal("cs-live", query["changeSetId"]);
    }

    [Fact]
    public async Task LogsMapStructuredSearchHistogramAndRawProviderEvidence()
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.Equal("/api/v1/admin/observability/logs", request.RequestUri?.AbsolutePath);
            return JsonResponse(new OperateLogPageResponse
            {
                InstanceId = "server-live-1",
                Capacity = 128,
                Items =
                [
                    new OperateLogEntryResponse
                    {
                        Timestamp = DateTimeOffset.Parse("2026-05-24T20:00:00Z"),
                        Level = "error",
                        Path = "/api/v1/admin/jobs/job-live-1",
                        StatusCode = 500,
                        CorrelationId = "corr-live",
                        Message = "Live provider exception"
                    },
                    new OperateLogEntryResponse
                    {
                        Timestamp = DateTimeOffset.Parse("2026-05-24T20:01:00Z"),
                        Level = "warning",
                        Path = "/api/v1/admin/observability/events",
                        StatusCode = 429,
                        CorrelationId = "corr-rate",
                        Message = "Rate limited"
                    }
                ]
            }, OperateObservabilityJsonContext.Default.OperateLogPageResponse);
        });
        var client = CreateClient(handler);

        var result = await client.GetLogsAsync();

        Assert.Equal(OperateSectionStatus.Allowed, result.Status);
        Assert.Equal("server-live-1", result.Value!.InstanceId);
        Assert.Contains(result.Value.SeverityBuckets, bucket => bucket.Label == "Error" && bucket.Count == 1);
        Assert.Contains(result.Value.ExceptionGroups, group => group.Label == "Live provider exception");
        Assert.All(result.Value.Logs, log => Assert.NotEmpty(log.ProviderLinks));
    }

    [Fact]
    public async Task JobDetailCarriesLogAndArtifactFetchFailures()
    {
        var now = DateTimeOffset.Parse("2026-05-24T20:00:00Z");
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/admin/jobs/live-job-1" => JsonResponse(
                new ConsoleJobDetail
                {
                    JobId = "live-job-1",
                    Kind = "Publishing",
                    Backend = "preview",
                    TargetKind = "map",
                    WorkloadName = "Publish live package",
                    Status = "Running",
                    RequestedBy = "operator.live",
                    CreatedAt = now,
                    UpdatedAt = now.AddMinutes(1),
                    PercentComplete = 0.5,
                    AttemptCount = 1,
                    MaxAttempts = 3,
                    ResourceRefs = ["service:svc-live"],
                    ArtifactCount = 2,
                    Stages =
                    [
                        new ConsoleJobStage
                        {
                            Name = "Validate",
                            Status = "Succeeded",
                            StartedAt = now,
                            CompletedAt = now.AddMinutes(1),
                            PercentComplete = 1
                        }
                    ]
                },
                OperateObservabilityJsonContext.Default.ConsoleJobDetail),
            "/api/v1/admin/jobs/live-job-1/logs" => new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                ReasonPhrase = "Forbidden"
            },
            "/api/v1/admin/jobs/live-job-1/artifacts" => new HttpResponseMessage(HttpStatusCode.NotImplemented)
            {
                ReasonPhrase = "Not Implemented"
            },
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var client = CreateClient(handler);

        var result = await client.GetJobDetailAsync("live-job-1");

        Assert.Equal(OperateSectionStatus.Allowed, result.Status);
        var job = result.Value!;
        Assert.Equal(OperateSectionStatus.Forbidden, job.LogsStatus);
        Assert.Contains("403", job.LogsMessage);
        Assert.Empty(job.Logs);
        Assert.Equal(OperateSectionStatus.Unsupported, job.ArtifactsStatus);
        Assert.Contains("501", job.ArtifactsMessage);
        Assert.Empty(job.Artifacts);
    }

    [Fact]
    public async Task JobDetailPanelRendersSubresourceFailureStatus()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        var provider = services.BuildServiceProvider();

        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var job = BuildRenderingJob(
            stages: [],
            logs: [],
            artifacts: []) with
        {
            LogsStatus = OperateSectionStatus.Forbidden,
            LogsMessage = "403 Forbidden from live job logs endpoint.",
            ArtifactsStatus = OperateSectionStatus.Unsupported,
            ArtifactsMessage = "501 Not Implemented from live job artifact endpoint."
        };

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<JobDetailPanel>(
                ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(JobDetailPanel.Job)] = job
                }));
            return output.ToHtmlString();
        });

        Assert.Contains("403 Forbidden from live job logs endpoint.", html);
        Assert.Contains("501 Not Implemented from live job artifact endpoint.", html);
        Assert.Contains("Permission required", html);
        Assert.Contains("Unsupported by this server", html);
        Assert.DoesNotContain("Job logs are not available.", html);
    }

    [Fact]
    public async Task OperatePageRendersInjectedLiveDataInsteadOfFixtureSentinels()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton<IConsoleOperateObservabilityClient>(new RenderingOperateClient());
        var provider = services.BuildServiceProvider();

        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<OperateObservabilityPage>();
            return output.ToHtmlString();
        });

        Assert.Contains("Live Server Alpha", html);
        Assert.Contains("Live job warning", html);
        Assert.Contains("Live provider exception", html);
        Assert.Contains("Live alert", html);
        Assert.Contains("Harbor Entry Live", html);
        Assert.Contains("Live investigation", html);
        Assert.Contains("live-job-1", html);
        Assert.DoesNotContain("Publish SLO burn alert", html);
        Assert.DoesNotContain("job-publish-001", html);
    }

    [Fact]
    public async Task OperatePageRendersMissingStatesForUnknownEventAndAlertDeepLinks()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton<IConsoleOperateObservabilityClient>(new RenderingOperateClient());
        var provider = services.BuildServiceProvider();

        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<OperateObservabilityPage>(
                ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(OperateObservabilityPage.SelectedEventId)] = "missing-event",
                    [nameof(OperateObservabilityPage.SelectedAlertId)] = "missing-alert"
                }));
            return output.ToHtmlString();
        });

        Assert.Contains("Event &#x27;missing-event&#x27; was not found in the loaded server event page.", html);
        Assert.Contains("Alert &#x27;missing-alert&#x27; was not found in the loaded server alert page.", html);
        Assert.Contains("Live job warning", html);
        Assert.Contains("Live alert", html);
    }

    [Fact]
    public async Task RulesSurfaceCarriesHealthAndGeofenceFetchFailures()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/admin/alerts/rules" => JsonResponse(
                new ConsoleApiEnvelope<AlertRuleResponse[]>
                {
                    Success = true,
                    Data =
                    [
                        new AlertRuleResponse
                        {
                            RuleId = 12,
                            ServiceId = "svc-live",
                            LayerId = 1,
                            RuleName = "Harbor Entry Live",
                            TriggerType = "enter",
                            CooldownSeconds = 60,
                            Severity = "warning",
                            Channels = ["websocket"],
                            IsActive = true
                        }
                    ]
                },
                OperateObservabilityJsonContext.Default.AlertRuleListEnvelope),
            "/api/v1/admin/alerts/rules/12/health" => new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                ReasonPhrase = "Forbidden"
            },
            "/api/v1/admin/alerts/zones" => new HttpResponseMessage(HttpStatusCode.NotImplemented)
            {
                ReasonPhrase = "Not Implemented"
            },
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var client = CreateClient(handler);

        var result = await client.GetRulesAsync();

        Assert.Equal(OperateSectionStatus.Allowed, result.Status);
        var rule = Assert.Single(result.Value!.Rules);
        Assert.NotEqual("healthy", rule.Status.Label);
        Assert.Contains(rule.ValidationMessages, message => message.Contains("Rule health unavailable", StringComparison.Ordinal));
        Assert.False(rule.CanEnable);
        Assert.Equal(OperateSectionStatus.Unsupported, result.Value.ZonesStatus);
        Assert.Contains("501", result.Value.ZonesMessage);
    }

    [Fact]
    public async Task InvestigationsCarryDetailFailureStateInsteadOfHidingMissingPinsAndLinks()
    {
        var now = DateTimeOffset.Parse("2026-05-24T20:00:00Z");
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/admin/investigations" => JsonResponse(
                new InvestigationPageResponse
                {
                    Items =
                    [
                        new InvestigationSummaryResponse
                        {
                            InvestigationId = "inv-live-1",
                            Title = "Live investigation",
                            Status = "open",
                            CreatedAt = now,
                            UpdatedAt = now.AddMinutes(5),
                            CreatedBy = "operator.live",
                            Summary = "Summary only",
                            PinCount = 1,
                            LinkCount = 2
                        }
                    ]
                },
                OperateObservabilityJsonContext.Default.InvestigationPageResponse),
            "/api/v1/admin/investigations/inv-live-1" => new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                ReasonPhrase = "Forbidden"
            },
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var client = CreateClient(handler);

        var result = await client.GetInvestigationsAsync();

        Assert.Equal(OperateSectionStatus.Allowed, result.Status);
        var investigation = Assert.Single(result.Value!);
        Assert.True(investigation.HasIncompleteDetails);
        Assert.Contains("403", investigation.DetailMessage);
        Assert.Contains(investigation.Notes, note => note.Contains("Investigation detail unavailable", StringComparison.Ordinal));
        Assert.Empty(investigation.PinnedEventIds);
        Assert.Empty(investigation.LinkedAlertIds);
        Assert.Empty(investigation.LinkedJobRunIds);
    }

    private static HttpConsoleOperateObservabilityClient CreateClient(HttpMessageHandler handler)
    {
        var profile = new ConsoleEnvironmentProfile
        {
            Id = "live",
            DisplayName = "Live Server Alpha",
            ServerBaseUri = new Uri("https://server.example"),
            UpdatedAt = DateTimeOffset.Parse("2026-05-24T19:00:00Z"),
            Account = new ConsoleAccountBinding
            {
                AuthMode = ConsoleAccountAuthMode.AccountRbac,
                AccountId = "operator.live"
            }
        };
        var profiles = new InMemoryConsoleEnvironmentProfileStore([profile], activeProfileId: profile.Id);
        var sessions = new InMemoryConsoleAccountSessionStore();
        sessions.SaveSessionAsync(new ConsoleAccountSession
        {
            ProfileId = profile.Id,
            AccountId = "operator.live",
            AccessToken = "live-token"
        }).GetAwaiter().GetResult();

        return new HttpConsoleOperateObservabilityClient(new HttpClient(handler), profiles, sessions);
    }

    private static HttpResponseMessage JsonResponse<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        var json = JsonSerializer.Serialize(value, typeInfo);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(CloneRequest(request));
            return Task.FromResult(responder(request));
        }

        private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return clone;
        }
    }

    private sealed class RenderingOperateClient : IConsoleOperateObservabilityClient
    {
        public Task<OperateSectionResult<OperateFleetOverview>> GetOverviewAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<OperateFleetOverview>.Allowed(new OperateFleetOverview(
                Environments:
                [
                    new(
                        "live",
                        "Live Server Alpha",
                        "server.example",
                        "2026.05.24",
                        "abc1234",
                        new OperateStatus("healthy", "Live server responded."),
                        "2026-05-24 20:00 UTC",
                        "Platform Ops",
                        "No drift")
                ],
                TelemetryFacts:
                [
                    new("live", "server.example", "Admin observability API", new OperateStatus("healthy", "Responded."), "fresh", "/operate/observability")
                ],
                CompatibilityRows:
                [
                    new("SDK contract projection", "thin HttpClient shim", "honua-sdk-dotnet", new OperateStatus("unknown", "Shim"), "Honua.Console.Contracts")
                ])));

        public Task<OperateSectionResult<IReadOnlyList<OperateEventRow>>> QueryEventsAsync(
            OperateEventQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<OperateEventRow>>.Allowed(
            [
                new(
                    "job:live-job-1",
                    "2026-05-24 20:00 UTC",
                    "warning",
                    "job",
                    "jobs",
                    "Live job warning",
                    "live",
                    "server.example",
                    "corr-live",
                    "trace-live",
                    "req-live",
                    "live-job-1",
                    null,
                    [new OperateEvidenceLink("event", "Live event", "/operate/events/job%3Alive-job-1", "Raw live event")],
                    [new OperateRelatedObject("job", "live-job-1", "/operate/jobs/live-job-1")],
                    [],
                    null)
            ]));

        public Task<OperateSectionResult<OperateLogsView>> GetLogsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<OperateLogsView>.Allowed(new OperateLogsView(
                "server-live-1",
                128,
                [
                    new(
                        "2026-05-24 20:00 UTC",
                        "error",
                        new OperateStatus("error", "Live provider exception"),
                        "/api/v1/admin/jobs/live-job-1",
                        500,
                        "corr-live",
                        "Live provider exception",
                        [new OperateEvidenceLink("server log", "corr-live", "/operate/observability#logs", "Live log")])
                ],
                [new OperateLogGroup("Error", 1, new OperateStatus("error", "One error"))],
                [new OperateLogGroup("Live provider exception", 1, new OperateStatus("error", "Grouped"))])));

        public Task<OperateSectionResult<IReadOnlyList<OperateAlertRecord>>> GetAlertsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<OperateAlertRecord>>.Allowed(
            [
                new(
                    "42",
                    "Live alert",
                    "critical",
                    new OperateStatus("open", "Live alert open"),
                    "realtime:enter",
                    "svc-live",
                    "2026-05-24 20:00 UTC",
                    "2026-05-24 20:00 UTC",
                    ["service:svc-live"],
                    [new OperateEvidenceLink("event", "Alert 42", "/operate/events/alert%3A42", "Raw live alert")],
                    null,
                    [new OperateAlertAction("Acknowledge", false, "Server policy disabled in test")])
            ]));

        public Task<OperateSectionResult<OperateRulesView>> GetRulesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<OperateRulesView>.Allowed(new OperateRulesView(
                [
                    new(
                        "12",
                        "Harbor Entry Live",
                        "geofence:enter",
                        true,
                        new OperateStatus("healthy", "Rule healthy"),
                        "Enter trigger",
                        "websocket",
                        "2026-05-24 20:00 UTC",
                        1,
                        0,
                        [],
                        3,
                        "2026-05-24 19:59 UTC")
                ],
                [
                    new("7", "Harbor Zone Live", "svc-live", true, 4326, "WKT geometry (24 chars)", [])
                ])));

        public Task<OperateSectionResult<IReadOnlyList<OperateJobRun>>> GetJobsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<OperateJobRun>>.Allowed([BuildRenderingJob(stages: [], logs: [], artifacts: [])]));

        public Task<OperateSectionResult<OperateJobRun>> GetJobDetailAsync(string jobRunId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<OperateJobRun>.Allowed(BuildRenderingJob(
                stages: [new OperateJobStage("Validate", new OperateStatus("succeeded", "Done"), 100, "completed")],
                logs: ["Live job detail log"],
                artifacts: [new OperateEvidenceLink("artifact", "Live artifact", "/operate/jobs/live-job-1#artifacts", "Live report")])));

        public Task<OperateSectionResult<IReadOnlyList<OperateInvestigation>>> GetInvestigationsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<OperateInvestigation>>.Allowed(
            [
                new(
                    "inv-live-1",
                    "Live investigation",
                    new OperateStatus("open", "Live investigation"),
                    "operator.live",
                    "2026-05-24 20:00 UTC - 2026-05-24 20:05 UTC",
                    ["job:live-job-1"],
                    ["42"],
                    ["live-job-1"],
                    ["Live investigation note"])
            ]));

        public Task<OperateSectionResult<IReadOnlyList<OperateRecentError>>> GetRecentErrorsAsync(int limit = 10, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<OperateRecentError>>.Allowed([]));

    }

    private static OperateJobRun BuildRenderingJob(
        IReadOnlyList<OperateJobStage> stages,
        IReadOnlyList<string> logs,
        IReadOnlyList<OperateEvidenceLink> artifacts) =>
        new(
            "live-job-1",
            "Publishing",
            "preview",
            "default",
            new OperateStatus("running", "Live job running"),
            "operator.live",
            "2026-05-24 20:00 UTC",
            "live",
            "server.example",
            45,
            "none",
            ["service:svc-live"],
            stages,
            logs,
            artifacts,
            [new OperateJobMetric("Artifacts", "1", new OperateStatus("info", "Artifact count"))],
            [new OperateJobAction("Cancel", false, "Server policy disabled in test")],
            [new OperateRelatedObject("events", "Events for this job", "/api/v1/admin/observability/events?kind=job&operationId=live-job-1")]);
}
