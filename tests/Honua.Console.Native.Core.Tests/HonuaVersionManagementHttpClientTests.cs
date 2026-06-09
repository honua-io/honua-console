using System.Net;
using System.Text;
using Honua.Console.Contracts;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Transport-level coverage for <see cref="HonuaVersionManagementHttpClient"/>, the thin HttpClient behind the
/// Honua.Console.Contracts boundary that binds the Operate branch-version manager + conflict-resolution surface
/// (honua-console#177) to honua-server's GeoServices VersionManagementServer (#371 / PR #1551). These tests
/// drive the real client over a recording <see cref="HttpMessageHandler"/> and feed it the BARE Esri-shaped
/// JSON the live VersionManagementServer emits (NOT the admin {success,data} envelope), so a drift between the
/// Console wire records and the server DTOs fails here. They assert the exact route + verb, the form-encoded
/// parameter names (versionName/accessPermission/conflictResolution/abortIfConflicts/conflicts), the
/// projection of versions/conflicts/counts, and the HTTP-status -> capability-state mapping.
/// </summary>
public sealed class HonuaVersionManagementHttpClientTests
{
    private static readonly Uri BaseUri = new("https://server.example");
    private const string ServiceId = "parcels";
    private const string VersionGuid = "11111111-2222-3333-4444-555555555555";

