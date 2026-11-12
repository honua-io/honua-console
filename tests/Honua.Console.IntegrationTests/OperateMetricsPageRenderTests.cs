using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class OperateMetricsPageRenderTests
{
    [Fact]
    public void Metrics_WithLiveData_RendersBarsRecommendationsAlertsAndHealthEntries()
    {
        var stub = new StubMetricsDataSource { Snapshot = BuildLiveSnapshot() };

        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton<IOperateMetricsDataSource>(stub);

        var page = ctx.Render<OperateMetricsPage>();

        page.WaitForAssertion(
            () =>
            {
                // Connection-pool utilization bar + query admission.
                Assert.NotNull(page.Find("#connection-pool .operate-metric-bar"));
                Assert.Contains("42.00%", page.Markup, StringComparison.Ordinal);
                Assert.Contains("Query admission", page.Markup, StringComparison.Ordinal);
                Assert.Contains("adaptive", page.Markup, StringComparison.Ordinal);

                // Cache hit-ratio bar + server recommendations are surfaced.
                Assert.NotNull(page.Find("#cache .operate-metric-bar"));
                Assert.Contains("Tuning recommendations", page.Markup, StringComparison.Ordinal);
                Assert.Contains("tune the cache TTL", page.Markup, StringComparison.Ordinal);

                // Memory / GC.
                Assert.Contains("512 MB", page.Markup, StringComparison.Ordinal);
                Assert.Contains("gen0 12", page.Markup, StringComparison.Ordinal);

                // Upload-queue depth bar.
                Assert.NotNull(page.Find("#upload-queue .operate-metric-bar"));

                // Database resilience error-rate + server alerts are surfaced.
                Assert.NotNull(page.Find("#database-resilience .operate-metric-bar"));
                Assert.Contains("Database alerts", page.Markup, StringComparison.Ordinal);
                Assert.Contains("error rate is very high", page.Markup, StringComparison.Ordinal);

                // Comprehensive health entries (name/status/duration).
                var healthRows = page.FindAll("#health table.operate-table tbody tr");
                Assert.Equal(2, healthRows.Count);
                Assert.Contains("database", page.Markup, StringComparison.Ordinal);

                // Color-coded for fast triage.
                Assert.Contains("console-state-success", page.Markup, StringComparison.Ordinal);
                Assert.Contains("console-state-danger", page.Markup, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
    }

    private static readonly (string Id, string V4, string Legacy)[] SectionHeadings =
    [
        ("connection-pool-heading", "Connection pool and query admission", "Connection Pool And Query Admission"),
        ("cache-heading", "Cache hit ratio", "Cache Hit Ratio"),
        ("resources-heading", "Memory and garbage collection", "Memory And Garbage Collection"),
        ("upload-queue-heading", "Upload queue depth", "Upload Queue Depth"),
        ("database-resilience-heading", "Circuit breaker and error rate", "Circuit Breaker And Error Rate"),
        ("health-heading", "Dependency health checks", "Dependency Health Checks"),
    ];

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public void Metrics_PresentationKeepsEverySectionAndBar(ConsolePresentationMode presentation)
    {
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = presentation == ConsolePresentationMode.V4 });
        ctx.Services.AddSingleton<IOperateMetricsDataSource>(new StubMetricsDataSource { Snapshot = BuildLiveSnapshot() });

        var page = ctx.Render<OperateMetricsPage>();

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("#health table.operate-table tbody tr").Count), TimeSpan.FromSeconds(5));
        var v4 = presentation == ConsolePresentationMode.V4;
        Assert.Equal(v4 ? "Server metrics" : "Operate Metrics", Assert.Single(page.FindAll("h1")).TextContent);
        Assert.Equal(v4 ? 1 : 0, page.FindAll(".console-page-heading-v4").Count);
        Assert.Equal(v4 ? 1 : 0, page.FindAll(".console-monitoring-v4.console-metrics-v4").Count);
        Assert.Equal(v4, page.Markup.Contains("operate-monitoring-v4.css", StringComparison.Ordinal));
        foreach (var (id, v4Text, legacyText) in SectionHeadings)
        {
            Assert.Equal(v4 ? v4Text : legacyText, page.Find($"#{id}").TextContent);
        }
        Assert.Equal(6, page.FindAll(".operate-section-nav a").Count);
        foreach (var section in new[] { "#connection-pool", "#cache", "#upload-queue", "#database-resilience" })
        {
            Assert.NotEmpty(page.FindAll($"{section} .operate-metric-bar"));
        }

        if (v4) CatalogAdminPreview.CaptureV4("metrics-live", page.Markup);
    }

    [Fact]
    public void Metrics_WhenNoEnvironmentBound_RendersMissingBindingState()
    {
        var unavailable = OperateMetricsAllUnavailable(
            "No active environment profile is selected. Connect an environment to load server metrics.");
        var stub = new StubMetricsDataSource { Snapshot = unavailable };

        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton<IOperateMetricsDataSource>(stub);

        var page = ctx.Render<OperateMetricsPage>();

        page.WaitForAssertion(
            () =>
            {
                Assert.Contains("Temporarily unavailable", page.Markup, StringComparison.Ordinal);
                Assert.Contains("Connect an environment to load server metrics", page.Markup, StringComparison.Ordinal);
                // No fabricated metric bars are rendered.
                Assert.Empty(page.FindAll(".operate-metric-bar span"));
            },
            TimeSpan.FromSeconds(5));
    }

    private static OperateMetricsSnapshot OperateMetricsAllUnavailable(string message) => new(
        OperateMetricSection<OperateConnectionPoolMetric>.Denied(OperateSectionStatus.Unavailable, message),
        OperateMetricSection<OperateCacheMetric>.Denied(OperateSectionStatus.Unavailable, message),
        OperateMetricSection<OperateResourceMetric>.Denied(OperateSectionStatus.Unavailable, message),
        OperateMetricSection<OperateUploadQueueMetric>.Denied(OperateSectionStatus.Unavailable, message),
        OperateMetricSection<OperateDatabaseResilienceMetric>.Denied(OperateSectionStatus.Unavailable, message),
        OperateMetricSection<OperateComprehensiveHealthMetric>.Denied(OperateSectionStatus.Unavailable, message));

    private static OperateMetricsSnapshot BuildLiveSnapshot()
    {
        var success = new OperateStatus("healthy", "ok");
        var danger = new OperateStatus("critical", "bad");

        var pool = new OperateConnectionPoolMetric(
            new OperateMetricBar(42, "42.00%", success, HasData: true),
            success,
            "Healthy",
            TotalFailures: 3,
            TotalTimeouts: 1,
            new OperateQueryAdmissionMetric(
                AdaptiveEnabled: true,
                CurrentLimit: 8,
                MinLimit: 2,
                MaxLimit: 16,
                InFlight: 5,
                AvailableSlots: 3,
                QueuedWaiters: 0,
                TargetDurationMs: 50,
                DurationEwmaMs: 47.5,
                QueueWaitEwmaMs: 1.2,
                AdjustmentCount: 4,
                LastAdjustmentDirection: "up",
                new OperateMetricBar(62.5, "5 / 8", success, HasData: true)),
            "2026-06-06 10:00:00 UTC");

        var cache = new OperateCacheMetric(
            new OperateMetricBar(62, "62.00%", new OperateStatus("warning", "low"), HasData: true),
            new OperateStatus("warning", "low"),
            ["Consider tune the cache TTL values or cache size limits."],
            "2026-06-06 10:00:00 UTC");

        var resources = new OperateResourceMetric(
            MemoryUsageMB: 512,
            MemoryUsageBytes: 512L * 1024 * 1024,
            MemoryPressureLevel: "low",
            PressureStatus: success,
            Health: success,
            Gen0Collections: 12,
            Gen1Collections: 4,
            Gen2Collections: 1,
            GcTotalMemoryBytes: 256L * 1024 * 1024,
            "2026-06-06 10:00:00 UTC");

        var queue = new OperateUploadQueueMetric(
            new OperateMetricBar(20, "2 / 10", success, HasData: true),
            QueueDepthValue: 2,
            MaxQueueDepth: 10,
            ActiveUploads: 1,
            MaxConcurrentUploads: 4,
            success,
            "2026-06-06 10:00:00 UTC");

        var db = new OperateDatabaseResilienceMetric(
            new OperateMetricBar(12, "12.00%", danger, HasData: true),
            CircuitBreakerEnabled: true,
            ConnectionFailures: 2,
            QueryFailures: 7,
            new OperateMetricBar(91, "91.00%", danger, HasData: true),
            danger,
            "Degraded",
            ["Critical: Database query error rate is very high (12.00%)"],
            "2026-06-06 10:00:00 UTC");

        var health = new OperateComprehensiveHealthMetric(
            new OperateStatus("degraded", "degraded"),
            "Degraded",
            "142.0 ms",
            [
                new OperateHealthCheckEntry("database", success, "Healthy", "40.0 ms", "OK"),
                new OperateHealthCheckEntry("cache", new OperateStatus("warning", "slow"), "Degraded", "102.0 ms", "Slow"),
            ],
            "2026-06-06 10:00:00 UTC");

        return new OperateMetricsSnapshot(
            OperateMetricSection<OperateConnectionPoolMetric>.Allowed(pool),
            OperateMetricSection<OperateCacheMetric>.Allowed(cache),
            OperateMetricSection<OperateResourceMetric>.Allowed(resources),
            OperateMetricSection<OperateUploadQueueMetric>.Allowed(queue),
            OperateMetricSection<OperateDatabaseResilienceMetric>.Allowed(db),
            OperateMetricSection<OperateComprehensiveHealthMetric>.Allowed(health));
    }

    private sealed class StubMetricsDataSource : IOperateMetricsDataSource
    {
        public required OperateMetricsSnapshot Snapshot { get; init; }

        public Task<OperateMetricsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Snapshot);
    }
}
