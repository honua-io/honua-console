using Honua.Console.Contracts;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Security;
using Honua.Console.Shell.Services;
using Honua.Console.Web.Auth;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// End-state "fail-closed by construction" coverage for the honua-console#254 refactor: the Family-A
/// server-bound typed clients now obtain their <see cref="HttpClient"/> from the browser Web host's
/// <see cref="ConsoleServerBoundClients"/> IHttpClientFactory named clients + scoped operator-injected
/// binding chain, instead of a self-contained per-client pooled handler.
///
/// These tests exercise the REAL registered chain (<see cref="ConsoleServerBoundClients.AddConsoleServerBoundClients"/>
/// + <see cref="ConsoleServerBoundOperatorGuardHandler"/> + <c>HonuaServerBindingHandler</c> over the
/// operator-partitioned stores), swapping only the pooled primary handler for a capturing one, and prove:
/// <list type="number">
/// <item>an UNRESOLVED operator is a hard deny on EVERY privileged server-bound client (they all funnel
/// through the one <see cref="ConsoleServerBoundClients.ServerBoundClientName"/> chain) — the
/// <c>__anonymous__</c> sentinel no longer yields a usable server-bound identity;</item>
/// <item>concurrent operators stay isolated — each call carries that operator's OWN bearer, never
/// another's, even under interleaving on the shared factory;</item>
/// <item>the legitimately-anonymous PUBLIC client tolerates an unresolved operator by design (keeps the
/// /public + /ogc/styles surfaces working).</item>
/// </list>
/// The existing <see cref="ConsoleServerBindingFailClosedTests"/> locks the binding-handler chokepoint in
/// isolation; this locks the composed IHttpClientFactory surface the refactor introduces.
/// </summary>
public sealed class ConsoleServerBoundClientFactoryFailClosedTests
{
    [Fact]
    public async Task ServerBoundClient_UnresolvedOperator_HardDenies_NoUsableAnonymousIdentity()
    {
        var context = new SwitchableOperatorContext { CurrentOperatorKey = ConsoleOperatorContext.AnonymousKey };
        using var harness = Harness.Build(context);
        var factory = harness.Provider.GetRequiredService<IHonuaServerBoundClientFactory>();

        using var client = factory.CreateServerBoundClient(new Uri("https://startup.honua.test/"));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/catalog/items");
        request.Headers.TryAddWithoutValidation("X-API-Key", "admin-key");

        // The __anonymous__ partition cannot act with honua-server privileges: the guard throws before the
        // binding handler can attach any credential or retarget the request.
        await Assert.ThrowsAsync<ConsoleOperatorContextUnresolvedException>(
            async () => await client.SendAsync(request));
    }

    [Fact]
    public async Task ServerBoundClient_ForwardsEachOperatorsOwnBearer_AndRetargets()
    {
        var context = new SwitchableOperatorContext();
        using var harness = Harness.Build(context);
        var factory = harness.Provider.GetRequiredService<IHonuaServerBoundClientFactory>();

        await BindOperatorAsync(harness, context, "operator-a", "https://a.honua.test/", "bearer-A");
        await BindOperatorAsync(harness, context, "operator-b", "https://b.honua.test/", "bearer-B");

        context.CurrentOperatorKey = "operator-a";
        var a = await SendAsync(factory.CreateServerBoundClient(new Uri("https://startup.honua.test/")));
        Assert.Equal("Bearer bearer-A", a.Authorization);
        Assert.Null(a.ApiKey);
        Assert.Equal("a.honua.test", a.Host);

        context.CurrentOperatorKey = "operator-b";
        var b = await SendAsync(factory.CreateServerBoundClient(new Uri("https://startup.honua.test/")));
        Assert.Equal("Bearer bearer-B", b.Authorization);
        Assert.Null(b.ApiKey);
        Assert.Equal("b.honua.test", b.Host);
    }

