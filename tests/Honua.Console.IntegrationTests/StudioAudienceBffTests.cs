using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Honua.Console.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class StudioAudienceBffTests
{
    private const string PublishedPath = "/api/studio/viewer/published/map/map-1";
    private const string FeaturePath = "/api/studio/viewer/data/rest/services/folder/service/FeatureServer/0/query";
    private const string StylePath = "/api/studio/viewer/styles/style-layer-7";

    [Theory]
    [InlineData("maps/2026.1", true)]
    [InlineData("maps/Hawaiʻi #1", true)]
    [InlineData("maps/../private", false)]
    [InlineData("maps/%2fprivate", false)]
    [InlineData("maps//private", false)]
    [InlineData("maps/map\nprivate", false)]
    public void PublishedRoutesPreserveNamesWithoutAllowingTraversal(string route, bool expected)
        => Assert.Equal(expected, StudioAudienceBff.IsRoute(route));

    [Fact]
    public async Task AnonymousReadForwardsNoBrowserOrConfiguredCredential()
    {
        using var fixture = new Fixture();
        var context = fixture.Context(PublishedPath);
        context.Request.Headers.Authorization = "Bearer browser-supplied";
        context.Request.Headers.Cookie = "console=secret";
        context.Request.Headers["X-API-Key"] = "browser-key";

        Assert.Equal(200, await Run(StudioAudienceBff.ReadPublishedAsync(context, "map/map-1", default), context));
        Assert.Equal("https://server.test/api/v1/studio/published/map/map-1?includeSources=true", fixture.Wire.Uri?.AbsoluteUri);
        Assert.Null(fixture.Wire.Authorization);
        Assert.False(fixture.Wire.HasApiKey);
        Assert.False(fixture.Wire.HasCookie);
        Assert.Equal("honua-studio-audience", fixture.Wire.ClientName);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.False(string.IsNullOrWhiteSpace(context.Response.Headers[StudioAudienceBff.PartitionHeader]));
    }

    [Fact]
    public async Task AuthenticatedReadForwardsOnlyMatchingCurrentBearer()
    {
        using var fixture = new Fixture();
        await fixture.SignIn();
        var context = fixture.Context(PublishedPath, authenticated: true);

        Assert.Equal(200, await Run(StudioAudienceBff.ReadPublishedAsync(context, "map/map-1", default), context));
        Assert.Equal("Bearer current-bearer", fixture.Wire.Authorization);
        Assert.False(fixture.Wire.HasApiKey);
        Assert.False(fixture.Wire.HasCookie);
    }

    [Fact]
    public async Task MissingCredentialOrStablePrincipalKeyFailsClosed()
    {
        using var fixture = new Fixture();
        await fixture.SignIn();
        fixture.Bearer.Token = null;
        var missing = fixture.Context(PublishedPath, authenticated: true);
        Assert.Equal(401, await Run(StudioAudienceBff.ReadPublishedAsync(missing, "map/map-1", default), missing));

        var nameless = fixture.Context(PublishedPath);
        nameless.User = new ClaimsPrincipal(new ClaimsIdentity([], "test-cookie"));
        Assert.Equal(401, await Run(StudioAudienceBff.ReadPublishedAsync(nameless, "map/map-1", default), nameless));
        Assert.Equal(0, fixture.Wire.Calls);
    }

    [Fact]
    public async Task DifferentActiveTargetRefusesAuthenticatedRead()
    {
        using var fixture = new Fixture();
        await fixture.SignIn();
        await fixture.Profiles.UpsertProfileAsync(fixture.Profile with { ServerBaseUri = new Uri("https://other.test/") });
        var context = fixture.Context(PublishedPath, authenticated: true);

        Assert.Equal(409, await Run(StudioAudienceBff.ReadPublishedAsync(context, "map/map-1", default), context));
        Assert.Equal(0, fixture.Wire.Calls);
    }

    [Fact]
    public async Task DataReadRequiresPartitionFromCurrentPublishedRead()
    {
        using var fixture = new Fixture();
        var missing = fixture.Context(FeaturePath);
        Assert.Equal(409, await Run(StudioAudienceBff.ReadFeaturesAsync(missing, "folder/service/FeatureServer/0/query", default), missing));
        var stale = fixture.Context(FeaturePath);
        stale.Request.Headers[StudioAudienceBff.PartitionHeader] = "old-view";
        Assert.Equal(409, await Run(StudioAudienceBff.ReadFeaturesAsync(stale, "folder/service/FeatureServer/0/query", default), stale));
        Assert.Equal(0, fixture.Wire.Calls);

        var published = fixture.Context(PublishedPath);
        Assert.Equal(200, await Run(StudioAudienceBff.ReadPublishedAsync(published, "map/map-1", default), published));
        var data = fixture.Context(FeaturePath, "?f=geojson&resultRecordCount=2000");
        data.Request.Headers[StudioAudienceBff.PartitionHeader] = published.Response.Headers[StudioAudienceBff.PartitionHeader];
        Assert.Equal(200, await Run(StudioAudienceBff.ReadFeaturesAsync(data, "folder/service/FeatureServer/0/query", default), data));
        Assert.Equal("https://server.test/rest/services/folder/service/FeatureServer/0/query?f=geojson&resultRecordCount=2000", fixture.Wire.Uri?.AbsoluteUri);
    }

    [Fact]
    public async Task StyleReadUsesCurrentPartitionAndCanonicalMapboxRoute()
    {
        using var fixture = new Fixture();
        var missing = fixture.Context(StylePath);
        Assert.Equal(409, await Run(StudioAudienceBff.ReadStyleAsync(missing, "style-layer-7", default), missing));
        var stale = fixture.Context(StylePath);
        stale.Request.Headers[StudioAudienceBff.PartitionHeader] = "old-view";
        Assert.Equal(409, await Run(StudioAudienceBff.ReadStyleAsync(stale, "style-layer-7", default), stale));
        Assert.Equal(0, fixture.Wire.Calls);

        var published = fixture.Context(PublishedPath);
        Assert.Equal(200, await Run(StudioAudienceBff.ReadPublishedAsync(published, "map/map-1", default), published));
        fixture.Wire.EntityTag = "\"style-v7\"";
        var style = fixture.Context(StylePath);
        style.Request.Headers[StudioAudienceBff.PartitionHeader] = published.Response.Headers[StudioAudienceBff.PartitionHeader];
        style.Request.Headers.Authorization = "Bearer browser-supplied";
        style.Request.Headers.Cookie = "console=secret";
        style.Request.Headers["X-API-Key"] = "browser-key";
        style.Request.Headers.IfMatch = "\"browser-version\"";
        Assert.Equal(200, await Run(StudioAudienceBff.ReadStyleAsync(style, "style-layer-7", default), style));
        Assert.Equal("https://server.test/ogc/styles/style-layer-7", fixture.Wire.Uri?.AbsoluteUri);
        Assert.Equal("application/vnd.mapbox.style+json", fixture.Wire.Accept);
        Assert.Null(fixture.Wire.Authorization);
        Assert.False(fixture.Wire.HasApiKey);
        Assert.False(fixture.Wire.HasCookie);
        Assert.False(fixture.Wire.HasIfMatch);
        Assert.Equal("\"style-v7\"", style.Response.Headers.ETag.ToString());
        Assert.Equal("application/vnd.mapbox.style+json", style.Response.ContentType);
        Assert.Equal(published.Response.Headers[StudioAudienceBff.PartitionHeader],
            style.Response.Headers[StudioAudienceBff.PartitionHeader]);
        Assert.Equal("no-store", style.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task AuthenticatedStyleReadSendsOnlyTheCurrentBearer()
    {
        using var fixture = new Fixture();
        await fixture.SignIn();
        var published = fixture.Context(PublishedPath, authenticated: true);
        Assert.Equal(200, await Run(StudioAudienceBff.ReadPublishedAsync(published, "map/map-1", default), published));
        var style = fixture.Context(StylePath, authenticated: true);
        style.Request.Headers[StudioAudienceBff.PartitionHeader] = published.Response.Headers[StudioAudienceBff.PartitionHeader];
        Assert.Equal(200, await Run(StudioAudienceBff.ReadStyleAsync(style, "style-layer-7", default), style));
        Assert.Equal("Bearer current-bearer", fixture.Wire.Authorization);
    }

    [Theory]
    [InlineData("", "/api/studio/viewer/styles/", "")]
    [InlineData("..", "/api/studio/viewer/styles/..", "")]
    [InlineData("style/name", "/api/studio/viewer/styles/style/name", "")]
    [InlineData("style%2Fname", "/api/studio/viewer/styles/style%252Fname", "")]
    [InlineData("style-layer-7", "/api/studio/viewer/styles/style%2dlayer-7", "")]
    [InlineData("style-layer-7", "/api/studio/viewer/styles/style-layer-7", "?f=json")]
    public async Task StyleReadRejectsNonCanonicalIdOrQueryBeforeTransport(string id, string path, string query)
    {
        using var fixture = new Fixture();
        var context = fixture.Context(path, query);
        Assert.Equal(400, await Run(StudioAudienceBff.ReadStyleAsync(context, id, default), context));
        Assert.Equal(0, fixture.Wire.Calls);
    }

    [Fact]
    public async Task StyleReadRejectsOversizeAndChangedAuthorityWithoutLeakingEtag()
    {
        using var fixture = new Fixture();
        await fixture.SignIn();
        var published = fixture.Context(PublishedPath, authenticated: true);
        Assert.Equal(200, await Run(StudioAudienceBff.ReadPublishedAsync(published, "map/map-1", default), published));
        fixture.Wire.EntityTag = "\"style-v7\"";
        fixture.Wire.ContentFactory = () => new ByteArrayContent(new byte[256 * 1024 + 1]);
        var oversized = fixture.Context(StylePath, authenticated: true);
        oversized.Request.Headers[StudioAudienceBff.PartitionHeader] = published.Response.Headers[StudioAudienceBff.PartitionHeader];
        Assert.Equal(502, await Run(StudioAudienceBff.ReadStyleAsync(oversized, "style-layer-7", default), oversized));
        Assert.True(string.IsNullOrEmpty(oversized.Response.Headers.ETag));

        fixture.Wire.ContentFactory = null;
        fixture.Wire.OnSend = async () =>
        {
            fixture.Bearer.Token = "other-bearer";
            await fixture.Sessions.SaveSessionAsync(fixture.Session("other-account", "other-bearer"));
        };
        var changed = fixture.Context(StylePath, authenticated: true);
        changed.Request.Headers[StudioAudienceBff.PartitionHeader] = published.Response.Headers[StudioAudienceBff.PartitionHeader];
        Assert.Equal(409, await Run(StudioAudienceBff.ReadStyleAsync(changed, "style-layer-7", default), changed));
        Assert.True(string.IsNullOrEmpty(changed.Response.Headers.ETag));
        Assert.True(string.IsNullOrEmpty(changed.Response.Headers[StudioAudienceBff.PartitionHeader]));
    }

    [Fact]
    public async Task AccountSwitchDuringBearerResolutionCannotPairOldBearerWithNewAccount()
    {
        using var fixture = new Fixture();
        await fixture.SignIn();
        fixture.Bearer.OnResolve = async () => await fixture.Sessions.SaveSessionAsync(
            fixture.Session("other-account", "other-bearer"));
        var context = fixture.Context(PublishedPath, authenticated: true);

        Assert.Equal(401, await Run(StudioAudienceBff.ReadPublishedAsync(context, "map/map-1", default), context));
        Assert.Equal(0, fixture.Wire.Calls);
    }

    [Theory]
    [InlineData("account")]
    [InlineData("token")]
    [InlineData("target")]
    public async Task ReplyFromChangedAuthorityIsDiscarded(string change)
    {
        using var fixture = new Fixture();
        await fixture.SignIn();
        fixture.Wire.OnSend = async () =>
        {
            if (change == "account")
            {
                fixture.Bearer.Token = "other-bearer";
                await fixture.Sessions.SaveSessionAsync(fixture.Session("other-account", "other-bearer"));
            }
            else if (change == "token")
            {
                fixture.Bearer.Token = "rotated-bearer";
                await fixture.Sessions.SaveSessionAsync(fixture.Session("account", "rotated-bearer"));
            }
            else
            {
                await fixture.Profiles.UpsertProfileAsync(fixture.Profile with { ServerBaseUri = new Uri("https://other.test/") });
            }
        };
        var context = fixture.Context(PublishedPath, authenticated: true);

        Assert.Equal(409, await Run(StudioAudienceBff.ReadPublishedAsync(context, "map/map-1", default), context));
        Assert.Equal(1, fixture.Wire.Calls);
        Assert.True(string.IsNullOrEmpty(context.Response.Headers[StudioAudienceBff.PartitionHeader]));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task UpstreamAccessAndMissingStatusesArePreserved(HttpStatusCode status)
    {
        using var fixture = new Fixture();
        fixture.Wire.Status = status;
        var context = fixture.Context(PublishedPath);

        Assert.Equal((int)status, await Run(StudioAudienceBff.ReadPublishedAsync(context, "map/map-1", default), context));
        Assert.Equal(1, fixture.Wire.Calls);
    }

    [Fact]
    public async Task RedirectNonJsonAndOversizedRepliesAreRejected()
    {
        using var fixture = new Fixture();
        fixture.Wire.Status = HttpStatusCode.Redirect;
        var redirect = fixture.Context(PublishedPath);
        Assert.Equal(502, await Run(StudioAudienceBff.ReadPublishedAsync(redirect, "map/map-1", default), redirect));

        fixture.Wire.Status = HttpStatusCode.OK;
        fixture.Wire.Body = "<html>not JSON</html>";
        var html = fixture.Context(PublishedPath);
        Assert.Equal(502, await Run(StudioAudienceBff.ReadPublishedAsync(html, "map/map-1", default), html));

        fixture.Wire.ContentFactory = () => new ByteArrayContent(new byte[16 * 1024 * 1024 + 1]);
        var oversized = fixture.Context(PublishedPath);
        Assert.Equal(502, await Run(StudioAudienceBff.ReadPublishedAsync(oversized, "map/map-1", default), oversized));
        Assert.Equal(3, fixture.Wire.Calls);
    }

    [Theory]
    [InlineData("folder/service/FeatureServer/0/updateFeatures", "", 400)]
    [InlineData("folder/service/FeatureServer/0/query", "?resultRecordCount=2001", 400)]
    [InlineData("folder/service/FeatureServer/0/query", "?where=1%3D1&delete=true", 400)]
    [InlineData("folder/service/FeatureServer/0/query", "?where=1%3D1", 409)]
    public async Task FeatureRouteAllowsOnlyBoundedReadsWithCurrentPartition(string path, string query, int expected)
    {
        using var fixture = new Fixture();
        var context = fixture.Context("/api/studio/viewer/data/rest/services/" + path, query);
        Assert.Equal(expected, await Run(StudioAudienceBff.ReadFeaturesAsync(context, path, default), context));
        Assert.Equal(0, fixture.Wire.Calls);
    }

    [Fact]
    public async Task FeatureRouteRejectsExcessiveTotalQueryBeforeTransport()
    {
        using var fixture = new Fixture();
        var longValue = new string('a', 7000);
        var query = $"?where={longValue}&outFields={longValue}&geometry={longValue}&outSR={longValue}&inSR={longValue}";
        var context = fixture.Context(FeaturePath, query);

        Assert.Equal(400, await Run(StudioAudienceBff.ReadFeaturesAsync(context, "folder/service/FeatureServer/0/query", default), context));
        Assert.Equal(0, fixture.Wire.Calls);
    }

    [Fact]
    public async Task FeatureRouteRejectsEncodedSeparatorAndExcessivePath()
    {
        using var fixture = new Fixture();
        var encoded = fixture.Context(FeaturePath);
        encoded.Features.Get<IHttpRequestFeature>()!.RawTarget =
            "/api/studio/viewer/data/rest/services/folder%2fservice/FeatureServer/0/query";
        Assert.Equal(400, await Run(StudioAudienceBff.ReadFeaturesAsync(
            encoded, "folder/service/FeatureServer/0/query", default), encoded));

        var longPath = "folder/" + new string('a', 4096) + "/FeatureServer/0/query";
        var longContext = fixture.Context("/api/studio/viewer/data/rest/services/" + longPath);
        Assert.Equal(400, await Run(StudioAudienceBff.ReadFeaturesAsync(longContext, longPath, default), longContext));
        Assert.Equal(0, fixture.Wire.Calls);
    }

    private static async Task<int> Run(Task<IResult> result, HttpContext context)
    {
        await (await result).ExecuteAsync(context);
        return context.Response.StatusCode;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider _services;
        public ConsoleEnvironmentProfile Profile { get; } = new() { Id = "environment", ServerBaseUri = new Uri("https://server.test/") };
        public InMemoryConsoleEnvironmentProfileStore Profiles { get; }
        public InMemoryConsoleAccountSessionStore Sessions { get; } = new();
        public MutableBearer Bearer { get; } = new();
        public Wire Wire { get; } = new();

        public Fixture()
        {
            Profiles = new InMemoryConsoleEnvironmentProfileStore([Profile]);
            _services = new ServiceCollection()
                .AddLogging()
                .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Honua:Server:BaseUrl"] = Profile.ServerBaseUri.AbsoluteUri,
                    ["Honua:Server:AdminApiKey"] = "configured-admin-secret"
                }).Build())
                .AddSingleton<IConsoleEnvironmentProfileStore>(Profiles)
                .AddSingleton<IConsoleAccountSessionStore>(Sessions)
                .AddSingleton<IConsoleOperatorBearerProvider>(Bearer)
                .AddSingleton<IHttpClientFactory>(Wire)
                .BuildServiceProvider();
        }

        public Task SignIn() => Sessions.SaveSessionAsync(Session("account", "current-bearer"));

        public ConsoleAccountSession Session(string account, string token) => new()
        {
            ProfileId = Profile.Id,
            AccountId = account,
            TenantId = "tenant",
            ServerBaseUri = Profile.ServerBaseUri,
            AccessToken = token
        };

        public DefaultHttpContext Context(string path, string query = "", bool authenticated = false)
        {
            var context = new DefaultHttpContext { RequestServices = _services };
            context.Response.Body = new MemoryStream();
            context.Request.Path = path;
            context.Request.QueryString = new QueryString(query);
            context.Features.Get<IHttpRequestFeature>()!.RawTarget = path + query;
            if (authenticated)
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "operator")], "test-cookie"));
            }
            return context;
        }

        public void Dispose()
        {
            _services.Dispose();
            Wire.Dispose();
        }
    }

    private sealed class MutableBearer : IConsoleOperatorBearerProvider
    {
        public string? Token { get; set; } = "current-bearer";
        public Func<Task>? OnResolve { get; set; }

        public async Task<ConsoleOperatorBearerResolution> ResolveAsync(
            ConsoleEnvironmentProfile profile, CancellationToken cancellationToken = default)
        {
            var token = Token;
            if (OnResolve is not null) await OnResolve();
            return new ConsoleOperatorBearerResolution { AccessToken = token, HasInteractiveSession = true };
        }
    }

    private sealed class Wire : HttpMessageHandler, IHttpClientFactory
    {
        public int Calls { get; private set; }
        public string? ClientName { get; private set; }
        public Uri? Uri { get; private set; }
        public string? Authorization { get; private set; }
        public bool HasApiKey { get; private set; }
        public bool HasCookie { get; private set; }
        public bool HasIfMatch { get; private set; }
        public string? Accept { get; private set; }
        public string? EntityTag { get; set; }
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public string Body { get; set; } = "{}";
        public Func<HttpContent>? ContentFactory { get; set; }
        public Func<Task>? OnSend { get; set; }

        public HttpClient CreateClient(string name)
        {
            ClientName = name;
            return new HttpClient(this, disposeHandler: false);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Uri = request.RequestUri;
            Authorization = request.Headers.Authorization?.ToString();
            HasApiKey = request.Headers.Contains("X-API-Key");
            HasCookie = request.Headers.Contains("Cookie");
            HasIfMatch = request.Headers.Contains("If-Match");
            Accept = request.Headers.Accept.ToString();
            if (OnSend is not null) await OnSend();
            var response = new HttpResponseMessage(Status) { Content = ContentFactory?.Invoke() ?? new StringContent(Body) };
            if (EntityTag is not null) response.Headers.ETag = new EntityTagHeaderValue(EntityTag);
            return response;
        }
    }
}
