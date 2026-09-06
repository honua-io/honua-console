using System.Net;
using System.Net.Http.Headers;
using Honua.Console.Shell.DependencyInjection;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Security;
using Honua.Console.Shell.Services;
using Honua.Console.Web.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace Honua.Console.IntegrationTests;

public sealed class FocusedOperatorCredentialTests
{
    public static IEnumerable<object[]> MissingCredentials()
    {
        foreach (var client in new[] { ConsoleServerBoundClients.ServerBoundClientName, "honua-map-proxy" })
        foreach (var state in new[] { "absent", "sentinel", "expired", "target", "unbound" })
        foreach (var method in new[] { "GET", "POST" })
            yield return [client, state, method];
    }

    [Theory]
    [MemberData(nameof(MissingCredentials))]
    public async Task InvalidOperatorCredential_ReturnsSignInWithoutTransportOrKeyFallback(string name, string state, string method)
    {
        using var fixture = new Fixture();
        await fixture.BindAsync("alice", "tenant-a");
        if (state == "absent")
            await fixture.Sessions.ClearSessionAsync("environment");
        else
        {
            var session = (await fixture.Sessions.GetSessionAsync("environment"))!;
            await fixture.Sessions.SaveSessionAsync(session with
            {
                AccessToken = state == "sentinel" ? "profile-session:environment" : session.AccessToken,
                AccessTokenExpiresAt = state == "expired" ? DateTimeOffset.UtcNow.AddMinutes(-1) : session.AccessTokenExpiresAt,
                ServerBaseUri = state == "target" ? new Uri("https://other.honua.test/") : state == "unbound" ? null : session.ServerBaseUri
            });
        }

        using var client = fixture.Services.GetRequiredService<IHttpClientFactory>().CreateClient(name);
        using var request = new HttpRequestMessage(new HttpMethod(method), "https://server.honua.test/api/v1/admin/proposals/protected");
        request.Headers.Add("X-API-Key", "shared-admin-secret");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "stale-client-credential");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Sign in", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain("protected", await response.Content.ReadAsStringAsync());
        Assert.Contains(response.Headers.WwwAuthenticate, value => value.Scheme == "Bearer");
        Assert.Null(request.Headers.Authorization);
        Assert.False(request.Headers.Contains("X-API-Key"));
        Assert.Empty(fixture.Wire.Requests);
    }

    [Fact]
    public async Task MapProxy_TargetMismatchDoesNotSendCredentialToConfiguredServer()
    {
        using var fixture = new Fixture();
        await fixture.BindAsync("alice", "tenant-a");
        using var client = fixture.Services.GetRequiredService<IHttpClientFactory>().CreateClient("honua-map-proxy");
        using var response = await client.GetAsync("https://another.honua.test/tiles/7/0/0/0.mvt");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(fixture.Wire.Requests);
    }

    [Fact]
    public async Task ProfileTargetEditCannotReuseAnExistingOperatorBearer()
    {
        using var fixture = new Fixture();
        await fixture.BindAsync("alice", "tenant-a");
        var profile = (await fixture.Profiles.GetActiveProfileAsync())!;
        await fixture.Profiles.UpsertProfileAsync(profile with { ServerBaseUri = new Uri("https://new.honua.test/") });
        using var client = fixture.Services.GetRequiredService<IHonuaServerBoundClientFactory>()
            .CreateServerBoundClient(new Uri("https://startup.honua.test/"));
        using var response = await client.GetAsync("api/v1/admin/connections");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(fixture.Wire.Requests);
    }

    [Theory]
    [InlineData(ConsoleServerBoundClients.ServerBoundClientName)]
    [InlineData("honua-map-proxy")]
    public async Task TwoOperatorsAndTenants_ForwardTheirOwnBearerAndPreserveServerDenials(string clientName)
    {
        using var fixture = new Fixture();
        await fixture.BindAsync("alice", "tenant-a");
        await fixture.BindAsync("bob", "tenant-b");
        using var client = fixture.Services.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);
        foreach (var actor in new[] { "alice", "bob", "alice" })
        {
            fixture.Operator.CurrentOperatorKey = actor;
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://server.honua.test/api/v1/admin/proposals/protected");
            request.Headers.Add("X-API-Key", "shared-admin-secret");
            fixture.Wire.Status = actor == "alice" ? HttpStatusCode.OK : HttpStatusCode.Forbidden;
            using var response = await client.SendAsync(request);
            Assert.Equal(fixture.Wire.Status, response.StatusCode);
            Assert.Equal($"Bearer {actor}-bearer", fixture.Wire.Requests.Last());
            Assert.False(request.Headers.Contains("X-API-Key"));
            if (actor == "bob") Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(3, fixture.Wire.Requests.Count);
    }

    [Fact]
    public async Task RealRegisteredReleaseRead_StripsConfiguredKeyAndForwardsOperator()
    {
        using var fixture = new Fixture();
        await fixture.BindAsync("alice", "tenant-a");
        var client = fixture.Services.GetRequiredService<IConsoleServerVersionClient>();
        var result = await client.GetServerVersionAsync();
        Assert.Equal(OperateSectionStatus.Allowed, result.Status);
        Assert.Equal("2026.1.7", result.Value!.ServerVersion);
        Assert.Equal("Bearer alice-bearer", Assert.Single(fixture.Wire.Requests));
    }

    [Fact]
    public async Task ProductionDeployment_BindsConfiguredTargetPerOperatorWithoutGrantingASession()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Honua:Server:BaseUrl"] = "https://production.honua.test/"
        });
        builder.Services.AddHonuaConsoleShell("https://production.honua.test/");
        builder.AddConsoleAuthentication();
        var actor = new OperatorContext();
        builder.Services.AddSingleton<IConsoleOperatorContext>(actor);
        using var services = builder.Services.BuildServiceProvider();
        var profiles = services.GetRequiredService<IConsoleEnvironmentProfileStore>();
        var alice = await profiles.GetActiveProfileAsync();
        Assert.Equal(new Uri("https://production.honua.test/"), alice!.ServerBaseUri);
        Assert.Equal("production", alice.EnvironmentKind);
        Assert.Null(await services.GetRequiredService<IConsoleAccountSessionStore>().GetSessionAsync(alice.Id));
        await profiles.UpsertProfileAsync(alice with { DisplayName = "Alice's environment" });
        actor.CurrentOperatorKey = "bob";
        Assert.Equal("Configured honua-server", (await profiles.GetActiveProfileAsync())!.DisplayName);
    }

    private sealed class Fixture : IDisposable
    {
        public OperatorContext Operator { get; } = new();
        public CaptureHandler Wire { get; } = new();
        public IConsoleEnvironmentProfileStore Profiles { get; }
        public IConsoleAccountSessionStore Sessions { get; }
        public ServiceProvider Services { get; }

        public Fixture()
        {
            Profiles = new OperatorScopedEnvironmentProfileStore(Operator);
            Sessions = new OperatorScopedAccountSessionStore(Operator);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IConsoleOperatorContext>(Operator);
            services.AddSingleton(Profiles);
            services.AddSingleton(Sessions);
            services.AddHonuaConsoleShell("https://server.honua.test/", "shared-admin-secret");
            services.AddConsoleServerBoundClients();
            foreach (var name in new[] { ConsoleServerBoundClients.ServerBoundClientName, "honua-map-proxy" })
                services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => Wire);
            Services = services.BuildServiceProvider();
        }

        public async Task BindAsync(string actor, string tenant)
        {
            Operator.CurrentOperatorKey = actor;
            await Profiles.UpsertProfileAsync(new ConsoleEnvironmentProfile
            {
                Id = "environment", ServerBaseUri = new Uri("https://server.honua.test/"), TenantId = tenant,
                Account = new ConsoleAccountBinding { AccountId = actor, TenantId = tenant, AuthMode = ConsoleAccountAuthMode.AccountRbac }
            });
            await Profiles.ActivateProfileAsync("environment");
            await Sessions.SaveSessionAsync(new ConsoleAccountSession
            {
                ProfileId = "environment", AccountId = actor, TenantId = tenant,
                AccessToken = $"{actor}-bearer", ServerBaseUri = new Uri("https://server.honua.test/"),
                AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
            });
        }

        public void Dispose() => Services.Dispose();
    }

    private sealed class OperatorContext : IConsoleOperatorContext
    {
        public string CurrentOperatorKey { get; set; } = "alice";
        public bool HasOperator => CurrentOperatorKey != ConsoleOperatorContext.AnonymousKey;
        public string RequireOperatorKey() => HasOperator ? CurrentOperatorKey : throw new ConsoleOperatorContextUnresolvedException();
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.False(request.Headers.Contains("X-API-Key"));
            Requests.Add(request.Headers.Authorization?.ToString() ?? "anonymous");
            return Task.FromResult(new HttpResponseMessage(Status)
            {
                Content = new StringContent(Status == HttpStatusCode.OK
                    ? "{\"server\":{\"serverVersion\":\"2026.1.7\",\"apiVersion\":\"1\"}}" : string.Empty)
            });
        }
    }
}
