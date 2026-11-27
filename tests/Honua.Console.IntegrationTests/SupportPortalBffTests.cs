using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Honua.Console.Web;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class SupportPortalBffTests
{
    private const string ConfiguredApiKey = "honua-support-super-secret-customer-key-98765";

    [Theory]
    [InlineData(null, ConfiguredApiKey)]
    [InlineData("https://support.honua.example", null)]
    [InlineData("http://support.honua.example", ConfiguredApiKey)]
    [InlineData("https://user:pass@support.honua.example", ConfiguredApiKey)]
    public async Task UnconfiguredOrUnsafeSettingsReturn503WithSetupGuidance(string? baseUrl, string? apiKey)
    {
        await using var host = await BffHost.StartAsync(new HostOptions
        {
            SupportBaseUrl = baseUrl,
            SupportApiKey = apiKey
        });

        using var response = await host.Client.GetAsync("/api/support/security/advisories");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains(SupportPortalBff.UnconfiguredCode, body);
        Assert.Contains("HonuaSupport:BaseUrl", body);
        Assert.Contains("HonuaSupport:ApiKey", body);
        Assert.DoesNotContain(ConfiguredApiKey, body);
        Assert.Empty(host.Upstream.Requests);
    }

    [Fact]
    public async Task UnauthenticatedRequestsReturn401WithoutCallingUpstream()
    {
        await using var host = await BffHost.StartAsync(new HostOptions
        {
            SupportBaseUrl = "https://support.honua.example",
            SupportApiKey = ConfiguredApiKey,
            OperatorId = null
        });

        using var response = await host.Client.GetAsync("/api/support/tickets");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(host.Upstream.Requests);
    }

    [Fact]
    public async Task MissingAntiforgeryTokenOnPostReturns403WithoutCallingUpstream()
    {
        await using var host = await BffHost.StartAsync(new HostOptions
        {
            SupportBaseUrl = "https://support.honua.example",
            SupportApiKey = ConfiguredApiKey
        });

        using var content = new StringContent("""{"symptoms":"500s"}""", Encoding.UTF8, "application/json");
        using var response = await host.Client.PostAsync("/api/support/preflight", content);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(host.Upstream.Requests);
    }

    [Fact]
    public async Task BffInjectsBearerServerSideStripsClientCredentialsAndNeverEchoesKey()
    {
        await using var host = await BffHost.StartAsync(new HostOptions
        {
            SupportBaseUrl = "https://support.honua.example",
            SupportApiKey = ConfiguredApiKey
        });
        host.Upstream.Responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""{"count":0,"advisories":[],"echo":"{{ConfiguredApiKey}}"}""",
                Encoding.UTF8,
                "application/json")
        };

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/support/security/advisories");
        request.Headers.Authorization = new("Bearer", "browser-spoofed-token");
        request.Headers.Add("X-API-Key", "browser-spoofed-api-key");
        request.Headers.Add("Cookie", "session=browser-cookie");

        using var response = await host.Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.DoesNotContain(ConfiguredApiKey, body);
        Assert.DoesNotContain(ConfiguredApiKey, response.Headers.ToString());

        var upstream = Assert.Single(host.Upstream.Requests);
        Assert.Equal("https://support.honua.example/api/v1/security/advisories", upstream.Uri.AbsoluteUri);
        Assert.Equal($"Bearer {ConfiguredApiKey}", upstream.Authorization);
        Assert.False(upstream.HasApiKeyHeader);
        Assert.False(upstream.HasCookieHeader);
    }

    [Fact]
    public async Task PreflightAutomaticallyAttachesConnectedServerVersionAndEnvironmentContext()
    {
        await using var host = await BffHost.StartAsync(new HostOptions
        {
            SupportBaseUrl = "https://support.honua.example",
            SupportApiKey = ConfiguredApiKey,
            ConnectedServerVersion = "2026.2.1-rc1",
            EnvironmentKind = "staging",
            ServerUri = "https://gis-staging.honua.example/",
            TenantId = "tenant-staging"
        });
        host.Upstream.Responder = _ => JsonOk("""{"productArea":"server","issueKind":"bug","classificationProvenance":"inferred","knownIssues":[]}""");

        using var response = await host.PostWithCsrfAsync("/api/support/preflight", """{"severity":"sev2","symptoms":"Timeout on FeatureServer"}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var upstream = Assert.Single(host.Upstream.Requests);
        Assert.Equal(HttpMethod.Post, upstream.Method);
        Assert.Equal("https://support.honua.example/api/v1/intake/preflight", upstream.Uri.AbsoluteUri);

        using var doc = JsonDocument.Parse(upstream.Body);
        var root = doc.RootElement;
        Assert.Equal("staging", root.GetProperty("environment").GetString());
        Assert.Equal("honua-server", root.GetProperty("service").GetString());
        Assert.Equal("2026.2.1-rc1", root.GetProperty("clientContext").GetProperty("serverVersion").GetString());
        Assert.Equal("honua-console", root.GetProperty("clientContext").GetProperty("clientName").GetString());
        var context = root.GetProperty("context");
        Assert.Equal("1.0", context.GetProperty("schemaVersion").GetString());
        Assert.Equal("staging", context.GetProperty("envKind").GetString());
        Assert.Equal("https://gis-staging.honua.example/", context.GetProperty("instanceUrl").GetString());
        Assert.Equal("2026.2.1-rc1", context.GetProperty("appVersion").GetString());
        Assert.Equal("tenant-staging", context.GetProperty("tenant").GetProperty("id").GetString());
    }

    [Fact]
    public async Task TicketsApproveRevokeAndFleetQueryForwardToExpectedUpstreamRoutes()
    {
        await using var host = await BffHost.StartAsync(new HostOptions
        {
            SupportBaseUrl = "https://support.honua.example",
            SupportApiKey = ConfiguredApiKey
        });
        host.Upstream.Responder = _ => JsonOk("""{"ok":true}""");

        using var listTickets = await host.Client.GetAsync("/api/support/tickets");
        using var createTicket = await host.PostWithCsrfAsync("/api/support/tickets", """{"severity":"sev3","symptoms":"slow tiles"}""");
        using var approve = await host.PostWithCsrfAsync("/api/support/tickets/TKT-101/approve", """{"accessMode":"operator-scoped","ttlMinutes":30}""");
        using var revoke = await host.PostWithCsrfAsync("/api/support/tickets/TKT-101/revoke-access", """{"reason":"resolved"}""");
        using var fleet = await host.Client.GetAsync("/api/support/fleet/instances?component=honua-server&version=1.4.0");

        Assert.Equal(HttpStatusCode.OK, listTickets.StatusCode);
        Assert.Equal(HttpStatusCode.OK, createTicket.StatusCode);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.OK, fleet.StatusCode);

        Assert.Equal(
            [
                "https://support.honua.example/api/v1/tickets",
                "https://support.honua.example/api/v1/tickets",
                "https://support.honua.example/api/v1/tickets/TKT-101/approve",
                "https://support.honua.example/api/v1/tickets/TKT-101/revoke-access",
                "https://support.honua.example/api/v1/fleet/instances?component=honua-server&version=1.4.0"
            ],
            host.Upstream.Requests.Select(r => r.Uri.AbsoluteUri).ToArray());
    }

    [Theory]
    [InlineData("..")]
    [InlineData("ticket/evil")]
    [InlineData("ticket%2fevil")]
    public async Task InvalidTicketIdAndUnexpectedFleetQueryReturn400(string badTicketId)
    {
        await using var host = await BffHost.StartAsync(new HostOptions
        {
            SupportBaseUrl = "https://support.honua.example",
            SupportApiKey = ConfiguredApiKey
        });

        Assert.False(SupportPortalBff.IsSafeTicketId(badTicketId));

        using var badQuery = await host.Client.GetAsync("/api/support/fleet/instances?customerId=other-tenant");
        Assert.Equal(HttpStatusCode.BadRequest, badQuery.StatusCode);
        Assert.Empty(host.Upstream.Requests);
    }

    [Fact]
    public async Task UpstreamRedirectReturns502BadGateway()
    {
        await using var host = await BffHost.StartAsync(new HostOptions
        {
            SupportBaseUrl = "https://support.honua.example",
            SupportApiKey = ConfiguredApiKey
        });
        host.Upstream.Responder = _ => new HttpResponseMessage(HttpStatusCode.Redirect);

        using var response = await host.Client.GetAsync("/api/support/security/advisories");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    private static HttpResponseMessage JsonOk(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed record HostOptions
    {
        public string? SupportBaseUrl { get; init; }

        public string? SupportApiKey { get; init; }

        public string? OperatorId { get; init; } = "operator-a";

        public string? ConnectedServerVersion { get; init; }

        public string EnvironmentKind { get; init; } = "production";

        public string ServerUri { get; init; } = "https://gis.honua.example/";

        public string? TenantId { get; init; } = "tenant-prod";
    }

    private sealed class BffHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        public HttpClient Client { get; }

        public RecordingUpstreamHandler Upstream { get; }

        private BffHost(WebApplication app, HttpClient client, RecordingUpstreamHandler upstream)
        {
            _app = app;
            Client = client;
            Upstream = upstream;
        }

        public static async Task<BffHost> StartAsync(HostOptions options)
        {
            var upstream = new RecordingUpstreamHandler();
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            ConfigureSettings(builder, options);
            ConfigureServices(builder, options, upstream);
            var app = builder.Build();
            ConfigurePipeline(app, options.OperatorId);
            await app.StartAsync();

            var address = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            var cookies = new CookieContainer();
            var client = new HttpClient(new HttpClientHandler { CookieContainer = cookies, AllowAutoRedirect = false })
            {
                BaseAddress = new Uri(address)
            };
            return new BffHost(app, client, upstream);
        }

        public async Task<HttpResponseMessage> PostWithCsrfAsync(string path, string json)
        {
            var token = await Client.GetStringAsync("/_test/csrf");
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-CSRF-Token", token);
            return await Client.SendAsync(request);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.DisposeAsync();
        }

        private static void ConfigureSettings(WebApplicationBuilder builder, HostOptions options)
        {
            var values = new Dictionary<string, string?>
            {
                ["HonuaSupport:BaseUrl"] = options.SupportBaseUrl,
                ["HonuaSupport:ApiKey"] = options.SupportApiKey
            };
            builder.Configuration.AddInMemoryCollection(values);
        }

        private static void ConfigureServices(WebApplicationBuilder builder, HostOptions options, RecordingUpstreamHandler upstream)
        {
            builder.Services.AddAntiforgery(o => o.HeaderName = "X-CSRF-Token");
            builder.Services.AddHttpClient(SupportPortalBff.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => upstream);
            var profile = new ConsoleEnvironmentProfile
            {
                Id = "env-1",
                DisplayName = "Connected GIS",
                ServerBaseUri = new Uri(options.ServerUri),
                EnvironmentKind = options.EnvironmentKind,
                TenantId = options.TenantId ?? string.Empty
            };
            builder.Services.AddSingleton<IConsoleEnvironmentProfileStore>(
                new InMemoryConsoleEnvironmentProfileStore([profile], activeProfileId: profile.Id));
            if (!string.IsNullOrWhiteSpace(options.ConnectedServerVersion))
            {
                builder.Services.AddSingleton<IConsoleServerVersionClient>(
                    new InMemoryConsoleServerVersionClient(new ServerVersionInfo(
                        options.ConnectedServerVersion,
                        "2026.2",
                        "sha256:abc",
                        "1.0",
                        "https://gis.honua.example/",
                        DateTimeOffset.UtcNow)));
            }
        }

        private static void ConfigurePipeline(WebApplication app, string? operatorId)
        {
            app.Use((context, next) =>
            {
                if (!string.IsNullOrWhiteSpace(operatorId))
                {
                    context.User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, operatorId)],
                        "Test"));
                }

                return next(context);
            });
            app.UseAntiforgery();
            app.MapGet("/_test/csrf", (HttpContext ctx, IAntiforgery antiforgery) =>
                Results.Text(antiforgery.GetAndStoreTokens(ctx).RequestToken ?? string.Empty));
            app.MapSupportPortalBff();
        }
    }

    private sealed record CapturedUpstream
    {
        public required HttpMethod Method { get; init; }

        public required Uri Uri { get; init; }

        public string? Authorization { get; init; }

        public bool HasApiKeyHeader { get; init; }

        public bool HasCookieHeader { get; init; }

        public string Body { get; init; } = string.Empty;
    }

    private sealed class RecordingUpstreamHandler : HttpMessageHandler
    {
        public List<CapturedUpstream> Requests { get; } = [];

        public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } = _ => JsonOk("{}");

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedUpstream
            {
                Method = request.Method,
                Uri = request.RequestUri!,
                Authorization = request.Headers.Authorization?.ToString(),
                HasApiKeyHeader = request.Headers.Contains("X-API-Key"),
                HasCookieHeader = request.Headers.Contains("Cookie"),
                Body = body
            });
            return Responder(request);
        }
    }
}
