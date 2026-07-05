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
        var provider = services.BuildServiceProvider();

        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<OperateHealthPage>(ParameterView.Empty);
            return output.ToHtmlString();
        });
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

    private sealed class StubOpsHealthDataSource : IOpsHealthDataSource
    {
        public OperateSectionResult<OpsHealthView> Result { get; init; } =
            OperateSectionResult<OpsHealthView>.Denied(OperateSectionStatus.Unavailable, "n/a");

        public Task<OperateSectionResult<OpsHealthView>> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result);
    }
}
