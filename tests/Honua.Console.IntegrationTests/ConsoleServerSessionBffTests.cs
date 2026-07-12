using System.Net;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Security;
using Honua.Console.Shell.Services;
using Honua.Console.Web.Auth;

namespace Honua.Console.IntegrationTests;

public sealed class ConsoleServerSessionBffTests
{
    private static readonly Uri ServerOrigin = new("https://server.example/");

    [Fact]
    public async Task SessionStore_SameProfileAcrossOperators_IsolatesCookieJars()
    {
        await using var store = new ConsoleServerSessionClientStore(TimeProvider.System);

        var operatorA = store.GetOrCreate("operator-a", Profile("shared"));
        var operatorB = store.GetOrCreate("operator-b", Profile("shared"));
        operatorA.Cookies.SetCookies(ServerOrigin, "honua_admin_session=session-a; Path=/; HttpOnly");
        operatorB.Cookies.SetCookies(ServerOrigin, "honua_admin_session=session-b; Path=/; HttpOnly");

        Assert.NotSame(operatorA.Cookies, operatorB.Cookies);
        Assert.Contains("session-a", operatorA.Cookies.GetCookieHeader(ServerOrigin), StringComparison.Ordinal);
        Assert.DoesNotContain("session-b", operatorA.Cookies.GetCookieHeader(ServerOrigin), StringComparison.Ordinal);
        Assert.Contains("session-b", operatorB.Cookies.GetCookieHeader(ServerOrigin), StringComparison.Ordinal);
        Assert.DoesNotContain("session-a", operatorB.Cookies.GetCookieHeader(ServerOrigin), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PendingState_WrongOperatorCannotConsumeOrDeleteOwnersFlow()
    {
        await using var store = new ConsoleServerSessionClientStore(TimeProvider.System);
        var pending = new ConsoleServerAuthPendingFlow(
            "state-a",
            "operator-a",
            "env-a",
            "oidc",
            ServerOrigin,
            "/operate",
            DateTimeOffset.UtcNow.AddMinutes(5));
        store.RegisterPending(pending);

        Assert.False(store.TryConsumePending("state-a", "operator-b", out _));
        Assert.True(store.TryConsumePending("state-a", "operator-a", out var consumed));
        Assert.Equal(pending, consumed);
        Assert.False(store.TryConsumePending("state-a", "operator-a", out _));
    }

    [Fact]
    public async Task PendingState_AfterExpiry_IsRejectedAndRemoved()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-07-10T10:00:00Z"));
        await using var store = new ConsoleServerSessionClientStore(clock);
        store.RegisterPending(new ConsoleServerAuthPendingFlow(
            "state-a",
            "operator-a",
            "env-a",
            "oidc",
            ServerOrigin,
            "/operate",
            clock.GetUtcNow().AddMinutes(5)));

        clock.Advance(TimeSpan.FromMinutes(6));

        Assert.False(store.TryConsumePending("state-a", "operator-a", out _));
    }

    [Fact]
    public async Task SessionStore_NewProcessInstance_DiscardsCookiesAndPendingState()
    {
        await using (var first = new ConsoleServerSessionClientStore(TimeProvider.System))
        {
            first.GetOrCreate("operator-a", Profile("env")).Cookies.SetCookies(
                ServerOrigin,
                "honua_admin_session=session-a; Path=/; HttpOnly");
            first.RegisterPending(new ConsoleServerAuthPendingFlow(
                "state-a",
                "operator-a",
                "env",
                "oidc",
                ServerOrigin,
                "/operate",
                DateTimeOffset.UtcNow.AddMinutes(5)));
        }

        await using var restarted = new ConsoleServerSessionClientStore(TimeProvider.System);
        Assert.Empty(restarted.GetOrCreate("operator-a", Profile("env")).Cookies.GetCookieHeader(ServerOrigin));
        Assert.False(restarted.TryConsumePending("state-a", "operator-a", out _));
    }

    [Fact]
    public async Task BeginSignIn_MissingProfile_PreservesSanitizedReturnTarget()
    {
        var context = new SwitchableOperatorContext("operator-a");
        var profiles = new OperatorScopedEnvironmentProfileStore(context);
        var sessions = new OperatorScopedAccountSessionStore(context);
        await using var serverSessions = new ConsoleServerSessionClientStore(TimeProvider.System);
        var coordinator = new ConsoleServerSessionBffCoordinator(
            context,
            profiles,
            sessions,
            serverSessions,
            TimeProvider.System);

        var result = await coordinator.BeginSignInAsync("missing", providerKey: null, "/operate/copilot");

        Assert.Equal(ConsoleServerSignInStatus.Unavailable, result.Status);
        Assert.Equal("/operate/copilot", result.RedirectUri);
    }

    [Fact]
    public async Task CompleteSignIn_TwoOperatorsAndProfiles_NeverCrossCookiesOrBearers()
    {
        var context = new SwitchableOperatorContext("operator-a");
        var profiles = new OperatorScopedEnvironmentProfileStore(context);
        var sessions = new OperatorScopedAccountSessionStore(context);
        await using var serverSessions = new ConsoleServerSessionClientStore(
            TimeProvider.System,
            static (key, cookies) => new SimulatedServerAuthHandler(key, cookies));
        var coordinator = new ConsoleServerSessionBffCoordinator(
            context,
            profiles,
            sessions,
            serverSessions,
            TimeProvider.System);

        await SeedOperatorAsync(context, profiles, sessions, "operator-a", "env-a");
        await SeedOperatorAsync(context, profiles, sessions, "operator-b", "env-b");

        context.OperatorKey = "operator-a";
        var beginA = await coordinator.BeginSignInAsync("env-a", providerKey: null, "/operate/copilot");
        context.OperatorKey = "operator-b";
        var beginB = await coordinator.BeginSignInAsync("env-b", providerKey: null, "/operate/health");

        context.OperatorKey = "operator-a";
        var completeA = await coordinator.CompleteSignInAsync("code-a", State(beginA.RedirectUri), error: null);
        context.OperatorKey = "operator-b";
        var completeB = await coordinator.CompleteSignInAsync("code-b", State(beginB.RedirectUri), error: null);

        Assert.Equal(ConsoleServerSignInStatus.Redirect, completeA.Status);
        Assert.Equal("/operate/copilot", completeA.RedirectUri);
        Assert.Equal(ConsoleServerSignInStatus.Redirect, completeB.Status);
        Assert.Equal("/operate/health", completeB.RedirectUri);

        context.OperatorKey = "operator-a";
        Assert.Equal("bearer-operator-a-env-a", (await sessions.GetSessionAsync("env-a"))!.AccessToken);
        Assert.Null(await sessions.GetSessionAsync("env-b"));
        context.OperatorKey = "operator-b";
        Assert.Equal("bearer-operator-b-env-b", (await sessions.GetSessionAsync("env-b"))!.AccessToken);
        Assert.Null(await sessions.GetSessionAsync("env-a"));
    }

    [Fact]
    public async Task CompleteSignIn_SameOperatorSwitchingProfiles_KeepsPartitionsIsolated()
    {
        var context = new SwitchableOperatorContext("operator-a");
        var profiles = new OperatorScopedEnvironmentProfileStore(context);
        var sessions = new OperatorScopedAccountSessionStore(context);
        await using var serverSessions = new ConsoleServerSessionClientStore(
            TimeProvider.System,
            static (key, cookies) => new SimulatedServerAuthHandler(key, cookies));
        var coordinator = new ConsoleServerSessionBffCoordinator(
            context,
            profiles,
            sessions,
            serverSessions,
            TimeProvider.System);

        await SeedOperatorAsync(context, profiles, sessions, "operator-a", "env-a");
        await SeedOperatorAsync(context, profiles, sessions, "operator-a", "env-b");

        var beginA = await coordinator.BeginSignInAsync("env-a", providerKey: null, "/operate/a");
        var completeA = await coordinator.CompleteSignInAsync("code-a", State(beginA.RedirectUri), error: null);
        var beginB = await coordinator.BeginSignInAsync("env-b", providerKey: null, "/operate/b");
        var completeB = await coordinator.CompleteSignInAsync("code-b", State(beginB.RedirectUri), error: null);

        Assert.Equal(ConsoleServerSignInStatus.Redirect, completeA.Status);
        Assert.Equal(ConsoleServerSignInStatus.Redirect, completeB.Status);
        Assert.Equal("bearer-operator-a-env-a", (await sessions.GetSessionAsync("env-a"))!.AccessToken);
        Assert.Equal("bearer-operator-a-env-b", (await sessions.GetSessionAsync("env-b"))!.AccessToken);
        Assert.True(serverSessions.TryGet("operator-a", Profile("env-a"), out var partitionA));
        Assert.True(serverSessions.TryGet("operator-a", Profile("env-b"), out var partitionB));
        Assert.NotSame(partitionA, partitionB);
        Assert.DoesNotContain(
            "session-operator-a-env-b",
            partitionA!.Cookies.GetCookieHeader(ServerOrigin),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "session-operator-a-env-a",
            partitionB!.Cookies.GetCookieHeader(ServerOrigin),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteSignIn_UpstreamDenial_DoesNotStoreBearer()
    {
        var context = new SwitchableOperatorContext("operator-a");
        var profiles = new OperatorScopedEnvironmentProfileStore(context);
        var sessions = new OperatorScopedAccountSessionStore(context);
        await using var serverSessions = new ConsoleServerSessionClientStore(
            TimeProvider.System,
            static (key, cookies) => new SimulatedServerAuthHandler(key, cookies, rejectToken: true));
        var coordinator = new ConsoleServerSessionBffCoordinator(
            context,
            profiles,
            sessions,
            serverSessions,
            TimeProvider.System);
        await SeedOperatorAsync(context, profiles, sessions, "operator-a", "env");

        var begin = await coordinator.BeginSignInAsync("env", providerKey: null, "/operate");
        var complete = await coordinator.CompleteSignInAsync("rejected-code", State(begin.RedirectUri), error: null);

        Assert.Equal(ConsoleServerSignInStatus.Denied, complete.Status);
        Assert.True(ConsoleAuthConstants.IsSessionSentinel((await sessions.GetSessionAsync("env"))!.AccessToken));
    }

    [Fact]
    public async Task ClearOperator_RemovesOnlyThatOperatorsServerSessions()
    {
        await using var store = new ConsoleServerSessionClientStore(TimeProvider.System);
        var a = store.GetOrCreate("operator-a", Profile("env"));
        var b = store.GetOrCreate("operator-b", Profile("env"));
        a.Cookies.SetCookies(ServerOrigin, "honua_admin_session=session-a; Path=/; HttpOnly");
        b.Cookies.SetCookies(ServerOrigin, "honua_admin_session=session-b; Path=/; HttpOnly");

        store.ClearOperator("operator-a");

        var newA = store.GetOrCreate("operator-a", Profile("env"));
        var sameB = store.GetOrCreate("operator-b", Profile("env"));
        Assert.Empty(newA.Cookies.GetCookieHeader(ServerOrigin));
        Assert.Contains("session-b", sameB.Cookies.GetCookieHeader(ServerOrigin), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignOut_ClearsOnlyCurrentOperatorsProfilesBearersAndCookieJars()
    {
        var context = new SwitchableOperatorContext("operator-a");
        var profiles = new OperatorScopedEnvironmentProfileStore(context);
        var sessions = new OperatorScopedAccountSessionStore(context);
        await using var serverSessions = new ConsoleServerSessionClientStore(
            TimeProvider.System,
            static (key, cookies) => new SimulatedServerAuthHandler(key, cookies));
        var coordinator = new ConsoleServerSessionBffCoordinator(
            context,
            profiles,
            sessions,
            serverSessions,
            TimeProvider.System);
        await SeedOperatorAsync(context, profiles, sessions, "operator-a", "env");
        context.OperatorKey = "operator-a";
        await sessions.SaveSessionAsync((await sessions.GetSessionAsync("env"))! with { AccessToken = "bearer-a" });
        serverSessions.GetOrCreate("operator-a", Profile("env")).Cookies.SetCookies(
            ServerOrigin,
            "honua_admin_session=session-a; Path=/; HttpOnly");

        await SeedOperatorAsync(context, profiles, sessions, "operator-b", "env");
        context.OperatorKey = "operator-b";
        await sessions.SaveSessionAsync((await sessions.GetSessionAsync("env"))! with { AccessToken = "bearer-b" });
        serverSessions.GetOrCreate("operator-b", Profile("env")).Cookies.SetCookies(
            ServerOrigin,
            "honua_admin_session=session-b; Path=/; HttpOnly");

        context.OperatorKey = "operator-a";
        await coordinator.SignOutAsync();

        Assert.Null(await sessions.GetSessionAsync("env"));
        Assert.Empty(serverSessions.GetOrCreate("operator-a", Profile("env")).Cookies.GetCookieHeader(ServerOrigin));
        context.OperatorKey = "operator-b";
        Assert.Equal("bearer-b", (await sessions.GetSessionAsync("env"))!.AccessToken);
        Assert.Contains(
            "session-b",
            serverSessions.GetOrCreate("operator-b", Profile("env")).Cookies.GetCookieHeader(ServerOrigin),
            StringComparison.Ordinal);
    }

    private static async Task SeedOperatorAsync(
        SwitchableOperatorContext context,
        IConsoleEnvironmentProfileStore profiles,
        IConsoleAccountSessionStore sessions,
        string operatorKey,
        string profileId)
    {
        context.OperatorKey = operatorKey;
        var profile = Profile(profileId);
        await profiles.UpsertProfileAsync(profile);
        await profiles.ActivateProfileAsync(profileId);
        await sessions.SaveSessionAsync(new ConsoleAccountSession
        {
            ProfileId = profileId,
            AccountId = operatorKey,
            AccessToken = ConsoleAuthConstants.SessionSentinelPrefix + profileId
        });
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

    private sealed class SwitchableOperatorContext(string operatorKey) : IConsoleOperatorContext
    {
        public string OperatorKey { get; set; } = operatorKey;

        public string CurrentOperatorKey => OperatorKey;

        public bool HasOperator => !string.IsNullOrWhiteSpace(OperatorKey);

        public string RequireOperatorKey() => HasOperator
            ? OperatorKey
            : throw new ConsoleOperatorContextUnresolvedException();
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now += duration;
    }

    private sealed class SimulatedServerAuthHandler(
        ConsoleServerSessionPartitionKey key,
        CookieContainer cookies,
        bool rejectToken = false) : HttpMessageHandler
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
                if (rejectToken)
                {
                    return new HttpResponseMessage(HttpStatusCode.BadRequest);
                }

                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                Assert.Contains($"state-{key.OperatorKey}-{key.ProfileId}", body, StringComparison.Ordinal);
                Assert.Contains($"pending-{key.OperatorKey}-{key.ProfileId}", cookies.GetCookieHeader(ServerOrigin), StringComparison.Ordinal);
                cookies.SetCookies(ServerOrigin, $"honua_admin_session=session-{key.OperatorKey}-{key.ProfileId}; Path=/; HttpOnly");
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (path.EndsWith("/bearer", StringComparison.Ordinal))
            {
                Assert.Contains($"session-{key.OperatorKey}-{key.ProfileId}", cookies.GetCookieHeader(ServerOrigin), StringComparison.Ordinal);
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
