using System.Net;
using System.Security.Claims;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Security;
using Honua.Console.Shell.Services;
using Honua.Console.Web.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Regression coverage for honua-console#306: an edge-forwarded operator (trusted-edge identity headers
/// with NO <c>X-Forwarded-Access-Token</c>) must retain a bearer obtained out-of-band through the
/// server-session BFF (<c>/auth/server/login</c> → <c>/admin/auth/callback</c>, honua-console#305). Before
/// the fix, <see cref="ConsoleEdgeIdentityMiddleware"/> re-ran <see cref="ConsoleOperatorSessionBridge.SyncAsync"/>
/// on every request and, seeing no forwarded token, overwrote the operator's session with the
/// non-forwardable sentinel — wiping the BFF-exchanged bearer on the very next request.
///
/// These tests drive the production stack (real <see cref="ConsoleOperatorContext"/> resolving the operator
/// from <see cref="HttpContext.User"/>, the operator-partitioned stores, the edge middleware, and — for the
/// full flow — the server-session BFF coordinator against a simulated honua-server) to prove the now-supported
/// combination and to lock the #305 isolation/sign-out invariants that must still hold.
/// </summary>
public sealed class ConsoleEdgeIdentityBearerPersistenceTests
{
    private static readonly Uri ServerOrigin = new("https://server.example/");
    private static readonly DateTimeOffset FarFuture = DateTimeOffset.Parse("2099-01-01T00:00:00Z");

    [Fact]
    public async Task EdgeIdentity_NoForwardedToken_PreservesBffExchangedBearerOnSubsequentRequest()
    {
        var accessor = new HttpContextAccessor();
        var operatorContext = new ConsoleOperatorContext(accessor);
        var profiles = new OperatorScopedEnvironmentProfileStore(operatorContext);
        var sessions = new OperatorScopedAccountSessionStore(operatorContext);
        var bridge = new ConsoleOperatorSessionBridge(profiles, sessions);

        // Seed operator-a's partition as the BFF callback would: an active RBAC profile and a real,
        // non-sentinel operator bearer with a known expiry.
        await SeedProfileAndBearerAsync(accessor, profiles, sessions, "operator-a", "bearer-a", FarFuture);

        // Next request: the edge forwards identity headers only (no access token). The middleware rebuilds
        // the edge principal and re-runs SyncAsync.
        var request = EdgeRequest("operator-a");
        accessor.HttpContext = request;
        await InvokeEdgeMiddlewareAsync(request, bridge);

        // The BFF-exchanged bearer (and its expiry) survives the per-request sync.
        var session = await sessions.GetSessionAsync("env");
        Assert.NotNull(session);
        Assert.Equal("bearer-a", session!.AccessToken);
        Assert.Equal(FarFuture, session.AccessTokenExpiresAt);
        Assert.False(ConsoleAuthConstants.IsSessionSentinel(session.AccessToken));
    }

    [Fact]
    public async Task EdgeIdentity_WithForwardedAccessToken_OverridesStoredBearer()
    {
        var accessor = new HttpContextAccessor();
        var operatorContext = new ConsoleOperatorContext(accessor);
        var profiles = new OperatorScopedEnvironmentProfileStore(operatorContext);
        var sessions = new OperatorScopedAccountSessionStore(operatorContext);
        var bridge = new ConsoleOperatorSessionBridge(profiles, sessions);

        await SeedProfileAndBearerAsync(accessor, profiles, sessions, "operator-a", "bearer-a", FarFuture);

        // The edge now supplies an access token: the edge-owned credential wins (current behavior preserved).
        var request = EdgeRequest("operator-a", accessToken: "edge-token");
        accessor.HttpContext = request;
        await InvokeEdgeMiddlewareAsync(request, bridge);

        var session = await sessions.GetSessionAsync("env");
        Assert.NotNull(session);
        Assert.Equal("edge-token", session!.AccessToken);
        // Edge-forwarded tokens carry no Console-visible expiry (edge-managed).
        Assert.Null(session.AccessTokenExpiresAt);
    }

    [Fact]
    public async Task EdgeIdentity_NoPriorBearer_WritesNonForwardableSentinel()
    {
        var accessor = new HttpContextAccessor();
        var operatorContext = new ConsoleOperatorContext(accessor);
        // Seed only an active profile — no prior session (the operator has not run the BFF exchange yet).
        var profiles = new OperatorScopedEnvironmentProfileStore(operatorContext);
        var sessions = new OperatorScopedAccountSessionStore(operatorContext);
        var bridge = new ConsoleOperatorSessionBridge(profiles, sessions);

        var seed = EdgeRequest("operator-a", authenticate: true);
        accessor.HttpContext = seed;
        await profiles.UpsertProfileAsync(Profile("env"));
        await profiles.ActivateProfileAsync("env");

        var request = EdgeRequest("operator-a");
        accessor.HttpContext = request;
        await InvokeEdgeMiddlewareAsync(request, bridge);

        var session = await sessions.GetSessionAsync("env");
        Assert.NotNull(session);
        // Fail-closed base case: signed in for read context, but no forwardable credential.
        Assert.True(ConsoleAuthConstants.IsSessionSentinel(session!.AccessToken));
    }