    [Fact]
    public async Task PublicClient_UnresolvedOperator_ToleratesAnonymous_AndStripsAdminKey()
    {
        var context = new SwitchableOperatorContext { CurrentOperatorKey = ConsoleOperatorContext.AnonymousKey };
        using var harness = Harness.Build(context);
        var factory = harness.Provider.GetRequiredService<IHonuaServerBoundClientFactory>();

        // The public /public + /ogc/styles surface must NOT fail closed: an anonymous read is allowed and
        // keeps the documented admin-key fallback on the configured server (never impersonating an operator).
        var anon = await SendAsync(factory.CreatePublicClient(new Uri("https://startup.honua.test/")));
        Assert.Null(anon.Authorization);
        Assert.Null(anon.ApiKey);
        Assert.Equal("startup.honua.test", anon.Host);
    }

    [Fact]
    public async Task ConcurrentInteractiveCircuits_ThroughFactory_StayIsolated_NoBearerBleed()
    {
        // The refactor must hold under the REAL circuit resolution path: two interactive circuits (no
        // HttpContext) whose operator identity comes only from their own AuthenticationStateProvider via the
        // production CircuitOperatorContextHandler ambient. Each circuit, inside an inbound activity, makes a
        // call through the shared factory; each must carry its OWN bearer with no cross-operator bleed.
        var context = new ConsoleOperatorContext(new NullHttpContextAccessor());
        using var harness = Harness.Build(context);
        var factory = harness.Provider.GetRequiredService<IHonuaServerBoundClientFactory>();

        var circuitA = new CircuitOperatorContextHandler(AuthState("operator-a"));
        var circuitB = new CircuitOperatorContextHandler(AuthState("operator-b"));

        // Seed each operator's partition (from within its circuit, as a signed-in component would).
        await RunInboundActivityAsync(circuitA, () => BindActiveAsync(harness, "operator-a", "https://a.honua.test/", "bearer-A"));
        await RunInboundActivityAsync(circuitB, () => BindActiveAsync(harness, "operator-b", "https://b.honua.test/", "bearer-B"));

        // Hammer both circuits concurrently to surface any shared/ambient cross-talk.
        var aTasks = Enumerable.Range(0, 40).Select(_ => RunInboundActivityAsync(circuitA,
            () => SendAsync(factory.CreateServerBoundClient(new Uri("https://startup.honua.test/")))));
        var bTasks = Enumerable.Range(0, 40).Select(_ => RunInboundActivityAsync(circuitB,
            () => SendAsync(factory.CreateServerBoundClient(new Uri("https://startup.honua.test/")))));

        var aResults = await Task.WhenAll(aTasks);
        var bResults = await Task.WhenAll(bTasks);

        Assert.All(aResults, r =>
        {
            Assert.Equal("Bearer bearer-A", r.Authorization);
            Assert.Equal("a.honua.test", r.Host);
        });
        Assert.All(bResults, r =>
        {
            Assert.Equal("Bearer bearer-B", r.Authorization);
            Assert.Equal("b.honua.test", r.Host);
        });
    }

    [Fact]
    public async Task OutsideAnyCircuitActivity_ServerBoundCall_FailsClosed()
    {
        // A stray server-bound call with no request and no active circuit activity (the ambient cleared)
        // must not linger as some operator's identity: it fails closed rather than acquiring the admin key.
        var context = new ConsoleOperatorContext(new NullHttpContextAccessor());
        using var harness = Harness.Build(context);
        var factory = harness.Provider.GetRequiredService<IHonuaServerBoundClientFactory>();

        using var client = factory.CreateServerBoundClient(new Uri("https://startup.honua.test/"));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/catalog/items");

        await Assert.ThrowsAsync<ConsoleOperatorContextUnresolvedException>(
            async () => await client.SendAsync(request));
    }

    // ---- helpers ---------------------------------------------------------------------------------------

