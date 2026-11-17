using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Security;
using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Tests;

// Exports reads may use the service credential; creating, unpublishing and connecting buckets need the operator.
public sealed class ConsoleExportsClientTests
{
    private const string ListBody = """
        {"success":true,"data":{"items":[{"id":"parcels","title":"Parcels","status":"ready","currentVersion":"v2",
        "source":{"kind":"layer","serviceId":"county","layerId":3,"name":"Parcels"},
        "collectionUrl":"https://server.example/api/v1/exports/parcels/collection.json",
        "notPublicReason":"The Layer has a row filter."}]}}
        """;

    [Fact]
    public async Task ListReadsEnvelopeWithServiceCredential()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, ListBody));
        var client = CreateClient(handler, adminApiKey: "read-key");

        var result = await client.ListAsync();

        Assert.True(result.IsAllowed);
        var export = Assert.Single(result.Value!.Items);
        Assert.Equal("parcels", export.Id);
        Assert.Equal(3, export.Source.LayerId);
        Assert.Equal("The Layer has a row filter.", export.NotPublicReason);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/api/v1/admin/exports", request.RequestUri!.AbsolutePath);
        Assert.Equal("read-key", request.Headers.GetValues("X-API-Key").Single());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.NotImplemented)]
    public async Task ListOnServerWithoutExportsIsUnsupported(HttpStatusCode status)
    {
        var client = CreateClient(new RecordingHandler(_ => new HttpResponseMessage(status)));

        var result = await client.ListAsync();

        Assert.Equal(OperateSectionStatus.Unsupported, result.Status);
        Assert.Contains("does not offer Exports", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMissingExportIsMissingNotUnsupported()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = CreateClient(handler);

        var result = await client.GetAsync("old export");

        Assert.Equal(OperateSectionStatus.Missing, result.Status);
        Assert.Equal("/api/v1/admin/exports/old%20export", Assert.Single(handler.Requests).RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task SuccessFalseEnvelopeIsNotTreatedAsData()
    {
        var client = CreateClient(new RecordingHandler(_ => Json(HttpStatusCode.OK, """{"success":false,"message":"Export store offline"}""")));

        var result = await client.ListAsync();

        Assert.Equal(OperateSectionStatus.Unavailable, result.Status);
        Assert.Equal("Export store offline", result.Message);
    }

    [Fact]
    public async Task CreateSendsLayerSourceWithOperatorBearerOnly()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.Accepted,
            """{"success":true,"data":{"id":"parcels","status":"running","jobId":"job-7"},"message":"Export started"}"""));
        var client = CreateClient(handler, adminApiKey: "shared-admin-key", sessions: BearerSessions());

        var result = await client.CreateAsync(new ExportCreateRequest
        {
            Source = new ExportSourceDescriptor { Kind = "layer", ServiceId = "county", LayerId = 3 },
            ExportId = "parcels"
        });

        Assert.True(result.IsAllowed);
        Assert.Equal("job-7", result.Value!.JobId);
        Assert.Equal("Export started", result.Message);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "operator-test-bearer"), request.Headers.Authorization);
        Assert.False(request.Headers.Contains("X-API-Key"));
        var body = Assert.Single(handler.Bodies);
        Assert.Contains("\"kind\":\"layer\"", body, StringComparison.Ordinal);
        Assert.Contains("\"layerId\":3", body, StringComparison.Ordinal);
        Assert.Contains("\"exportId\":\"parcels\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateWithoutOperatorSessionFailsClosedWithoutSending()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("Request must not be sent."));
        var client = CreateClient(handler, adminApiKey: "shared-admin-key");

        var result = await client.CreateAsync(new ExportCreateRequest { Source = new ExportSourceDescriptor { Kind = "layer" } });

        Assert.Equal(OperateSectionStatus.Forbidden, result.Status);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task RefusedCreateShowsTheServerReason()
    {
        var client = CreateClient(
            new RecordingHandler(_ => Json(HttpStatusCode.BadRequest, """{"success":false,"message":"Export id 'parcels' is already used."}""")),
            sessions: BearerSessions());

        var result = await client.CreateAsync(new ExportCreateRequest { Source = new ExportSourceDescriptor { Kind = "layer" } });

        Assert.Equal(OperateSectionStatus.Rejected, result.Status);
        Assert.Equal("Export id 'parcels' is already used.", result.Message);
    }

    [Fact]
    public async Task UnpublishDeletesTheExport()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, """{"success":true,"data":{"id":"parcels","status":"unpublished"}}"""));
        var client = CreateClient(handler, sessions: BearerSessions());

        var result = await client.UnpublishAsync("parcels");

        Assert.True(result.IsAllowed);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal("/api/v1/admin/exports/parcels", request.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task ObjectStorageSetupBrowseAndCreateUseConnectionRoutes()
    {
        var connectionId = Guid.Parse("6f1b2c9e-0000-4000-8000-000000000001");
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/admin/connections/object-storage/setup" => Json(HttpStatusCode.OK,
                """{"success":true,"data":{"externalId":"ext-123","quickCreateUrl":"https://console.example/quick"}}"""),
            "/api/v1/admin/connections" => Json(HttpStatusCode.Created,
                $$"""{"success":true,"data":{"id":"{{connectionId}}","name":"imagery"}}"""),
            _ => Json(HttpStatusCode.OK,
                """{"success":true,"data":{"bucket":"imagery","prefix":"ortho/","folders":["ortho/2026/"],"objects":[{"key":"ortho/a.tif","kind":"cog","bytes":2048}]}}""")
        });
        var client = CreateClient(handler, sessions: BearerSessions());

        var setup = await client.GetObjectStorageSetupAsync("imagery", "ortho/");
        var created = await client.CreateObjectStorageAsync(new ObjectStorageConnectionRequest
        {
            Name = "imagery", Bucket = "imagery", Prefix = "ortho/", Region = "us-west-2",
            RoleArn = "arn:aws:iam::123456789012:role/honua-connection-imagery", ExternalId = "ext-123"
        });
        var listing = await client.BrowseAsync(connectionId, "ortho/");

        Assert.Equal("ext-123", setup.Value!.ExternalId);
        Assert.Equal(connectionId, created.Value!.Id);
        Assert.Equal("cog", Assert.Single(listing.Value!.Objects).Kind);
        Assert.Equal("?bucket=imagery&prefix=ortho%2F", handler.Requests[0].RequestUri!.Query);
        Assert.Contains("\"kind\":\"object-storage\"", handler.Bodies.Single(), StringComparison.Ordinal);
        Assert.Contains("\"externalId\":\"ext-123\"", handler.Bodies.Single(), StringComparison.Ordinal);
        Assert.Equal($"/api/v1/admin/connections/{connectionId:D}/objects", handler.Requests[2].RequestUri!.AbsolutePath);
        Assert.Equal("?prefix=ortho%2F", handler.Requests[2].RequestUri!.Query);
    }

    [Fact]
    public async Task UnreachableServerIsUnavailable()
    {
        var client = CreateClient(new RecordingHandler(_ => throw new HttpRequestException("refused")));

        var result = await client.BrowseAsync(Guid.NewGuid(), string.Empty);

        Assert.Equal(OperateSectionStatus.Unavailable, result.Status);
    }

    private static HttpConsoleExportsClient CreateClient(
        HttpMessageHandler handler,
        string? adminApiKey = null,
        IConsoleAccountSessionStore? sessions = null)
    {
        var profile = new ConsoleEnvironmentProfile
        {
            Id = "live",
            DisplayName = "Live",
            ServerBaseUri = new Uri("https://server.example"),
            Account = new ConsoleAccountBinding { AuthMode = ConsoleAccountAuthMode.AccountRbac, AccountId = "operator.live" }
        };
        var sessionStore = sessions ?? new InMemoryConsoleAccountSessionStore();
        return new HttpConsoleExportsClient(
            new HttpClient(handler),
            new InMemoryConsoleEnvironmentProfileStore([profile], activeProfileId: profile.Id),
            sessionStore,
            new ConsoleOperatorBearerProvider(sessionStore, new UnavailableConsoleOperatorBearerExchange(), TimeProvider.System),
            adminApiKey);
    }

    private static IConsoleAccountSessionStore BearerSessions()
    {
        var sessions = new InMemoryConsoleAccountSessionStore();
        sessions.SaveSessionAsync(new ConsoleAccountSession { ProfileId = "live", AccessToken = "operator-test-bearer" })
            .GetAwaiter().GetResult();
        return sessions;
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            Requests.Add(clone);
            if (request.Content is not null)
            {
                Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            return responder(request);
        }
    }
}
