using System.Net;
using System.Text;
using Honua.Console.Contracts;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Transport-level coverage for <see cref="HonuaTemporalHttpClient"/>, the thin HttpClient behind the single
/// Honua.Console.Contracts boundary that binds the temporal viewer (honua-console#23) to the merged
/// honua-server temporal data history API (honua-server#1166 slice 1: capabilities + as-of) and the
/// disconnected replica management API (honua-server#1167 slice 1: replica list + detail). These tests drive
/// the real client over a recording <see cref="HttpMessageHandler"/> and feed it byte-for-byte the JSON the
/// live server emits (camelCase bodies; capability/as-of return the DTO directly, replicas wrap in the shared
/// ApiResponse envelope), so a drift between the Console wire records and the server responses fails here
/// rather than only in the opt-in Docker test. They assert the exact route + verb + admin-key header and the
/// HTTP-status -> issue mapping.
/// </summary>
public sealed class HonuaTemporalHttpClientTests
{
    private static readonly Uri BaseUri = new("https://server.example");

    [Fact]
    public async Task GetCapability_GetsCapabilitiesRoute_WithAdminKeyAndMapsDeferred()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, CapabilityJson));
        var client = CreateClient(handler, apiKey: "admin-secret");

        var result = await client.GetCapabilityAsync("parcels", 0);

        Assert.Null(result.Issue);
        Assert.NotNull(result.Data);
        Assert.True(result.Data!.SupportsHistory);
        Assert.True(result.Data.SupportsAsOf);
        Assert.Equal(42, result.Data.CurrentGeneration);
        // Deferred capabilities (#1285) are reported as false in slice 1.
        Assert.False(result.Data.Deferred.SupportsDiff);
        Assert.False(result.Data.Deferred.SupportsRollback);

        var recorded = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, recorded.Method);
        Assert.Equal("/api/v1/temporal/services/parcels/layers/0/capabilities", recorded.Uri!.AbsolutePath);
        Assert.Equal("admin-secret", Assert.Single(recorded.ApiKeyValues));
    }

    [Fact]
    public async Task ReadAsOf_PassesGenerationAndLimit_OnAsOfRoute()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, AsOfJson));
        var client = CreateClient(handler);

        var result = await client.ReadAsOfAsync("parcels", 0, generation: 40, timestamp: null, limit: 25);

        Assert.Null(result.Issue);
        Assert.Equal(42, result.Data!.CurrentGeneration);
        Assert.Equal(40, result.Data.ResolvedGeneration);
        var feature = Assert.Single(result.Data.Features);
        Assert.Equal(101, feature.ObjectId);

        var recorded = Assert.Single(handler.Requests);
        Assert.Equal("/api/v1/temporal/services/parcels/layers/0/as-of", recorded.Uri!.AbsolutePath);
        Assert.Contains("generation=40", recorded.Uri.Query, StringComparison.Ordinal);
        Assert.Contains("limit=25", recorded.Uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetCapability_LayerNotSupported_MapsConflictToUnsupported()
    {
        var handler = new RecordingHandler(_ => ProblemDetails(HttpStatusCode.Conflict));
        var client = CreateClient(handler);

        var result = await client.GetCapabilityAsync("parcels", 0);

        Assert.Null(result.Data);
        Assert.NotNull(result.Issue);
        Assert.Equal("Unsupported", result.Issue!.State);
    }

    [Fact]
    public async Task GetCapability_Forbidden_MapsToForbidden()
    {
        var handler = new RecordingHandler(_ => ProblemDetails(HttpStatusCode.Forbidden));
        var client = CreateClient(handler);

        var result = await client.GetCapabilityAsync("parcels", 0);

        Assert.Equal("Forbidden", result.Issue!.State);
    }

    [Fact]
    public async Task ListReplicas_GetsReplicasRoute_AndUnwrapsApiResponseEnvelope()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, ReplicaListJson));
        var client = CreateClient(handler, apiKey: "admin-secret");

        var result = await client.ListReplicasAsync("parcels");

        Assert.Null(result.Issue);
        Assert.NotNull(result.Data);
        Assert.Equal("parcels", result.Data!.ServiceId);
        var replica = Assert.Single(result.Data.Replicas);
        Assert.Equal("replica-1", replica.ReplicaId);
        Assert.Equal("Field Crew 7", replica.ReplicaName);

        var recorded = Assert.Single(handler.Requests);
        Assert.Equal("/api/v1/admin/services/parcels/replicas/", recorded.Uri!.AbsolutePath);
        Assert.Equal("admin-secret", Assert.Single(recorded.ApiKeyValues));
    }

    [Fact]
    public async Task GetReplica_GetsReplicaDetailRoute_AndUnwrapsEnvelope()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, ReplicaDetailJson));
        var client = CreateClient(handler);

        var result = await client.GetReplicaAsync("parcels", "replica-1");

        Assert.Null(result.Issue);
        Assert.Equal("replica-1", result.Data!.ReplicaId);
        Assert.Equal(42, result.Data.LastSyncGeneration);

        var recorded = Assert.Single(handler.Requests);
        Assert.Equal("/api/v1/admin/services/parcels/replicas/replica-1", recorded.Uri!.AbsolutePath);
    }

    [Fact]
    public async Task ListReplicas_TransportFailure_MapsToUnavailable()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException("connection refused"));
        var client = CreateClient(handler);

        var result = await client.ListReplicasAsync("parcels");

        Assert.Equal("Unavailable", result.Issue!.State);
    }

    private static HonuaTemporalHttpClient CreateClient(HttpMessageHandler handler, string? apiKey = null)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = BaseUri };
        return new HonuaTemporalHttpClient(httpClient, new HonuaTemporalClientOptions(BaseUri, apiKey));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage ProblemDetails(HttpStatusCode status) =>
        new(status)
        {
            Content = new StringContent(
                $$"""{"type":"about:blank","title":"error","status":{{(int)status}},"detail":"server says no"}""",
                Encoding.UTF8,
                "application/problem+json")
        };

    private const string CapabilityJson =
        """
        {
          "serviceId":"parcels","layerId":0,"layerName":"Parcels","supportsHistory":true,"supportsAsOf":true,
          "temporalColumn":"valid_time","cursorKind":"Generation","currentGeneration":42,
          "deferred":{"supportsDiff":false,"supportsTimeline":false,"supportsAttribution":false,"supportsRollback":false}
        }
        """;

    private const string AsOfJson =
        """
        {
          "serviceId":"parcels","layerId":0,"requestedCursorKind":"Generation","resolvedGeneration":40,
          "currentGeneration":42,
          "features":[{"objectId":101,"operation":"Update","changedAt":"2026-05-28T09:00:00.000Z","attributes":{"owner":"Acme"}}]
        }
        """;

    private const string ReplicaListJson =
        """
        {
          "success":true,
          "data":{
            "serviceId":"parcels",
            "replicas":[
              {"replicaId":"replica-1","replicaName":"Field Crew 7","serviceId":"parcels","syncModel":"perReplica",
               "layerIds":[0],"createdAt":"2026-05-20T08:00:00Z","lastSyncTime":"2026-05-28T09:00:00Z"}
            ]
          },
          "message":null
        }
        """;

    private const string ReplicaDetailJson =
        """
        {
          "success":true,
          "data":{"replicaId":"replica-1","replicaName":"Field Crew 7","serviceId":"parcels","syncModel":"perReplica",
                  "layerIds":[0],"createdAt":"2026-05-20T08:00:00Z","lastSyncTime":"2026-05-28T09:00:00Z","lastSyncGeneration":42},
          "message":null
        }
        """;

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var apiKeys = request.Headers.TryGetValues("X-API-Key", out var values)
                ? values.ToArray()
                : [];

            Requests.Add(new RecordedRequest(request.Method, request.RequestUri, apiKeys));
            return Task.FromResult(responder(request));
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, Uri? Uri, IReadOnlyList<string> ApiKeyValues);
}
