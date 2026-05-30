using System.Net;
using System.Net.Http.Json;
using Honua.Console.Contracts;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free coverage for <see cref="HonuaContentPublicationHttpClient"/> transport semantics. Caller-requested
/// cancellation must propagate (it cancels the calling operation rather than masquerading as an Unavailable
/// endpoint issue), while an HttpClient timeout still surfaces as Unavailable. Status mapping (404 not found,
/// 403 missing permission) and the success path are also asserted. Mirrors the form package client's contract.
/// </summary>
public sealed class ContentPublicationHttpClientTests
{
    private static readonly Uri BaseAddress = new("https://honua.test");

    [Fact]
    public async Task Get_WhenCallerCancels_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var client = CreateClient(new BlockUntilCancelledHandler());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync("pub-1", cts.Token));
    }

    [Fact]
    public async Task Get_OnHttpClientTimeout_ReturnsUnavailable()
    {
        using var client = CreateClient(new BlockUntilCancelledHandler(), TimeSpan.FromMilliseconds(50));

        var result = await client.GetAsync("pub-1", CancellationToken.None);

        Assert.NotNull(result.Issue);
        Assert.Equal("Unavailable", result.Issue!.State);
    }

    [Fact]
    public async Task Get_WhenNotFound_MapsToUnsupportedIssue()
    {
        using var client = CreateClient(new StaticResponseHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));

        var result = await client.GetAsync("pub-missing");

        Assert.Null(result.Data);
        Assert.NotNull(result.Issue);
        Assert.Equal("Unsupported", result.Issue!.State);
        Assert.Equal(404, result.Issue.StatusCode);
    }

    [Fact]
    public async Task Get_WhenForbidden_MapsToMissingPermission()
    {
        using var client = CreateClient(new StaticResponseHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)));

        var result = await client.GetAsync("pub-1");

        Assert.NotNull(result.Issue);
        Assert.Equal("Missing permission", result.Issue!.State);
        Assert.Equal(403, result.Issue.StatusCode);
    }

    [Fact]
    public async Task Get_OnSuccess_DeserializesDetailAndTargetsTheConsoleRoute()
    {
        string? requestedPath = null;
        var detail = new HonuaContentPublicationDetail
        {
            Route = new HonuaContentPublicationRouteState
            {
                PublicationId = "pub-report-1",
                RouteSlug = "monthly-infrastructure",
                RoutePath = "/published/monthly-infrastructure",
                Kind = HonuaContentPublicationKinds.Report,
                ActiveVersionId = "ver-2",
                ActiveRevision = 2,
                Etag = "etag-2"
            },
            Versions =
            [
                new HonuaContentPublicationVersion
                {
                    PublicationId = "pub-report-1",
                    VersionId = "ver-2",
                    Revision = 2,
                    Kind = HonuaContentPublicationKinds.Report,
                    RouteSlug = "monthly-infrastructure",
                    RoutePath = "/published/monthly-infrastructure",
                    Title = "Monthly infrastructure report",
                    CreatedBy = "ops@honua.test"
                }
            ]
        };
        using var client = CreateClient(new StaticResponseHandler(request =>
        {
            requestedPath = request.RequestUri?.AbsolutePath;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(detail) };
        }));

        var result = await client.GetAsync("pub-report-1");

        Assert.Null(result.Issue);
        Assert.NotNull(result.Data);
        Assert.Equal("/api/v1/console/publications/pub-report-1", requestedPath);
        Assert.Equal("pub-report-1", result.Data!.Route.PublicationId);
        Assert.Equal(HonuaContentPublicationKinds.Report, result.Data.Route.Kind);
        var version = Assert.Single(result.Data.Versions);
        Assert.Equal("Monthly infrastructure report", version.Title);
    }

    [Fact]
    public async Task Publish_PostsReportPayloadToCollectionRoute_AndAcceptsCreated()
    {
        string? requestedPath = null;
        HttpMethod? method = null;
        string? sentBody = null;
        var detail = ReportDetail("pub-report-1", revision: 1, etag: "etag-1");
        using var client = CreateClient(new RecordingHandler(async request =>
        {
            requestedPath = request.RequestUri?.AbsolutePath;
            method = request.Method;
            sentBody = request.Content is null ? null : await request.Content.ReadAsStringAsync();
            // The publish endpoint returns 201 Created; the client must treat it as success.
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(detail) };
        }));

        var result = await client.PublishAsync(new HonuaPublishContentRequest
        {
            Kind = HonuaContentPublicationKinds.Report,
            RouteSlug = "monthly-infrastructure",
            Title = "Monthly infrastructure report",
            ContentPayload = "{\"format\":\"honua.report-document.v1\"}"
        });

        Assert.Null(result.Issue);
        Assert.NotNull(result.Data);
        Assert.Equal(HttpMethod.Post, method);
        Assert.Equal("/api/v1/console/publications", requestedPath);
        Assert.NotNull(sentBody);
        Assert.Contains("\"kind\":\"report\"", sentBody, StringComparison.Ordinal);
        Assert.Contains("monthly-infrastructure", sentBody!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Republish_PostsToRepublishRoute()
    {
        string? requestedPath = null;
        var detail = ReportDetail("pub-report-1", revision: 2, etag: "etag-2");
        using var client = CreateClient(new RecordingHandler(request =>
        {
            requestedPath = request.RequestUri?.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(detail) });
        }));

        var result = await client.RepublishAsync("pub-report-1", new HonuaRepublishContentRequest { Title = "v2" });

        Assert.Null(result.Issue);
        Assert.Equal("/api/v1/console/publications/pub-report-1/republish", requestedPath);
        Assert.Equal(2, result.Data!.Route.ActiveRevision);
    }

    [Fact]
    public async Task Rollback_PostsTargetVersionToRollbackRoute()
    {
        string? requestedPath = null;
        string? sentBody = null;
        var detail = ReportDetail("pub-report-1", revision: 1, etag: "etag-3");
        using var client = CreateClient(new RecordingHandler(async request =>
        {
            requestedPath = request.RequestUri?.AbsolutePath;
            sentBody = request.Content is null ? null : await request.Content.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(detail) };
        }));

        var result = await client.RollbackAsync("pub-report-1", new HonuaRollbackContentRequest { TargetVersionId = "ver-1" });

        Assert.Null(result.Issue);
        Assert.Equal("/api/v1/console/publications/pub-report-1/rollback", requestedPath);
        Assert.Contains("ver-1", sentBody!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdatePolicy_PatchesPolicyRoute_AndReturnsUpdatedRoute()
    {
        string? requestedPath = null;
        HttpMethod? method = null;
        var response = new HonuaContentPublicationPolicyUpdateResponse
        {
            Route = ReportDetail("pub-report-1", revision: 2, etag: "etag-9").Route with
            {
                Policy = new HonuaContentPublicationPolicy
                {
                    Visibility = HonuaContentPublicationVisibilities.Public,
                    Embed = new HonuaContentEmbedPolicy { AllowEmbedding = true }
                }
            }
        };
        using var client = CreateClient(new RecordingHandler(request =>
        {
            requestedPath = request.RequestUri?.AbsolutePath;
            method = request.Method;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(response) });
        }));

        var result = await client.UpdatePolicyAsync(
            "pub-report-1",
            new HonuaUpdatePublicationPolicyRequest { Visibility = HonuaContentPublicationVisibilities.Public });

        Assert.Null(result.Issue);
        Assert.Equal(HttpMethod.Patch, method);
        Assert.Equal("/api/v1/console/publications/pub-report-1/policy", requestedPath);
        Assert.Equal(HonuaContentPublicationVisibilities.Public, result.Data!.Route.Policy.Visibility);
    }

    [Fact]
    public async Task Republish_WhenEtagConflict_MapsToConflictIssue()
    {
        using var client = CreateClient(new RecordingHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict))));

        var result = await client.RepublishAsync("pub-report-1", new HonuaRepublishContentRequest { ExpectedEtag = "stale" });

        Assert.Null(result.Data);
        Assert.NotNull(result.Issue);
        Assert.Equal("Conflict", result.Issue!.State);
        Assert.Equal(409, result.Issue.StatusCode);
    }

    private static HonuaContentPublicationDetail ReportDetail(string publicationId, long revision, string etag) =>
        new()
        {
            Route = new HonuaContentPublicationRouteState
            {
                PublicationId = publicationId,
                RouteSlug = "monthly-infrastructure",
                RoutePath = "/published/monthly-infrastructure",
                Kind = HonuaContentPublicationKinds.Report,
                ActiveVersionId = $"ver-{revision}",
                ActiveRevision = revision,
                Lifecycle = HonuaContentPublicationLifecycles.Active,
                Etag = etag
            },
            Versions =
            [
                new HonuaContentPublicationVersion
                {
                    PublicationId = publicationId,
                    VersionId = $"ver-{revision}",
                    Revision = revision,
                    Kind = HonuaContentPublicationKinds.Report,
                    RouteSlug = "monthly-infrastructure",
                    RoutePath = "/published/monthly-infrastructure",
                    Title = "Monthly infrastructure report",
                    CreatedBy = "ops@honua.test"
                }
            ]
        };

    private static HonuaContentPublicationHttpClient CreateClient(HttpMessageHandler handler, TimeSpan? timeout = null)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = BaseAddress };
        if (timeout is { } value)
        {
            httpClient.Timeout = value;
        }

        return new HonuaContentPublicationHttpClient(httpClient, new HonuaContentPublicationClientOptions(BaseAddress));
    }

    private sealed class BlockUntilCancelledHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class StaticResponseHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public StaticResponseHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_responseFactory(request));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _responseFactory;

        public RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _responseFactory(request);
    }
}
