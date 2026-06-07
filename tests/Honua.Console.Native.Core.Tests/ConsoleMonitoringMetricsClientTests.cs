using System.Net;
using System.Text;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Recording-HttpClient unit tests proving the monitoring metrics client binds to
/// honua-server's production-monitoring endpoints (group /monitoring, admin-authorized,
/// bare JSON — NO ApiResponse envelope) and degrades honestly on failure / no-profile
/// rather than fabricating metrics (Console Patterns Charter section 11).
/// </summary>
public sealed class ConsoleMonitoringMetricsClientTests
{
    [Fact]
    public async Task ConnectionPoolMapsSuccessFromBareJsonAndAttachesAdminKey()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            var path when path.EndsWith("/monitoring/metrics/connection-pool", StringComparison.Ordinal) =>
                JsonResponse(BuildConnectionPool(), MonitoringMetricsJsonContext.Default.ConnectionPoolMetricsResponse),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var client = CreateClient(handler, adminApiKey: "admin-key");

        var result = await client.GetConnectionPoolMetricsAsync();

        Assert.Equal(OperateSectionStatus.Allowed, result.Status);
        var pool = result.Value!;
        Assert.True(pool.HasUtilizationData);
        Assert.Equal(0.42, pool.Utilization, 3);
        Assert.Equal("Healthy", pool.HealthStatus);
        Assert.True(pool.IsHealthy);
        Assert.Equal(3, pool.TotalFailures);
        Assert.NotNull(pool.QueryAdmission);
        Assert.True(pool.QueryAdmission!.AdaptiveEnabled);
        Assert.Equal(8, pool.QueryAdmission.CurrentLimit);
        Assert.Equal(5, pool.QueryAdmission.InFlight);

