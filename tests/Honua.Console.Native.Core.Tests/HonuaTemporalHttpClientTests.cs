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
    public async Task GetDiff_GetsDiffRoute_WithFromToLimit_AndReturnsDirectBody()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, DiffJson));
        var client = CreateClient(handler, apiKey: "admin-secret");

        var result = await client.GetDiffAsync("parcels", 0, from: "40", to: "42", limit: 50);

        Assert.Null(result.Issue);
        Assert.Equal(6, result.Data!.Summary.Total);
        var change = Assert.Single(result.Data.Changes);
        Assert.Equal(101, change.ObjectId);
        Assert.Equal("owner", Assert.Single(change.FieldChanges).Field);

        var recorded = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, recorded.Method);
        Assert.Equal("/api/v1/temporal/services/parcels/layers/0/diff", recorded.Uri!.AbsolutePath);
        Assert.Contains("from=40", recorded.Uri.Query, StringComparison.Ordinal);
        Assert.Contains("to=42", recorded.Uri.Query, StringComparison.Ordinal);
        Assert.Contains("limit=50", recorded.Uri.Query, StringComparison.Ordinal);
        Assert.Equal("admin-secret", Assert.Single(recorded.ApiKeyValues));
    }

    [Fact]
    public async Task GetFeatureTimeline_GetsTimelineRoute_AndReturnsDirectBody()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, TimelineJson));
        var client = CreateClient(handler);

        var result = await client.GetFeatureTimelineAsync("parcels", 0, featureId: 101, limit: null);

        Assert.Null(result.Issue);
        Assert.Equal(101, result.Data!.ObjectId);
        var revision = Assert.Single(result.Data.Revisions);
        Assert.Equal(40, revision.Generation);
        Assert.Equal("Update", revision.Operation);

        var recorded = Assert.Single(handler.Requests);
        Assert.Equal("/api/v1/temporal/services/parcels/layers/0/features/101/timeline", recorded.Uri!.AbsolutePath);
    }

    [Fact]
    public async Task PlanRollback_PostsPlanRoute_WithCheckpointBody_AndReturnsDirectBody()
    {
        string? body = null;
        var handler = new RecordingHandler(request =>
        {
            body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(HttpStatusCode.OK, PlanJson);
        });
        var client = CreateClient(handler, apiKey: "admin-secret");

        var result = await client.PlanRollbackAsync(
            "parcels", 0,
            new HonuaTemporalRollbackPlanRequest
            {
                Checkpoint = new HonuaTemporalCheckpointBody { Kind = "generation", Generation = 40 },
            });

        Assert.Null(result.Issue);
        Assert.Equal("jobRequired", result.Data!.State);
        Assert.Equal(17, result.Data.AffectedFeatureCount);
        Assert.True(result.Data.RequiresApproval);

        var recorded = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, recorded.Method);
        Assert.Equal("/api/v1/temporal/services/parcels/layers/0/rollback/plan", recorded.Uri!.AbsolutePath);
        Assert.Equal("admin-secret", Assert.Single(recorded.ApiKeyValues));
        Assert.Contains("\"generation\":40", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteRollback_PostsRollbackRoute_AcceptsAccepted202_AndReturnsJobHandle()
    {
        string? body = null;
        var handler = new RecordingHandler(request =>
        {
            body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(HttpStatusCode.Accepted, JobJson);
        });
        var client = CreateClient(handler);

        var result = await client.ExecuteRollbackAsync(
            "parcels", 0,
            new HonuaTemporalRollbackExecuteRequest
            {
                Checkpoint = new HonuaTemporalCheckpointBody { Kind = "generation", Generation = 40 },
                Approved = true,
            });

        Assert.Null(result.Issue);
        Assert.Equal("job-7", result.Data!.JobId);
        Assert.Equal("queued", result.Data.Status);

        var recorded = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, recorded.Method);
        Assert.Equal("/api/v1/temporal/services/parcels/layers/0/rollback", recorded.Uri!.AbsolutePath);
        Assert.Contains("\"approved\":true", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetDiff_Forbidden_MapsToForbidden()
    {
        var handler = new RecordingHandler(_ => ProblemDetails(HttpStatusCode.Forbidden));
        var client = CreateClient(handler);

        var result = await client.GetDiffAsync("parcels", 0, from: "40", to: "42", limit: null);

        Assert.Null(result.Data);
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
    public async Task ListReplicaConflicts_GetsConflictsRoute_WithStatusFilter_AndUnwrapsEnvelope()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, ConflictListJson));
        var client = CreateClient(handler, apiKey: "admin-secret");

        var result = await client.ListReplicaConflictsAsync("parcels", "replica-1", status: "pending");

        Assert.Null(result.Issue);
        Assert.Equal("replica-1", result.Data!.ReplicaId);
        var conflict = Assert.Single(result.Data.Conflicts);
        Assert.Equal("conflict-1", conflict.ConflictId);
        Assert.Equal("geometry", conflict.ConflictType);

        var recorded = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, recorded.Method);
        Assert.Equal("/api/v1/admin/services/parcels/replicas/replica-1/conflicts", recorded.Uri!.AbsolutePath);
        Assert.Contains("status=pending", recorded.Uri.Query, StringComparison.Ordinal);
        Assert.Equal("admin-secret", Assert.Single(recorded.ApiKeyValues));
    }

    [Fact]
    public async Task GetReplicaConflict_GetsConflictDetailRoute_AndUnwrapsBaseClientServer()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, ConflictDetailJson));
        var client = CreateClient(handler);

        var result = await client.GetReplicaConflictAsync("parcels", "replica-1", "conflict-1");

        Assert.Null(result.Issue);
        Assert.Equal("conflict-1", result.Data!.ConflictId);
        Assert.Equal(42, result.Data.ServerGeneration);
        Assert.NotNull(result.Data.BaseState);
        Assert.NotNull(result.Data.ClientState);
        Assert.NotNull(result.Data.ServerState);

        var recorded = Assert.Single(handler.Requests);
        Assert.Equal("/api/v1/admin/services/parcels/replicas/replica-1/conflicts/conflict-1", recorded.Uri!.AbsolutePath);
    }

    [Fact]
    public async Task ResolveReplicaConflict_PostsResolveRoute_WithActionBody_AndUnwrapsResponse()
    {
        string? body = null;
        var handler = new RecordingHandler(request =>
        {
            body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(HttpStatusCode.OK, ResolveResponseJson);
        });
        var client = CreateClient(handler, apiKey: "admin-secret");

        var result = await client.ResolveReplicaConflictAsync(
            "parcels", "replica-1", "conflict-1",
            new HonuaReplicaConflictResolutionRequest { Action = "acceptClient" });

        Assert.Null(result.Issue);
        Assert.True(result.Data!.CommittedNewServerState);
        Assert.Equal(43, result.Data.Conflict!.ResolvedServerGeneration);

        var recorded = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, recorded.Method);
        Assert.Equal("/api/v1/admin/services/parcels/replicas/replica-1/conflicts/conflict-1/resolve", recorded.Uri!.AbsolutePath);
        Assert.Equal("admin-secret", Assert.Single(recorded.ApiKeyValues));
        Assert.Contains("acceptClient", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveReplicaConflict_AlreadyResolved_MapsConflictToConflict()
    {
        var handler = new RecordingHandler(_ => ProblemDetails(HttpStatusCode.Conflict));
        var client = CreateClient(handler);

        var result = await client.ResolveReplicaConflictAsync(
            "parcels", "replica-1", "conflict-1",
            new HonuaReplicaConflictResolutionRequest { Action = "acceptClient" });

        Assert.Null(result.Data);
        // A 409 on the resolve POST is an already-resolved conflict, not a capability gap: it must surface
        // a recoverable Conflict state (reload-and-retry), never "Unsupported".
        Assert.Equal("Conflict", result.Issue!.State);
        Assert.Contains("already been resolved", result.Issue.Detail, StringComparison.Ordinal);
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

    private const string DiffJson =
        """
        {
          "serviceId":"parcels","layerId":0,
          "from":{"kind":"generation","value":null,"generation":40},
          "to":{"kind":"generation","value":null,"generation":42},
          "summary":{"added":2,"removed":1,"attributeChanged":3,"geometryChanged":1,"total":6},
          "changes":[
            {"objectId":101,"primaryClass":"attributeChanged","classes":["attributeChanged"],"geometryChanged":false,
             "fieldChanges":[{"field":"owner","oldValue":"Acme","newValue":"Acme Inc","masked":false}],
             "attribution":{"actor":"alice","source":"editSession","operation":"edit","sourceId":"sess-1"}}
          ],
          "nextCursor":null
        }
        """;

    private const string TimelineJson =
        """
        {
          "serviceId":"parcels","layerId":0,"objectId":101,"currentGeneration":42,
          "revisions":[
            {"generation":40,"operation":"Update","changedAt":"2026-05-28T09:00:00.000Z",
             "attribution":{"actor":"bob","source":"job","operation":null,"sourceId":"job-3"}}
          ],
          "nextCursor":null
        }
        """;

    private const string PlanJson =
        """
        {
          "serviceId":"parcels","layerId":0,
          "targetCheckpoint":{"kind":"generation","value":null,"generation":40},
          "currentGeneration":42,"state":"jobRequired","affectedFeatureCount":17,
          "validationFindings":[{"code":"FK_RISK","severity":"warning","message":"Foreign keys may break."}],
          "compatibilityFindings":[],
          "requiresApproval":true
        }
        """;

    private const string JobJson =
        """
        {
          "jobId":"job-7","serviceId":"parcels","layerId":0,
          "targetCheckpoint":{"kind":"generation","value":null,"generation":40},
          "status":"queued"
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

    private const string ConflictListJson =
        """
        {
          "success":true,
          "data":{
            "serviceId":"parcels","replicaId":"replica-1","statusFilter":"pending",
            "conflicts":[
              {"conflictId":"conflict-1","replicaId":"replica-1","serviceId":"parcels","layerId":0,
               "objectId":101,"conflictType":"geometry","status":"pending","serverGeneration":42,
               "detectedAt":"2026-05-28T09:00:00Z"}
            ]
          },
          "message":null
        }
        """;

    private const string ConflictDetailJson =
        """
        {
          "success":true,
          "data":{
            "conflictId":"conflict-1","replicaId":"replica-1","serviceId":"parcels","layerId":0,
            "objectId":101,"conflictType":"attribute","status":"pending","serverGeneration":42,
            "baseState":{"attributes":{"owner":"Acme"}},
            "clientState":{"attributes":{"owner":"Acme LLC"}},
            "serverState":{"attributes":{"owner":"Acme Inc"}},
            "detectedAt":"2026-05-28T09:00:00Z"
          },
          "message":null
        }
        """;

    private const string ResolveResponseJson =
        """
        {
          "success":true,
          "data":{
            "conflict":{"conflictId":"conflict-1","replicaId":"replica-1","serviceId":"parcels","layerId":0,
              "objectId":101,"conflictType":"attribute","status":"resolved","serverGeneration":42,
              "detectedAt":"2026-05-28T09:00:00Z","resolutionAction":"acceptClient","resolvedServerGeneration":43},
            "committedNewServerState":true
          },
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