    [Fact]
    public async Task EdgeIdentity_PreservedBearer_DoesNotLeakAcrossOperators()
    {
        var accessor = new HttpContextAccessor();
        var operatorContext = new ConsoleOperatorContext(accessor);
        var profiles = new OperatorScopedEnvironmentProfileStore(operatorContext);
        var sessions = new OperatorScopedAccountSessionStore(operatorContext);
        var bridge = new ConsoleOperatorSessionBridge(profiles, sessions);

        // operator-a holds a BFF-exchanged bearer; operator-b has never exchanged one.
        await SeedProfileAndBearerAsync(accessor, profiles, sessions, "operator-a", "bearer-a", FarFuture);
        var seedB = EdgeRequest("operator-b", authenticate: true);
        accessor.HttpContext = seedB;
        await profiles.UpsertProfileAsync(Profile("env"));
        await profiles.ActivateProfileAsync("env");

        // operator-b's edge request (no token) must never observe or inherit operator-a's bearer.
        var requestB = EdgeRequest("operator-b");
        accessor.HttpContext = requestB;
        await InvokeEdgeMiddlewareAsync(requestB, bridge);
        var sessionB = await sessions.GetSessionAsync("env");
        Assert.NotNull(sessionB);
        Assert.True(ConsoleAuthConstants.IsSessionSentinel(sessionB!.AccessToken));

        // operator-a's bearer is untouched.
        accessor.HttpContext = EdgeRequest("operator-a", authenticate: true);
        var sessionA = await sessions.GetSessionAsync("env");
        Assert.Equal("bearer-a", sessionA!.AccessToken);
    }

    [Fact]
    public async Task EdgeIdentity_FullBffFlowThenEdgeRequest_RetainsBearerUntilPerOperatorSignOut()
    {
        var accessor = new HttpContextAccessor();
        var operatorContext = new ConsoleOperatorContext(accessor);
        var profiles = new OperatorScopedEnvironmentProfileStore(operatorContext);
        var sessions = new OperatorScopedAccountSessionStore(operatorContext);
        var bridge = new ConsoleOperatorSessionBridge(profiles, sessions);
        await using var serverSessions = new ConsoleServerSessionClientStore(
            TimeProvider.System,
            static (key, cookies) => new SimulatedServerAuthHandler(key, cookies));
        var coordinator = new ConsoleServerSessionBffCoordinator(
            operatorContext,
            profiles,
            sessions,
            serverSessions,
            TimeProvider.System);

        // Two operators run the real BFF exchange; each lands a distinct bearer in its own partition.
        await RunBffExchangeAsync(accessor, profiles, sessions, coordinator, "operator-a");
        await RunBffExchangeAsync(accessor, profiles, sessions, coordinator, "operator-b");

        accessor.HttpContext = EdgeRequest("operator-a", authenticate: true);
        Assert.Equal("bearer-operator-a-env", (await sessions.GetSessionAsync("env"))!.AccessToken);

        // operator-a's subsequent edge request (identity headers, no token) retains the exchanged bearer.
        var edgeRequest = EdgeRequest("operator-a");
        accessor.HttpContext = edgeRequest;
        await InvokeEdgeMiddlewareAsync(edgeRequest, bridge);
        Assert.Equal("bearer-operator-a-env", (await sessions.GetSessionAsync("env"))!.AccessToken);

        // Per-operator sign-out erases operator-a's bearer only; operator-b's is unaffected (#305).
        accessor.HttpContext = EdgeRequest("operator-a", authenticate: true);
        await coordinator.SignOutAsync();
        Assert.Null(await sessions.GetSessionAsync("env"));

        accessor.HttpContext = EdgeRequest("operator-b", authenticate: true);
        Assert.Equal("bearer-operator-b-env", (await sessions.GetSessionAsync("env"))!.AccessToken);
    }

