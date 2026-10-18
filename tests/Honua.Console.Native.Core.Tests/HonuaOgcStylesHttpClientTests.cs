using System.Net;
using System.Text;
using Honua.Console.Contracts;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Transport-level coverage for <see cref="HonuaOgcStylesHttpClient"/>, the thin HttpClient behind the single
/// Honua.Console.Contracts boundary that binds the Studio map builder's per-layer style picker (#161,
/// honua-server ADR-0048) to the server's OGC API - Styles list (<c>GET /ogc/styles</c>). These tests drive
/// the real client over a recording <see cref="HttpMessageHandler"/> and feed it byte-for-byte the JSON the
/// live OGC surface emits — a PUBLIC OGC StylesList document (<c>{styles:[{id,title,links}],default}</c>) that
/// is NOT wrapped in the admin <c>{success,data}</c> envelope — so a drift between the Console wire records and
/// the server's OGC document fails here. They assert the exact route + verb, the styleId/title/default
/// projection, the legacy/empty handling, and the HTTP-status -> capability-state mapping the picker degrades
/// through.
/// </summary>
public sealed class HonuaOgcStylesHttpClientTests
{
    private static readonly Uri BaseUri = new("https://server.example");

    [Fact]
    public async Task ListStyles_GetsOgcStylesRoute_AndProjectsIdsTitlesAndDefault()
    {
        const string body = """
            {
              "styles": [
                { "id": "topographic", "title": "Topographic", "links": [ { "href": "https://server.example/ogc/styles/topographic", "rel": "stylesheet" } ] },
                { "id": "night", "title": "Night mode", "links": [] }
              ],
              "default": "topographic",
              "links": [ { "href": "https://server.example/ogc/styles", "rel": "self" } ]
            }
            """;
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, body));
        var client = CreateClient(handler);

        var result = await client.ListStylesAsync();

        Assert.Null(result.Issue);
        Assert.NotNull(result.Data);
        Assert.Equal("topographic", result.Data!.Default);
        Assert.Collection(
            result.Data.Styles,
            first =>
            {
                Assert.Equal("topographic", first.Id);
                Assert.Equal("Topographic", first.Title);
            },
            second =>
            {
                Assert.Equal("night", second.Id);
                Assert.Equal("Night mode", second.Title);
            });

        var recorded = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, recorded.Method);
        Assert.Equal("/ogc/styles", recorded.Uri!.AbsolutePath);
    }

    [Fact]
    public async Task ListStyles_TitleOptional_ProjectsNullTitle()
    {
        const string body = """{ "styles": [ { "id": "bare", "links": [] } ] }""";
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, body));
        var client = CreateClient(handler);

        var result = await client.ListStylesAsync();

        var style = Assert.Single(result.Data!.Styles);
        Assert.Equal("bare", style.Id);
        Assert.Null(style.Title);
        Assert.Null(result.Data.Default);
    }

    [Fact]
    public async Task ListStyles_DropsEntriesWithoutAStableId()
    {
        // An id is required by the contract; an entry missing it is unusable as a picker reference and is
        // filtered rather than surfaced as a blank option.
        const string body = """{ "styles": [ { "id": "", "links": [] }, { "id": "good", "links": [] } ] }""";
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, body));
        var client = CreateClient(handler);

        var result = await client.ListStylesAsync();

        var style = Assert.Single(result.Data!.Styles);
        Assert.Equal("good", style.Id);
    }

    [Fact]
    public async Task ListStyles_EmptyList_IsDataNotIssue()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, """{ "styles": [] }"""));
        var client = CreateClient(handler);

        var result = await client.ListStylesAsync();

        Assert.Null(result.Issue);
        Assert.Empty(result.Data!.Styles);
    }

    [Fact]
    public async Task ListStyles_ForwardsApiKeyHeader_WhenConfigured()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, """{ "styles": [] }"""));
        var client = CreateClient(handler, apiKey: "admin-secret");

        await client.ListStylesAsync();

        var recorded = Assert.Single(handler.Requests);
        Assert.Equal("admin-secret", Assert.Single(recorded.ApiKeyValues));
    }

    [Fact]
    public async Task ListStyles_OmitsApiKeyHeader_WhenNoKeyConfigured()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, """{ "styles": [] }"""));
        var client = CreateClient(handler, apiKey: null);

        await client.ListStylesAsync();

        var recorded = Assert.Single(handler.Requests);
        Assert.Empty(recorded.ApiKeyValues);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "Unsupported")]
    [InlineData(HttpStatusCode.NotImplemented, "Unsupported")]
    [InlineData(HttpStatusCode.Unauthorized, "Missing permission")]
    [InlineData(HttpStatusCode.Forbidden, "Missing permission")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "Unavailable")]
    public async Task ListStyles_ServerError_MapsStatusToCapabilityState(HttpStatusCode status, string expectedState)
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(status));
        var client = CreateClient(handler);

        var result = await client.ListStylesAsync();

        Assert.Null(result.Data);
        Assert.NotNull(result.Issue);
        Assert.Equal(expectedState, result.Issue!.State);
        Assert.Equal((int)status, result.Issue.StatusCode);
        Assert.Equal("GET /ogc/styles", result.Issue.Contract);
    }

    [Fact]
    public async Task ListStyles_TransportFailure_SurfacesUnavailableWithoutThrowing()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException("connection refused"));
        var client = CreateClient(handler);

        var result = await client.ListStylesAsync();

        Assert.Null(result.Data);
        Assert.Equal("Unavailable", result.Issue!.State);
        Assert.Contains("could not be reached", result.Issue.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListStyles_MalformedBody_SurfacesUnsupportedContractMismatch()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{ this is not valid json", Encoding.UTF8, "application/json")
        });
        var client = CreateClient(handler);

        var result = await client.ListStylesAsync();

        Assert.Null(result.Data);
        Assert.Equal("Unsupported", result.Issue!.State);
    }

    [Fact]
    public async Task ConditionalSaveUsesVersionFromSameReadAndPreservesStrictValidation()
    {
        var handler = new RecordingHandler(request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                var response = Json(HttpStatusCode.OK, "{\"version\":8}");
                response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"style-v7\"");
                return response;
            }
            Assert.Equal("\"style-v7\"", Assert.Single(request.Headers.IfMatch).ToString());
            Assert.Contains("handling=strict", request.Headers.GetValues("Prefer"));
            Assert.Equal(new string('a', 64), Assert.Single(request.Headers.GetValues("X-Honua-Style-Impact")));
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var client = CreateClient(handler);
        var read = await client.GetStylesheetAsync("shared", HonuaOgcStyleEncoding.MapLibre);
        Assert.Equal("{\"version\":8}", read.Data!.Content);
        var saved = await client.UpdateStylesheetConditionallyAsync("shared", HonuaOgcStyleEncoding.MapLibre,
            "{\"version\":8}", true, read.Data.EntityTag!, new string('a', 64));
        Assert.True(saved.Succeeded);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task StaleConditionalSaveReportsConflictWithoutRetry()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.PreconditionFailed, "{}"));
        var result = await CreateClient(handler).UpdateStylesheetConditionallyAsync("shared", HonuaOgcStyleEncoding.MapLibre,
            "{}", false, "\"style-v7\"", new string('a', 64));
        Assert.False(result.Succeeded);
        Assert.Equal("Conflict", result.State);
        Assert.Contains("edits are retained", result.Detail, StringComparison.Ordinal);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("W/\"style-v7\"")]
    [InlineData("\"style-v7\", \"style-v8\"")]
    public async Task ConditionalSaveNeverSendsMissingWeakOrMultipleVersion(string version)
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var result = await CreateClient(handler).UpdateStylesheetConditionallyAsync("shared", HonuaOgcStyleEncoding.MapLibre,
            "{}", false, version, new string('a', 64));
        Assert.False(result.Succeeded);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ImpactReadRequiresVersionAndRetainsOnlyReturnedAssociations()
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.Equal("/ogc/styles/shared/impact", request.RequestUri!.AbsolutePath);
            var response = Json(HttpStatusCode.OK, $$"""
                {"styleId":"shared","styleVersion":7,"etag":"\"style-v7\"",
                 "contentHash":"{{new string('a', 64)}}","impactHash":"{{new string('b', 64)}}",
                 "canEdit":false,"associations":[{"resourceId":"visible","name":"Visible resource"}]}
                """);
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"style-v7\"");
            return response;
        });
        var result = await CreateClient(handler).GetSharedStyleImpactAsync("shared");
        Assert.Null(result.Issue);
        Assert.False(result.Data!.CanEdit);
        Assert.False(result.Data.ImpactComplete);
        Assert.Empty(result.Data.Consumers);
        Assert.Equal("visible", Assert.Single(result.Data.Associations).ResourceId);
    }

    [Fact]
    public async Task BoundWriteRequiresExplicitHostTransportSupport()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("Must not send."));
        var result = await CreateClient(handler).UpdateStylesheetBoundAsync("shared", HonuaOgcStyleEncoding.MapLibre, "{}", true, "\"style-v7\"", new string('a', 64), "operator-binding");
        Assert.False(result.Succeeded);
        Assert.Equal("Unsupported", result.State);
    }

    [Fact]
    public async Task BoundStyleReadCapturesHostOnlyAuthorityAndWriteCarriesItAsOptions()
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.False(request.Headers.Contains("X-Honua-Session-Partition"));
            if (request.Method == HttpMethod.Get)
            {
                Assert.True(request.Options.TryGetValue(ConsoleAuthorityRequestOptions.Capture, out var capture) && capture);
                request.Options.Set(ConsoleAuthorityRequestOptions.Resolved, "operator-binding");
                var response = Json(HttpStatusCode.OK, "{\"version\":8}");
                response.Headers.ETag = new("\"style-v7\""); return response;
            }
            Assert.True(request.Options.TryGetValue(ConsoleAuthorityRequestOptions.Expected, out var expected));
            Assert.Equal("operator-binding", expected);
            return new HttpResponseMessage(HttpStatusCode.PreconditionFailed);
        });
        using var client = new HonuaOgcStylesHttpClient(new HttpClient(handler), new HonuaOgcStylesClientOptions(BaseUri, AuthorityBindingSupported: true));
        var read = await client.GetStylesheetAsync("shared", HonuaOgcStyleEncoding.MapLibre);
        Assert.Equal("operator-binding", read.Data?.AuthorityBinding);
        var saved = await client.UpdateStylesheetBoundAsync("shared", HonuaOgcStyleEncoding.MapLibre, "{}", true, "\"style-v7\"", new string('a', 64), read.Data!.AuthorityBinding!);
        Assert.False(saved.Succeeded); Assert.Equal("Conflict", saved.State);
    }

    private static HonuaOgcStylesHttpClient CreateClient(HttpMessageHandler handler, string? apiKey = null)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = BaseUri };
        return new HonuaOgcStylesHttpClient(httpClient, new HonuaOgcStylesClientOptions(BaseUri, apiKey));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

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