        // X-API-Key admin auth is attached.
        var request = Assert.Single(handler.Requests);
        Assert.True(request.Headers.TryGetValues("X-API-Key", out var values) && values.Single() == "admin-key");
    }

    [Fact]
    public async Task CacheMapsRecommendationsFromServer()
    {
        var handler = new RecordingHandler(_ =>
            JsonResponse(
                new CacheMetricsResponse
                {
                    HitRatio = 0.62,
                    HitRatioPercentage = "62.00%",
                    IsHealthy = false,
                    Recommendations = ["Consider tuning cache TTL values or cache size limits."],
                    Timestamp = DateTimeOffset.Parse("2026-06-06T10:00:00Z")
                },
                MonitoringMetricsJsonContext.Default.CacheMetricsResponse));
        var client = CreateClient(handler);

        var result = await client.GetCacheMetricsAsync();

        Assert.Equal(OperateSectionStatus.Allowed, result.Status);
        Assert.False(result.Value!.IsHealthy);
        Assert.Contains("tuning cache TTL", result.Value.Recommendations!.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DatabaseResilienceMapsNestedBlocksAndAlerts()
    {
        var handler = new RecordingHandler(_ =>
            JsonResponse(
                new DatabaseResilienceMetricsResponse
                {
                    ConnectionPool = new DatabaseConnectionPoolMetricsResponse
                    {
                        Utilization = 0.91,
                        HasUtilizationData = true,
                        HealthStatus = "Degraded",
                        IsHealthy = false
                    },
                    Resilience = new DatabaseResilienceStatusResponse
                    {
                        CircuitBreakerEnabled = true,
                        ConnectionFailures = 2,
                        QueryFailures = 7,
                        ErrorRate = 0.12,
                        ErrorRatePercentage = "12.00%"
                    },
                    Alerts = ["Critical: Database connection pool utilization is very high (91.00%)"],
                    Timestamp = DateTimeOffset.Parse("2026-06-06T10:00:00Z")
                },
                MonitoringMetricsJsonContext.Default.DatabaseResilienceMetricsResponse));
        var client = CreateClient(handler);

        var result = await client.GetDatabaseResilienceMetricsAsync();

        Assert.Equal(OperateSectionStatus.Allowed, result.Status);
        Assert.True(result.Value!.Resilience!.CircuitBreakerEnabled);
        Assert.Equal(0.12, result.Value.Resilience.ErrorRate, 3);
        Assert.Contains("utilization is very high", result.Value.Alerts!.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComprehensiveHealthMapsEntries()
    {
        var handler = new RecordingHandler(_ =>
            JsonResponse(
                new ComprehensiveHealthResponse
                {
                    Status = "Degraded",
                    TotalDuration = TimeSpan.FromMilliseconds(142),
                    Entries = new Dictionary<string, HealthCheckEntryResponse>
                    {
                        ["database"] = new() { Status = "Healthy", Duration = TimeSpan.FromMilliseconds(40), Description = "OK" },
                        ["cache"] = new() { Status = "Degraded", Duration = TimeSpan.FromMilliseconds(102), Description = "Slow" }
                    },
                    Timestamp = DateTimeOffset.Parse("2026-06-06T10:00:00Z")
                },
                MonitoringMetricsJsonContext.Default.ComprehensiveHealthResponse));
        var client = CreateClient(handler);

        var result = await client.GetComprehensiveHealthAsync();

        Assert.Equal(OperateSectionStatus.Allowed, result.Status);
        Assert.Equal("Degraded", result.Value!.Status);
        Assert.Equal(2, result.Value.Entries!.Count);
    }

    [Fact]
    public async Task ForbiddenMapsToForbiddenWithoutFabricatingData()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var client = CreateClient(handler);

        var result = await client.GetResourceMetricsAsync();

        Assert.Equal(OperateSectionStatus.Forbidden, result.Status);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task NotFoundMapsToMissing()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = CreateClient(handler);

        var result = await client.GetUploadQueueMetricsAsync();

        Assert.Equal(OperateSectionStatus.Missing, result.Status);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task UnreachableServerMapsToUnavailable()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException("connection refused"));
        var client = CreateClient(handler);

        var result = await client.GetConnectionPoolMetricsAsync();

        Assert.Equal(OperateSectionStatus.Unavailable, result.Status);
        Assert.Null(result.Value);
        Assert.Contains("unreachable", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadsReturnMissingBindingWhenNoEnvironmentIsConnected()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var profiles = new InMemoryConsoleEnvironmentProfileStore([], activeProfileId: null);
        var client = new HttpConsoleMonitoringMetricsClient(new HttpClient(handler), profiles, adminApiKey: "admin-key");

        var pool = await client.GetConnectionPoolMetricsAsync();
        var cache = await client.GetCacheMetricsAsync();
        var health = await client.GetComprehensiveHealthAsync();

        Assert.Equal(OperateSectionStatus.Unavailable, pool.Status);
        Assert.Equal(OperateSectionStatus.Unavailable, cache.Status);
        Assert.Equal(OperateSectionStatus.Unavailable, health.Status);
        Assert.Empty(handler.Requests);
    }

    private static HttpConsoleMonitoringMetricsClient CreateClient(HttpMessageHandler handler, string? adminApiKey = null)
    {
        var profile = new ConsoleEnvironmentProfile
        {
            Id = "live",
            DisplayName = "Live Server Alpha",
            ServerBaseUri = new Uri("https://server.example"),
            UpdatedAt = DateTimeOffset.Parse("2026-06-06T10:00:00Z"),
            Account = new ConsoleAccountBinding
            {
                AuthMode = ConsoleAccountAuthMode.AccountRbac,
                AccountId = "operator.live"
            }
        };
        var profiles = new InMemoryConsoleEnvironmentProfileStore([profile], activeProfileId: profile.Id);
        return new HttpConsoleMonitoringMetricsClient(new HttpClient(handler), profiles, adminApiKey);
    }

    private static ConnectionPoolMetricsResponse BuildConnectionPool() => new()
    {
        Utilization = 0.42,
        HasUtilizationData = true,
        UtilizationStatus = "available",
        UtilizationPercentage = "42.00%",
        TotalFailures = 3,
        TotalTimeouts = 1,
        HealthStatus = "Healthy",
        IsHealthy = true,
        QueryAdmission = new QueryAdmissionMetricsResponse
        {
            AdaptiveEnabled = true,
            CurrentLimit = 8,
            MinLimit = 2,
            MaxLimit = 16,
            InFlight = 5,
            AvailableSlots = 3,
            QueuedWaiters = 0,
            TargetDurationMs = 50,
            DurationEwmaMs = 47.5,
            QueueWaitEwmaMs = 1.2,
            AdjustmentCount = 4,
            LastAdjustmentDirection = "up"
        },
        Timestamp = DateTimeOffset.Parse("2026-06-06T10:00:00Z")
    };

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
}