    private static async Task BindOperatorAsync(
        Harness harness, SwitchableOperatorContext context, string operatorKey, string serverBaseUri, string bearer)
    {
        context.CurrentOperatorKey = operatorKey;
        await BindActiveAsync(harness, operatorKey, serverBaseUri, bearer);
    }

    [Theory]
    [InlineData("uri")]
    [InlineData("profile")]
    [InlineData("principal")]
    public async Task StyleAuthorityPinRejectsProfileChangeBeforeTransport(string changed)
    {
        var context = new SwitchableOperatorContext { CurrentOperatorKey = "operator" };
        using var harness = Harness.Build(context);
        await BindActiveAsync(harness, "operator", "https://a.honua.test/", "bearer-A");
        var factory = harness.Provider.GetRequiredService<IHonuaServerBoundClientFactory>();
        using var client = factory.CreatePublicClient(new Uri("https://startup.honua.test/"));
        using var read = new HttpRequestMessage(HttpMethod.Get, "/ogc/styles/shared");
        read.Options.Set(ConsoleAuthorityRequestOptions.Capture, true);
        using var observed = await client.SendAsync(read);
        Assert.Equal(HttpStatusCode.OK, observed.StatusCode);
        Assert.True(read.Options.TryGetValue(ConsoleAuthorityRequestOptions.Resolved, out var binding));
        if (changed == "principal") { context.CurrentOperatorKey = "other"; await BindActiveAsync(harness, "other", "https://a.honua.test/", "bearer-B"); }
        else if (changed == "profile")
        {
            await harness.Profiles.UpsertProfileAsync(new ConsoleEnvironmentProfile { Id = "other-profile", ServerBaseUri = new Uri("https://a.honua.test/") });
            await harness.Profiles.ActivateProfileAsync("other-profile");
        }
        else await BindActiveAsync(harness, "operator", "https://b.honua.test/", "bearer-B");
        using var write = new HttpRequestMessage(HttpMethod.Put, "/ogc/styles/shared");
        write.Options.Set(ConsoleAuthorityRequestOptions.Expected, binding!);
        using var rejected = await client.SendAsync(write);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Null(Header(rejected, "X-Captured-Host"));
        Assert.False(await harness.Provider.GetRequiredService<IConsoleAuthorityBindingValidator>().IsCurrentAsync(binding!));
    }

    [Fact]
    public async Task CapturedAuthorityPinsFirstReadAndRetryToOriginalProfilePath()
    {
        var context = new SwitchableOperatorContext { CurrentOperatorKey = "operator" };
        using var harness = Harness.Build(context);
        await BindActiveAsync(harness, "operator", "https://a.honua.test/honua/", "bearer-A");
        var validator = harness.Provider.GetRequiredService<IConsoleAuthorityBindingValidator>();
        var binding = await validator.CaptureAsync();
        Assert.False(string.IsNullOrWhiteSpace(binding));
        using var client = harness.Provider.GetRequiredService<IHonuaServerBoundClientFactory>().CreatePublicClient(new Uri("https://startup.honua.test/"));
        using var read = new HttpRequestMessage(HttpMethod.Get, "/ogc/styles/shared");
        read.Options.Set(ConsoleAuthorityRequestOptions.Expected, binding!);
        read.Options.Set(ConsoleAuthorityRequestOptions.RelativeStylePath, "ogc/styles/shared");
        using var observed = await client.SendAsync(read);
        Assert.Equal(HttpStatusCode.OK, observed.StatusCode);
        Assert.Equal("/honua/ogc/styles/shared", Header(observed, "X-Captured-Path"));
        Assert.Equal("Bearer bearer-A", Header(observed, "X-Captured-Authorization"));
        await BindActiveAsync(harness, "operator", "https://b.honua.test/other/", "bearer-B");
        using var retry = new HttpRequestMessage(HttpMethod.Get, "/ogc/styles/shared");
        retry.Options.Set(ConsoleAuthorityRequestOptions.Expected, binding!);
        retry.Options.Set(ConsoleAuthorityRequestOptions.RelativeStylePath, "ogc/styles/shared");
        using var refused = await client.SendAsync(retry);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Null(Header(refused, "X-Captured-Host"));
        Assert.False(await validator.IsCurrentAsync(binding!));
    }