    private static async Task RunBffExchangeAsync(
        HttpContextAccessor accessor,
        IConsoleEnvironmentProfileStore profiles,
        IConsoleAccountSessionStore sessions,
        ConsoleServerSessionBffCoordinator coordinator,
        string operatorKey)
    {
        accessor.HttpContext = EdgeRequest(operatorKey, authenticate: true);
        await profiles.UpsertProfileAsync(Profile("env"));
        await profiles.ActivateProfileAsync("env");
        await sessions.SaveSessionAsync(new ConsoleAccountSession
        {
            ProfileId = "env",
            AccountId = operatorKey,
            AccessToken = ConsoleAuthConstants.SessionSentinelPrefix + "env"
        });

        var begin = await coordinator.BeginSignInAsync("env", providerKey: null, "/operate");
        var complete = await coordinator.CompleteSignInAsync("code", State(begin.RedirectUri), error: null);
        Assert.Equal(ConsoleServerSignInStatus.Redirect, complete.Status);
    }

    private static async Task SeedProfileAndBearerAsync(
        HttpContextAccessor accessor,
        IConsoleEnvironmentProfileStore profiles,
        IConsoleAccountSessionStore sessions,
        string operatorKey,
        string bearer,
        DateTimeOffset expiresAt)
    {
        accessor.HttpContext = EdgeRequest(operatorKey, authenticate: true);
        await profiles.UpsertProfileAsync(Profile("env"));
        await profiles.ActivateProfileAsync("env");
        await sessions.SaveSessionAsync(new ConsoleAccountSession
        {
            ProfileId = "env",
            AccountId = operatorKey,
            AccessToken = bearer,
            AccessTokenExpiresAt = expiresAt
        });
    }

    private static async Task InvokeEdgeMiddlewareAsync(HttpContext context, ConsoleOperatorSessionBridge bridge)
    {
        var middleware = new ConsoleEdgeIdentityMiddleware(
            _ => Task.CompletedTask,
            Options.Create(new ConsoleEdgeAuthOptions { Enabled = true }),
            bridge,
            NullLogger<ConsoleEdgeIdentityMiddleware>.Instance);
        await middleware.InvokeAsync(context);
    }

    // Builds a request carrying the trusted-edge identity headers. When authenticate is true the operator is
    // already resolved on HttpContext.User (used to seed/read a specific operator's partition directly);
    // otherwise User is anonymous so the middleware establishes identity from the headers, as in production.
    private static DefaultHttpContext EdgeRequest(string subject, string? accessToken = null, bool authenticate = false)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[ConsoleAuthConstants.DefaultUserHeader] = subject;
        context.Request.Headers[ConsoleAuthConstants.DefaultEmailHeader] = subject + "@example.test";
        if (!string.IsNullOrEmpty(accessToken))
        {
            context.Request.Headers[ConsoleAuthConstants.DefaultAccessTokenHeader] = accessToken;
        }

        if (authenticate)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, subject),
                    new Claim(ClaimTypes.Name, subject),
                ],
                "EdgeForwarded"));
        }

        return context;
    }

    private static ConsoleEnvironmentProfile Profile(string id) => new()
    {
        Id = id,
        DisplayName = id,
        ServerBaseUri = ServerOrigin,
        Account = new ConsoleAccountBinding
        {
            AuthMode = ConsoleAccountAuthMode.AccountRbac,
            AccountId = "operator"
        }
    };

    private static string State(string redirectUri)
    {
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(redirectUri).Query);
        return query["state"].ToString();
    }

    private sealed class SimulatedServerAuthHandler(
        ConsoleServerSessionPartitionKey key,
        CookieContainer cookies) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/config", StringComparison.Ordinal))
            {
                return Json("""
                    {"oidcEnabled":true,"providers":[{"key":"oidc","displayName":"Company login"}]}
                    """);
            }

            if (path.EndsWith("/authorize-url", StringComparison.Ordinal))
            {
                var state = $"state-{key.OperatorKey}-{key.ProfileId}";
                cookies.SetCookies(ServerOrigin, $"honua_admin_pending=pending-{key.OperatorKey}-{key.ProfileId}; Path=/; HttpOnly");
                return Json($"{{\"authorizeUrl\":\"https://idp.example/authorize?state={state}\"}}");
            }

            if (path.EndsWith("/token", StringComparison.Ordinal))
            {
                cookies.SetCookies(ServerOrigin, $"honua_admin_session=session-{key.OperatorKey}-{key.ProfileId}; Path=/; HttpOnly");
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (path.EndsWith("/bearer", StringComparison.Ordinal))
            {
                await Task.CompletedTask;
                return Json($$"""
                    {"accessToken":"bearer-{{key.OperatorKey}}-{{key.ProfileId}}","tokenType":"Bearer","expiresAt":"2099-01-01T00:00:00Z","expiresIn":300}
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
    }
}
