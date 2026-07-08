using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Honua.Console.Native.Core.Tests;

public sealed class OperateHealthPageTests
{
    [Fact]
    public async Task RendersSnapshotSectionsAndFlagsSloBreach()
    {
        var view = OpsHealthDataSource.Map(BuildSnapshot());
        var html = await RenderAsync(new StubOpsHealthDataSource
        {
            Result = OperateSectionResult<OpsHealthView>.Allowed(view)
        });

        Assert.Contains("Ops Health", html);
        Assert.Contains("GeoServices", html);
        Assert.Contains("Per-Protocol Latency", html);
        Assert.Contains("SLO breach", html);          // GeoServices p95 breach flagged.
        Assert.Contains("Platform release", html);
        Assert.Contains("worker-plane", html);         // skewed plane listed.
        Assert.Contains("Cache hit ratio", html);

        // console#292 scope item 3: every degraded/breach badge deep-links to its actionable
        // surface — no dead ends. The fixture's SLO breach and deploy-readiness "blocked" status
        // both breach, so both drilldowns must render.
        Assert.Contains("data-health-drilldown=\"serving-latency\"", html, StringComparison.Ordinal);
        Assert.Contains("data-health-drilldown=\"deploy\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/operate/copilot\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/operate/deploy#deploy-approvals\"", html, StringComparison.Ordinal);

        // The healthy geoprocessing/alert-dispatch sections in this fixture are not breaches,
        // so they must NOT render a drilldown link (a healthy badge is not a dead end either).
        Assert.DoesNotContain("data-health-drilldown=\"geoprocessing\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-health-drilldown=\"alert-dispatch\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RendersTrendChartsWithBreachChipWhenHistoryIsAllowed()
    {
        var view = OpsHealthDataSource.Map(BuildSnapshot());
        var trend = OpsHealthTrendMapper.Map(BuildHistory(), OpsHealthTrendRangeSelection.LastHour.Label);
        var html = await RenderAsync(new StubOpsHealthDataSource
        {
            Result = OperateSectionResult<OpsHealthView>.Allowed(view),
            TrendResult = OperateSectionResult<OpsHealthTrendView>.Allowed(trend)
        });

        Assert.Contains("Trend charts", html);
        Assert.Contains("data-trend-grid", html, StringComparison.Ordinal);
        Assert.Contains("GeoServices latency", html);
        Assert.Contains("data-trend-breach-chip", html, StringComparison.Ordinal); // p95 1500ms breaches.
        Assert.Contains("Geoprocessing queue", html);
        Assert.Contains("Alert-dispatch backlog", html);
    }

    [Fact]
    public async Task RendersFirstRunSkeletonWhenHistoryIsEmpty()
    {
        var view = OpsHealthDataSource.Map(BuildSnapshot());
        var emptyHistory = new OpsHealthHistoryResponse
        {
            GeneratedAt = DateTimeOffset.Parse("2026-06-06T10:00:00Z"),
            Resolution = "1m",
            WindowSeconds = 3600,
            From = DateTimeOffset.Parse("2026-06-06T09:00:00Z"),
            To = DateTimeOffset.Parse("2026-06-06T10:00:00Z"),
            PerReplica = false,
            Latency = [],
            Vitals = []
        };
        var trend = OpsHealthTrendMapper.Map(emptyHistory, OpsHealthTrendRangeSelection.LastHour.Label);
        var html = await RenderAsync(new StubOpsHealthDataSource
        {
            Result = OperateSectionResult<OpsHealthView>.Allowed(view),
            TrendResult = OperateSectionResult<OpsHealthTrendView>.Allowed(trend)
        });

        Assert.Contains("Collecting baseline", html);
        Assert.Contains("first signal in ~60s", html);
    }

    [Fact]
    public async Task RendersUnsupportedTrendMessageAgainstAnOlderServer()
    {
        var view = OpsHealthDataSource.Map(BuildSnapshot());
        var html = await RenderAsync(new StubOpsHealthDataSource
        {
            Result = OperateSectionResult<OpsHealthView>.Allowed(view),
            TrendResult = OperateSectionResult<OpsHealthTrendView>.Denied(OperateSectionStatus.Unsupported, "n/a")
        });

        Assert.Contains("does not yet expose ops-health history", html);
    }

    [Fact]
    public async Task RendersMissingBindingWhenUnbound()
    {
        var html = await RenderAsync(new StubOpsHealthDataSource
        {
            Result = OperateSectionResult<OpsHealthView>.Denied(
                OperateSectionStatus.Unavailable,
                "No active environment profile is selected. Connect an environment to load the ops-health snapshot.")
        });

        Assert.Contains("Temporarily unavailable", html);
        Assert.Contains("Connect an environment", html);
    }

    [Fact]
    public async Task RendersForbiddenState()
    {
        var html = await RenderAsync(new StubOpsHealthDataSource
        {
            Result = OperateSectionResult<OpsHealthView>.Denied(
                OperateSectionStatus.Forbidden,
                "The active environment profile is not permitted to read this surface.")
        });

        Assert.Contains("Permission required", html);
        Assert.Contains("not permitted", html);
    }

    private static async Task<string> RenderAsync(IOpsHealthDataSource dataSource)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(dataSource);
        // The Trends section renders ChartPreview, which [Inject]s IJSRuntime. Static HtmlRenderer
        // never runs OnAfterRender, so JS is never actually invoked during these render tests —
        // this no-op only satisfies DI (mirrors OperateTransitionDataSourceTests' NoOpJsRuntime for
        // MapPreview).
        services.AddSingleton<Microsoft.JSInterop.IJSRuntime>(new NoOpJsRuntime());
        var provider = services.BuildServiceProvider();

        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<OperateHealthPage>(ParameterView.Empty);
            return output.ToHtmlString();
        });
    }

    private sealed class NoOpJsRuntime : Microsoft.JSInterop.IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => default;

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args) => default;
    }

    private static OpsHealthSnapshotResponse BuildSnapshot() => new()
    {
        GeneratedAt = DateTimeOffset.Parse("2026-06-06T10:00:00Z"),
        OverallStatus = "Degraded",
        Health = new OpsHealthChecksResponse
        {
            Status = "Degraded",
            TotalDurationMs = 142,
            Entries = [new OpsHealthCheckEntryResponse { Name = "database", Status = "Healthy", DurationMs = 40 }]
        },
        ServingLatency = new OpsServingLatencyResponse
        {
            WindowSeconds = 300,
            Protocols =
            [
                new OpsServingLatencyProtocolResponse
                {
                    Protocol = "GeoServices",
                    RequestCount = 1200,
                    ErrorCount = 2,
                    ErrorRate = 0.002,
                    P50Ms = 40,
                    P95Ms = 1500,
                    P99Ms = 2100,
                    MaxMs = 3000
                }
            ]
        },
        Geoprocessing = new OpsGpQueueResponse
        {
            TotalActive = 1,
            Available = true,
            Buckets = [new OpsGpQueueBucketResponse { Status = "Running", Backend = "local", Count = 1 }]
        },
        AlertDispatch = new OpsAlertDispatchResponse
        {
            DispatcherRunning = true,
            DispatcherEnabled = true,
            StoragePollFailing = false,
            PendingCount = 0,
            DeadLetteredCount = 0
        },
        Deploy = new OpsDeployReadinessResponse
        {
            Status = "blocked",
            ReadyForCoordinatedDeploy = false,
            PendingMigrationsCount = 1,
            PendingContractScriptsCount = 1,
            PlatformRelease = new OpsPlatformReleaseResponse
            {
                ReleaseVersion = "2026.06.1",
                ReleaseDeclared = true,
                IsCoVersioned = false,
                SkewedIds = ["worker-plane"]
            }
        },
        Database = new OpsDatabaseResponse
        {
            ConnectionPoolUtilization = 0.42,
            HasConnectionPoolData = true,
            ActiveConnections = 5,
            CacheHitRatio = 0.91,
            ErrorRate = 0.004
        }
    };

    private static OpsHealthHistoryResponse BuildHistory() => new()
    {
        GeneratedAt = DateTimeOffset.Parse("2026-06-06T10:00:00Z"),
        Resolution = "1m",
        WindowSeconds = 3600,
        From = DateTimeOffset.Parse("2026-06-06T09:00:00Z"),
        To = DateTimeOffset.Parse("2026-06-06T10:00:00Z"),
        PerReplica = false,
        Latency =
        [
            new OpsHealthHistoryLatencySeriesResponse
            {
                Protocol = "GeoServices",
                Points =
                [
                    new OpsHealthHistoryLatencyPointResponse
                    {
                        BucketStart = DateTimeOffset.Parse("2026-06-06T09:55:00Z"),
                        RequestCount = 100,
                        ErrorCount = 2,
                        ErrorRate = 0.002,
                        P50Ms = 40,
                        P95Ms = 1500, // breaches the shared p95 SLO heuristic.
                        P99Ms = 2100,
                        MaxMs = 3000
                    }
                ]
            }
        ],
        Vitals =
        [
            new OpsHealthHistoryVitalsPointResponse
            {
                BucketStart = DateTimeOffset.Parse("2026-06-06T09:55:00Z"),
                OverallStatus = "Degraded",
                GpQueueTotal = 1,
                GpQueueBreakdown = new Dictionary<string, int> { ["Running|local"] = 1 },
                AlertPending = 4,
                AlertDeadLettered = 1,
                DbActiveConnections = 5,
                CacheHitRatio = 0.91,
                ErrorRate = 0.004
            }
        ]
    };

    private sealed class StubOpsHealthDataSource : IOpsHealthDataSource
    {
        public OperateSectionResult<OpsHealthView> Result { get; init; } =
            OperateSectionResult<OpsHealthView>.Denied(OperateSectionStatus.Unavailable, "n/a");

        public OperateSectionResult<OpsHealthTrendView> TrendResult { get; init; } =
            OperateSectionResult<OpsHealthTrendView>.Denied(OperateSectionStatus.Unsupported, "n/a");

        public Task<OperateSectionResult<OpsHealthView>> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result);

        public Task<OperateSectionResult<OpsHealthTrendView>> GetHistoryAsync(
            OpsHealthTrendRangeSelection selection, CancellationToken cancellationToken = default) =>
            Task.FromResult(TrendResult);
    }
}