    [Fact]
    public async Task TypedStyleRouteUsesExactProfilePathBaseAndRejectsChangedPath()
    {
        var context = new SwitchableOperatorContext { CurrentOperatorKey = "operator" };
        using var harness = Harness.Build(context);
        await BindActiveAsync(harness, "operator", "https://a.honua.test/honua/", "bearer-A");
        using var client = harness.Provider.GetRequiredService<IHonuaServerBoundClientFactory>().CreatePublicClient(new Uri("https://startup.honua.test/"));
        using var read = new HttpRequestMessage(HttpMethod.Get, "/ogc/styles/shared");
        read.Options.Set(ConsoleAuthorityRequestOptions.Capture, true);
        read.Options.Set(ConsoleAuthorityRequestOptions.RelativeStylePath, "ogc/styles/shared");
        using var observed = await client.SendAsync(read);
        Assert.Equal(HttpStatusCode.OK, observed.StatusCode);
        Assert.Equal("/honua/ogc/styles/shared", Header(observed, "X-Captured-Path"));
        Assert.True(read.Options.TryGetValue(ConsoleAuthorityRequestOptions.Resolved, out var binding));
        using var impact = new HttpRequestMessage(HttpMethod.Get, "/ogc/styles/shared/impact");
        impact.Options.Set(ConsoleAuthorityRequestOptions.Expected, binding!);
        impact.Options.Set(ConsoleAuthorityRequestOptions.RelativeStylePath, "ogc/styles/shared/impact");
        using var reviewed = await client.SendAsync(impact);
        Assert.Equal("/honua/ogc/styles/shared/impact", Header(reviewed, "X-Captured-Path"));
        await BindActiveAsync(harness, "operator", "https://a.honua.test/other/", "bearer-B");
        using var write = new HttpRequestMessage(HttpMethod.Put, "/ogc/styles/shared");
        write.Options.Set(ConsoleAuthorityRequestOptions.Expected, binding!);
        write.Options.Set(ConsoleAuthorityRequestOptions.RelativeStylePath, "ogc/styles/shared");
        using var rejected = await client.SendAsync(write);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Null(Header(rejected, "X-Captured-Path"));
    }

    [Fact]
    public async Task TypedPublicStyleListUsesPathBaseWithoutMintingEditorAuthority()
    {
        var context = new SwitchableOperatorContext { CurrentOperatorKey = "operator" };
        using var harness = Harness.Build(context);
        await BindActiveAsync(harness, "operator", "https://a.honua.test/honua/", "bearer-A");
        using var client = harness.Provider.GetRequiredService<IHonuaServerBoundClientFactory>().CreatePublicClient(new Uri("https://startup.honua.test/"));
        using var read = new HttpRequestMessage(HttpMethod.Get, "/ogc/styles");
        read.Options.Set(ConsoleAuthorityRequestOptions.RelativeStylePath, "ogc/styles");
        using var response = await client.SendAsync(read);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/honua/ogc/styles", Header(response, "X-Captured-Path"));
        Assert.False(read.Options.TryGetValue(ConsoleAuthorityRequestOptions.Resolved, out _));
    }

