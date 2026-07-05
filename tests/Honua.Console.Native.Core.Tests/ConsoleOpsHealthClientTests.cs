using System.Net;
using System.Text;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Recording-HttpClient unit tests proving the ops-health client binds to honua-server's
/// consolidated ops-health snapshot endpoint (group /api/v1/admin/observability,
/// admin-authorized, bare JSON — NO ApiResponse envelope), maps the snapshot into the
/// color-coded view, and degrades honestly on failure / no-profile rather than fabricating
/// data (Console Patterns Charter section 11).
/// </summary>
public sealed class ConsoleOpsHealthClientTests
{
    [Fact]
    public async Task SnapshotDeserializesFullPayloadAndAttachesAdminKey()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            var path when path.EndsWith("/api/v1/admin/observability/ops-health", StringComparison.Ordinal) =>
                JsonResponse(BuildSnapshot(), OpsHealthJsonContext.Default.OpsHealthSnapshotResponse),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var client = CreateClient(handler, adminApiKey: "admin-key");

        var result = await client.GetSnapshotAsync();

        Assert.Equal(OperateSectionStatus.Allowed, result.Status);
        var snapshot = result.Value!;
        Assert.Equal("Degraded", snapshot.OverallStatus);
        Assert.Equal(2, snapshot.ServingLatency!.Protocols!.Count);
        Assert.True(snapshot.Geoprocessing!.Available);
        Assert.Equal(1, snapshot.AlertDispatch!.DeadLetteredCount);
        Assert.False(snapshot.Deploy!.PlatformRelease!.IsCoVersioned);
        Assert.Single(snapshot.Deploy.PlatformRelease.SkewedIds!);
        Assert.Equal(0.42, snapshot.Database!.ConnectionPoolUtilization!.Value, 3);

        var request = Assert.Single(handler.Requests);
        Assert.True(request.Headers.TryGetValues("X-API-Key", out var values) && values.Single() == "admin-key");
    }

    [Fact]
    public async Task ForbiddenMapsToForbiddenWithoutFabricatingData()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var client = CreateClient(handler);

        var result = await client.GetSnapshotAsync();

        Assert.Equal(OperateSectionStatus.Forbidden, result.Status);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task NotImplementedMapsToUnsupported()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotImplemented));
        var client = CreateClient(handler);

        var result = await client.GetSnapshotAsync();

        Assert.Equal(OperateSectionStatus.Unsupported, result.Status);
    }

    [Fact]
    public async Task NoProfileReturnsMissingBindingWithoutCallingServer()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var profiles = new InMemoryConsoleEnvironmentProfileStore([], activeProfileId: null);
        var client = new HttpConsoleOpsHealthClient(new HttpClient(handler), profiles, adminApiKey: "admin-key");

        var result = await client.GetSnapshotAsync();

        Assert.Equal(OperateSectionStatus.Unavailable, result.Status);
        Assert.Null(result.Value);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void MapperFlagsServingLatencySloBreaches()
    {
        var view = OpsHealthDataSource.Map(BuildSnapshot());

        // GeoServices row: p95 = 1500ms (breaches p95 threshold) -> warning.
        var geo = view.ServingLatency.Rows.Single(r => r.Protocol == "GeoServices");
        Assert.Equal("warning", geo.Status.NormalizedState);

        // OGC row: healthy p95 + low error rate -> healthy.
        var ogc = view.ServingLatency.Rows.Single(r => r.Protocol == "OGC");
        Assert.Equal("healthy", ogc.Status.NormalizedState);

        Assert.True(view.ServingLatency.HasBreach);
        Assert.Equal("warning", view.Overall.NormalizedState); // Degraded overall.
        Assert.Equal("warning", view.Deploy.PlatformRelease.CoVersionStatus.NormalizedState);
        Assert.True(view.AlertDispatch.HasDeadLetters);
    }

    [Fact]
    public void MapperMarksMissingSectionsNeutralWithoutFabrication()
    {
        var view = OpsHealthDataSource.Map(new OpsHealthSnapshotResponse
        {
            GeneratedAt = DateTimeOffset.Parse("2026-06-06T10:00:00Z"),
            OverallStatus = "Healthy"
        });

        Assert.True(view.Health.Status.IsNeutral);
        Assert.Empty(view.ServingLatency.Rows);
        Assert.False(view.Geoprocessing.Available);
        Assert.False(view.Database.PoolUtilization.HasData);
    }

    private static HttpConsoleOpsHealthClient CreateClient(HttpMessageHandler handler, string? adminApiKey = null)
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
        return new HttpConsoleOpsHealthClient(new HttpClient(handler), profiles, adminApiKey);
    }

    private static OpsHealthSnapshotResponse BuildSnapshot() => new()
    {
        GeneratedAt = DateTimeOffset.Parse("2026-06-06T10:00:00Z"),
        OverallStatus = "Degraded",
        Health = new OpsHealthChecksResponse
        {
            Status = "Degraded",
            TotalDurationMs = 142.0,
            Entries =
            [
                new OpsHealthCheckEntryResponse { Name = "database", Status = "Healthy", DurationMs = 40, Description = "OK" },
                new OpsHealthCheckEntryResponse { Name = "alert-dispatch", Status = "Degraded", DurationMs = 12 }
            ]
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
                },
                new OpsServingLatencyProtocolResponse
                {
                    Protocol = "OGC",
                    RequestCount = 800,
                    ErrorCount = 0,
                    ErrorRate = 0.0,
                    P50Ms = 20,
                    P95Ms = 120,
                    P99Ms = 200,
                    MaxMs = 350
                }
            ]
        },
        Geoprocessing = new OpsGpQueueResponse
        {
            TotalActive = 3,
            Available = true,
            Buckets =
            [
                new OpsGpQueueBucketResponse { Status = "Running", Backend = "local", Count = 2 },
                new OpsGpQueueBucketResponse { Status = "Queued", Backend = "local", Count = 1 }
            ]
        },
        AlertDispatch = new OpsAlertDispatchResponse
        {
            DispatcherRunning = true,
            DispatcherEnabled = true,
            StoragePollFailing = false,
            LastPollAt = DateTimeOffset.Parse("2026-06-06T09:59:00Z"),
            PendingCount = 4,
            DeadLetteredCount = 1
        },
        Deploy = new OpsDeployReadinessResponse
        {
            Status = "blocked",
            ReadyForCoordinatedDeploy = false,
            PendingMigrationsCount = 2,
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
            ConnectionAcquisitionTimeouts = 0,
            ConnectionAcquisitionFailures = 0,
            CacheHitRatio = 0.91,
            ErrorRate = 0.004
        }
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