    [Fact]
    public async Task ListVersions_GetsVersionsRoute_AndProjectsIdentity()
    {
        const string body = """
            {
              "versions": [
                {
                  "versionGuid": "11111111-2222-3333-4444-555555555555",
                  "versionName": "alex.edit",
                  "owner": "alex",
                  "access": "private",
                  "status": "active",
                  "creationMoment": 1700000000000,
                  "modifiedMoment": 1700000100000
                }
              ]
            }
            """;
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, body));
        var client = CreateClient(handler);

        var result = await client.ListVersionsAsync(ServiceId);

        Assert.Null(result.Issue);
        var version = Assert.Single(result.Data!);
        Assert.Equal("alex.edit", version.VersionName);
        Assert.Equal("alex", version.Owner);

        var recorded = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, recorded.Method);
        Assert.Equal("/rest/services/parcels/VersionManagementServer/versions", recorded.Uri!.AbsolutePath);
    }

    [Fact]
    public async Task CreateVersion_PostsFormEncodedNameAndAccess()
    {
        const string body = """
            {
              "success": true,
              "versionInfo": {
                "versionGuid": "11111111-2222-3333-4444-555555555555",
                "versionName": "alex.edit",
                "owner": "alex",
                "access": "protected",
                "status": "active",
                "creationMoment": 1700000000000,
                "modifiedMoment": 1700000000000
              }
            }
            """;
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, body));
        var client = CreateClient(handler);

        var result = await client.CreateVersionAsync(ServiceId, "alex.edit", "alex", HonuaVersionAccess.Protected, "desc");

        Assert.Null(result.Issue);
        Assert.Equal("alex.edit", result.Data!.VersionName);

        var recorded = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, recorded.Method);
        Assert.Equal("/rest/services/parcels/VersionManagementServer/create", recorded.Uri!.AbsolutePath);
        Assert.Contains("versionName=alex.edit", recorded.Body, StringComparison.Ordinal);
        Assert.Contains("accessPermission=protected", recorded.Body, StringComparison.Ordinal);
        Assert.Contains("owner=alex", recorded.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HonuaVersionReconcilePolicy.None, "conflictResolution=none")]
    [InlineData(HonuaVersionReconcilePolicy.LastWriteWins, "conflictResolution=lastWriteWins")]
    [InlineData(HonuaVersionReconcilePolicy.VersionWins, "conflictResolution=versionWins")]
    [InlineData(HonuaVersionReconcilePolicy.DefaultWins, "conflictResolution=defaultWins")]
    public async Task Reconcile_PostsPolicyAndCounts(HonuaVersionReconcilePolicy policy, string expectedParam)
    {
        const string body = """
            {
              "success": true,
              "hasConflicts": true,
              "canPost": false,
              "autoResolvedCount": 2,
              "conflicts": [
                { "layerId": 3, "objectId": 42, "conflictType": "attribute", "fieldDiffs": [] }
              ]
            }
            """;
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, body));
        var client = CreateClient(handler);

        var result = await client.ReconcileAsync(ServiceId, VersionGuid, policy, abortIfConflicts: false);

        Assert.Null(result.Issue);
        Assert.True(result.Data!.HasConflicts);
        Assert.Equal(2, result.Data.AutoResolvedCount);
        Assert.Single(result.Data.Conflicts);

        var recorded = Assert.Single(handler.Requests);
        Assert.EndsWith($"/versions/{VersionGuid}/reconcile", recorded.Uri!.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains(expectedParam, recorded.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reconcile_AbortIfConflicts_PostsAbortFlag()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, """{ "success": true, "hasConflicts": false, "canPost": true, "autoResolvedCount": 0, "conflicts": [] }"""));
        var client = CreateClient(handler);

        await client.ReconcileAsync(ServiceId, VersionGuid, HonuaVersionReconcilePolicy.LastWriteWins, abortIfConflicts: true);

        var recorded = Assert.Single(handler.Requests);
        Assert.Contains("abortIfConflicts=true", recorded.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InspectConflicts_GetsRoute_AndProjectsThreeWayImages()
    {
        const string body = """
            {
              "success": true,
              "hasConflicts": true,
              "conflicts": [
                {
                  "layerId": 3,
                  "objectId": 42,
                  "conflictType": "attribute",
                  "baseAttributes": "{\"name\":\"A\"}",
                  "defaultAttributes": "{\"name\":\"B\"}",
                  "versionAttributes": "{\"name\":\"C\"}",
                  "baseGeometry": "POINT(0 0)",
                  "defaultGeometry": "POINT(1 1)",
                  "versionGeometry": "POINT(2 2)",
                  "fieldDiffs": [ { "name": "name", "base": "A", "default": "B", "version": "C" } ]
                }
              ]
            }
            """;
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, body));
        var client = CreateClient(handler);

        var result = await client.InspectConflictsAsync(ServiceId, VersionGuid);

        Assert.Null(result.Issue);
        var conflict = Assert.Single(result.Data!.Conflicts);
        Assert.Equal(3, conflict.LayerId);
        Assert.Equal(42, conflict.ObjectId);
        Assert.Equal("POINT(2 2)", conflict.VersionGeometry);
        var diff = Assert.Single(conflict.FieldDiffs);
        Assert.Equal("name", diff.Name);
        Assert.Equal("C", diff.Version);

        var recorded = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, recorded.Method);
        Assert.EndsWith($"/versions/{VersionGuid}/inspectConflicts", recorded.Uri!.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveConflicts_PostsConflictsJsonArray()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, """{ "success": true, "resolved": 1, "remaining": 0, "canPost": true }"""));
        var client = CreateClient(handler);

        var result = await client.ResolveConflictsAsync(
            ServiceId,
            VersionGuid,
            [new HonuaVersionConflictResolution(3, 42, HonuaVersionConflictChoice.TakeDefault)]);

        Assert.Null(result.Issue);
        Assert.Equal(1, result.Data!.Resolved);
        Assert.True(result.Data.CanPost);

        var recorded = Assert.Single(handler.Requests);
        Assert.EndsWith($"/versions/{VersionGuid}/resolveConflicts", recorded.Uri!.AbsolutePath, StringComparison.Ordinal);
        // The form value is URL-encoded; decode to assert the conflicts JSON array shape and choice mapping.
        var decoded = Uri.UnescapeDataString(recorded.Body.Replace('+', ' '));
        Assert.Contains("\"layerId\":3", decoded, StringComparison.Ordinal);
        Assert.Contains("\"objectId\":42", decoded, StringComparison.Ordinal);
        Assert.Contains("\"choice\":\"default\"", decoded, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_ProjectsBlockedByConflicts()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, """{ "success": false, "appliedChanges": 0, "serverGeneration": 0, "blockedByConflicts": true }"""));
        var client = CreateClient(handler);

        var result = await client.PostAsync(ServiceId, VersionGuid);

        Assert.Null(result.Issue);
        Assert.True(result.Data!.BlockedByConflicts);
        Assert.False(result.Data.Success);
    }

    [Fact]
    public async Task ForwardsApiKeyHeader_WhenConfigured()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, """{ "versions": [] }"""));
        var client = CreateClient(handler, apiKey: "admin-secret");

        await client.ListVersionsAsync(ServiceId);

        var recorded = Assert.Single(handler.Requests);
        Assert.Equal("admin-secret", Assert.Single(recorded.ApiKeyValues));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "Unsupported")]
    [InlineData(HttpStatusCode.NotImplemented, "Unsupported")]
    [InlineData(HttpStatusCode.Unauthorized, "Missing permission")]
    [InlineData(HttpStatusCode.Forbidden, "Missing permission")]
    [InlineData(HttpStatusCode.Conflict, "Rejected")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "Unavailable")]
    public async Task ServerError_MapsStatusToCapabilityState(HttpStatusCode status, string expectedState)
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(status));
        var client = CreateClient(handler);

        var result = await client.ListVersionsAsync(ServiceId);

        Assert.Null(result.Data);
        Assert.Equal(expectedState, result.Issue!.State);
        Assert.Equal((int)status, result.Issue.StatusCode);
    }

    [Fact]
    public async Task ServerError_SurfacesEsriErrorMessage()
    {
        const string body = """{ "error": { "code": 400, "message": "Unsupported conflictResolution policy.", "details": [] } }""";
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.BadRequest, body));
        var client = CreateClient(handler);

        var result = await client.ReconcileAsync(ServiceId, VersionGuid, HonuaVersionReconcilePolicy.None, false);

        Assert.NotNull(result.Issue);
        Assert.Equal("Unsupported conflictResolution policy.", result.Issue!.Detail);
    }

    [Fact]
    public async Task TransportFailure_SurfacesUnavailableWithoutThrowing()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException("connection refused"));
        var client = CreateClient(handler);

        var result = await client.ListVersionsAsync(ServiceId);

        Assert.Null(result.Data);
        Assert.Equal("Unavailable", result.Issue!.State);
        Assert.Contains("could not be reached", result.Issue.Detail, StringComparison.OrdinalIgnoreCase);
    }

    private static HonuaVersionManagementHttpClient CreateClient(HttpMessageHandler handler, string? apiKey = null)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = BaseUri };
        return new HonuaVersionManagementHttpClient(httpClient, new HonuaVersionManagementClientOptions(BaseUri, apiKey));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var apiKeys = request.Headers.TryGetValues("X-API-Key", out var values)
                ? values.ToArray()
                : [];

            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            Requests.Add(new RecordedRequest(request.Method, request.RequestUri, apiKeys, body));
            return responder(request);
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, Uri? Uri, IReadOnlyList<string> ApiKeyValues, string Body);
}