    [Theory]
    [InlineData("../ogc/styles/shared")]
    [InlineData("ogc/styles/%2E%2E")]
    [InlineData("ogc/styles/a%2Fb")]
    [InlineData("ogc/styles/shared?url=https://other.test")]
    [InlineData("ogc/styles/shared/impact/extra")]
    public async Task HostStylePathOptionCannotBroadenTheCanonicalRoute(string path)
    {
        var context = new SwitchableOperatorContext { CurrentOperatorKey = "operator" };
        using var harness = Harness.Build(context);
        await BindActiveAsync(harness, "operator", "https://a.honua.test/honua/", "bearer-A");
        using var client = harness.Provider.GetRequiredService<IHonuaServerBoundClientFactory>().CreatePublicClient(new Uri("https://startup.honua.test/"));
        using var read = new HttpRequestMessage(HttpMethod.Get, "/ogc/styles/shared");
        read.Options.Set(ConsoleAuthorityRequestOptions.Capture, true);
        read.Options.Set(ConsoleAuthorityRequestOptions.RelativeStylePath, path);
        using var rejected = await client.SendAsync(read);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Null(Header(rejected, "X-Captured-Path"));
    }

    [Fact]
    public async Task CapturedStyleResponseIsRejectedAfterAuthorityChangesDuringRequest()
    {
        var context = new SwitchableOperatorContext { CurrentOperatorKey = "operator" };
        Harness? active = null;
        using var harness = Harness.Build(context, async () => await BindActiveAsync(active!, "operator", "https://b.honua.test/", "bearer-B"));
        active = harness;
        await BindActiveAsync(harness, "operator", "https://a.honua.test/", "bearer-A");
        using var client = harness.Provider.GetRequiredService<IHonuaServerBoundClientFactory>().CreatePublicClient(new Uri("https://startup.honua.test/"));
        using var read = new HttpRequestMessage(HttpMethod.Get, "/ogc/styles/shared");
        read.Options.Set(ConsoleAuthorityRequestOptions.Capture, true);
        using var result = await client.SendAsync(read);
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        Assert.Null(Header(result, "X-Captured-Host"));
    }

    [Fact]
    public async Task PublicCatalogCannotSupplyAnonymousEditorAuthority()
    {
        var context = new SwitchableOperatorContext { CurrentOperatorKey = ConsoleOperatorContext.AnonymousKey };
        using var harness = Harness.Build(context);
        using var client = harness.Provider.GetRequiredService<IHonuaServerBoundClientFactory>().CreatePublicClient(new Uri("https://startup.honua.test/"));
        using var read = new HttpRequestMessage(HttpMethod.Get, "/ogc/styles/shared");
        read.Options.Set(ConsoleAuthorityRequestOptions.Capture, true);
        using var result = await client.SendAsync(read);
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
        Assert.False(read.Options.TryGetValue(ConsoleAuthorityRequestOptions.Resolved, out _));
    }

    private static async Task BindActiveAsync(Harness harness, string operatorKey, string serverBaseUri, string bearer)
    {
        await harness.Profiles.UpsertProfileAsync(new ConsoleEnvironmentProfile
        {
            Id = "env-shared",
            DisplayName = operatorKey,
            ServerBaseUri = new Uri(serverBaseUri),
            Account = new ConsoleAccountBinding
            {
                AuthMode = ConsoleAccountAuthMode.AccountRbac,
                AccountId = operatorKey,
            },
        });
        await harness.Profiles.ActivateProfileAsync("env-shared");
        await harness.Sessions.SaveSessionAsync(new ConsoleAccountSession
        {
            ProfileId = "env-shared",
            AccountId = operatorKey,
            AccessToken = bearer,
            ServerBaseUri = new Uri(serverBaseUri),
        });
    }

    private static async Task<CapturedRequest> SendAsync(HttpClient client)
    {
        using (client)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/catalog/items");
            // The inner typed client always stamps the shared admin key; the binding handler must replace it
            // with the operator bearer when one is resolvable.
            request.Headers.TryAddWithoutValidation("X-API-Key", "admin-key");
            using var response = await client.SendAsync(request);
            return new CapturedRequest
            {
                Authorization = Header(response, "X-Captured-Authorization"),
                ApiKey = Header(response, "X-Captured-ApiKey"),
                Host = Header(response, "X-Captured-Host"),
            };
        }
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static async Task RunInboundActivityAsync(CircuitOperatorContextHandler handler, Func<Task> work)
    {
        var pipeline = handler.CreateInboundActivityHandler(async _ => await work().ConfigureAwait(false));
        await pipeline(null!).ConfigureAwait(false);
    }

