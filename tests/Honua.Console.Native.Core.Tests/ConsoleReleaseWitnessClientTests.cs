using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

public sealed class ConsoleReleaseWitnessClientTests
{
    [Fact]
    public async Task ObserveBindsExactRuntimePublicationAndStructuredAuditOperation()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/admin/version" => Json("{\"data\":{\"sourceRevision\":\"" + new string('a', 40) + "\"}}"),
            "/api/v1/studio/content-items" => Json("""
                {"items":[{"itemId":"item-1","publishedVersionId":"version-1","publication":{"publicationId":"pub-1"}}]}
                """),
            "/api/v1/console/publications/pub-1" => Json("""
                {"route":{"publicationId":"pub-1","activeVersionId":"active-1","routePath":"/share/map-1"},
                 "versions":[{"versionId":"active-1","sourceContentId":"item-1","contentVersionId":"version-1","contentHash":"sha256-content"}]}
                """),
            "/api/v1/admin/observability/audit" => Json("""
                {"items":[{"auditId":7,"resourceType":"operation_proposal","resourceId":"proposal-1",
                 "action":"operation.applied","outcome":"Success","correlationId":"corr-1",
                 "details":"{\"executionOperationId\":\"operation-1\"}"}]}
                """),
            "/api/v1/admin/observability/audit/verify" => Json("""{"verified":true}"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        var client = CreateClient(handler);

        var result = await client.ObserveAsync(new("map", "item-1", "version-1", "sha256-content", "proposal-1"));

        Assert.True(result.IsAllowed, result.Message + " " + result.Detail);
        Assert.Equal(new string('a', 40), result.Value!.ServerSourceRevision);
        Assert.Equal("pub-1", result.Value.PublicationId);
        Assert.Equal("https://server.example/share/map-1", result.Value.PublicUrl);
        Assert.Equal("corr-1", result.Value.AuditCorrelationId);
        Assert.Equal("operation-1", result.Value.AuditExecutionOperationId);
        Assert.All(handler.Requests, request =>
            Assert.Equal("operator-test-bearer", request.Headers.Authorization?.Parameter));
    }

    [Fact]
    public async Task ObserveRefusesAuditWithoutIndependentStructuredOperationIdentity()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/admin/version" => Json("{\"data\":{\"sourceRevision\":\"" + new string('a', 40) + "\"}}"),
            "/api/v1/studio/content-items" => Json("""
                {"items":[{"itemId":"item-1","publishedVersionId":"version-1","publication":{"publicationId":"pub-1"}}]}
                """),
            "/api/v1/console/publications/pub-1" => Json("""
                {"route":{"publicationId":"pub-1","activeVersionId":"active-1","routePath":"/share/map-1"},
                 "versions":[{"versionId":"active-1","sourceContentId":"item-1","contentVersionId":"version-1","contentHash":"sha256-content"}]}
                """),
            "/api/v1/admin/observability/audit" => Json("""
                {"items":[{"resourceId":"proposal-1","action":"operation.applied","outcome":"Success",
                 "correlationId":"corr-1","details":"{}"}]}
                """),
            _ => throw new InvalidOperationException(request.RequestUri.AbsolutePath),
        });
        var client = CreateClient(handler);

        var result = await client.ObserveAsync(new("map", "item-1", "version-1", "sha256-content", "proposal-1"));

        Assert.False(result.IsAllowed);
        Assert.Contains("omits executionOperationId", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ObservePagesStudioItemsUntilTheBoundItemIsFound()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/admin/version" => Json("{\"data\":{\"sourceRevision\":\"" + new string('a', 40) + "\"}}"),
            "/api/v1/studio/content-items" when !request.RequestUri.Query.Contains("cursor=", StringComparison.Ordinal) => Json("""
                {"items":[{"itemId":"item-other"}],"nextCursor":"page-2"}
                """),
            "/api/v1/studio/content-items" when request.RequestUri.Query.Contains("cursor=page-2", StringComparison.Ordinal) => Json("""
                {"items":[{"itemId":"item-1","publishedVersionId":"version-1","publication":{"publicationId":"pub-1"}}]}
                """),
            "/api/v1/console/publications/pub-1" => Json("""
                {"route":{"publicationId":"pub-1","activeVersionId":"active-1","routePath":"/share/map-1"},
                 "versions":[{"versionId":"active-1","sourceContentId":"item-1","contentVersionId":"version-1","contentHash":"sha256-content"}]}
                """),
            "/api/v1/admin/observability/audit" => Json("""
                {"items":[{"auditId":7,"resourceType":"operation_proposal","resourceId":"proposal-1",
                 "action":"operation.applied","outcome":"Success","correlationId":"corr-1",
                 "details":"{\"executionOperationId\":\"operation-1\"}"}]}
                """),
            "/api/v1/admin/observability/audit/verify" => Json("""{"verified":true}"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        var client = CreateClient(handler);

        var result = await client.ObserveAsync(new("map", "item-1", "version-1", "sha256-content", "proposal-1"));

        Assert.True(result.IsAllowed, result.Message + " " + result.Detail);
        Assert.Equal(2, handler.Requests.Count(request => request.RequestUri!.AbsolutePath == "/api/v1/studio/content-items"));
    }

    private static HttpConsoleReleaseWitnessClient CreateClient(HttpMessageHandler handler)
    {
        var profile = new ConsoleEnvironmentProfile
        {
            Id = "live",
            DisplayName = "Candidate",
            ServerBaseUri = new Uri("https://server.example"),
            UpdatedAt = DateTimeOffset.Parse("2026-08-21T00:00:00Z"),
            Account = new ConsoleAccountBinding { AuthMode = ConsoleAccountAuthMode.AccountRbac, AccountId = "operator.live" },
        };
        var profiles = new InMemoryConsoleEnvironmentProfileStore([profile], activeProfileId: profile.Id);
        var sessions = new InMemoryConsoleAccountSessionStore();
        sessions.SaveSessionAsync(new ConsoleAccountSession { ProfileId = profile.Id, AccessToken = "operator-test-bearer" })
            .GetAwaiter().GetResult();
        return new HttpConsoleReleaseWitnessClient(new HttpClient(handler), profiles, sessions);
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri)
            {
                Headers = { Authorization = request.Headers.Authorization is { } auth
                    ? new AuthenticationHeaderValue(auth.Scheme, auth.Parameter)
                    : null },
            };
            Requests.Add(clone);
            return Task.FromResult(responder(request));
        }
    }
}