    private static async Task<T> RunInboundActivityAsync<T>(CircuitOperatorContextHandler handler, Func<Task<T>> work)
    {
        T result = default!;
        var pipeline = handler.CreateInboundActivityHandler(async _ => result = await work().ConfigureAwait(false));
        await pipeline(null!).ConfigureAwait(false);
        return result;
    }

    private static AuthenticationStateProvider AuthState(string operatorId) =>
        new StubAuthenticationStateProvider(new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, operatorId),
                new Claim(ClaimTypes.Name, operatorId),
            ],
            authenticationType: ConsoleAuthConstants.CookieScheme)));

    private sealed class Harness : IDisposable
    {
        public required ServiceProvider Provider { get; init; }
        public required IConsoleEnvironmentProfileStore Profiles { get; init; }
        public required IConsoleAccountSessionStore Sessions { get; init; }

        public static Harness Build(IConsoleOperatorContext context, Func<Task>? beforeResponse = null)
        {
            var profiles = new OperatorScopedEnvironmentProfileStore(context);
            var sessions = new OperatorScopedAccountSessionStore(context);

            var services = new ServiceCollection();
            services.AddSingleton(context);
            services.AddSingleton<IConsoleEnvironmentProfileStore>(profiles);
            services.AddSingleton<IConsoleAccountSessionStore>(sessions);

            // The production registration (named clients + guard + binding + factory) ...
            services.AddConsoleServerBoundClients();
            // ... with the pooled primary handler swapped for a capturing one that echoes what reached the
            // wire back as response headers (race-free under concurrency).
            services.AddHttpClient(ConsoleServerBoundClients.ServerBoundClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new CapturingHandler(beforeResponse));
            services.AddHttpClient(ConsoleServerBoundClients.PublicClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new CapturingHandler(beforeResponse));

            var provider = services.BuildServiceProvider();
            return new Harness { Provider = provider, Profiles = profiles, Sessions = sessions };
        }

        public void Dispose() => Provider.Dispose();
    }

    private sealed class CapturedRequest
    {
        public string? Authorization { get; init; }
        public string? ApiKey { get; init; }
        public string? Host { get; init; }
    }

    private sealed class CapturingHandler(Func<Task>? beforeResponse = null) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.TryAddWithoutValidation("X-Captured-Path", request.RequestUri?.AbsolutePath);
            if (request.Headers.Authorization is { } auth)
            {
                response.Headers.TryAddWithoutValidation("X-Captured-Authorization", auth.ToString());
            }

            if (request.Headers.TryGetValues("X-API-Key", out var apiKey))
            {
                response.Headers.TryAddWithoutValidation("X-Captured-ApiKey", string.Join(",", apiKey));
            }

            if (request.RequestUri?.Host is { } host)
            {
                response.Headers.TryAddWithoutValidation("X-Captured-Host", host);
            }

            if (beforeResponse is not null) await beforeResponse();
            return response;
        }
    }

    private sealed class StubAuthenticationStateProvider : AuthenticationStateProvider
    {
        private readonly AuthenticationState _state;
        public StubAuthenticationStateProvider(ClaimsPrincipal user) => _state = new AuthenticationState(user);
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(_state);
    }

    private sealed class NullHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get => null; set { } }
    }

    private sealed class SwitchableOperatorContext : IConsoleOperatorContext
    {
        public string CurrentOperatorKey { get; set; } = ConsoleOperatorContext.AnonymousKey;

        public bool HasOperator =>
            !string.Equals(CurrentOperatorKey, ConsoleOperatorContext.AnonymousKey, StringComparison.Ordinal);

        public string RequireOperatorKey() =>
            HasOperator ? CurrentOperatorKey : throw new ConsoleOperatorContextUnresolvedException();
    }
}
