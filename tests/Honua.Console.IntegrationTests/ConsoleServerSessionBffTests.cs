using System.Net;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Security;
using Honua.Console.Shell.Services;
using Honua.Console.Web.Auth;

namespace Honua.Console.IntegrationTests;

public sealed partial class ConsoleServerSessionBffTests
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
    public async Task GetOrCreate_OneOperatorCannotFillTheProcessWideTable()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-10-21T00:00:00Z"));
        await using var store = new ConsoleServerSessionClientStore(
            clock,
            maximumPartitions: 4,
            maximumPartitionsPerOperator: 2);

        store.GetOrCreate("operator-a", Profile("env-1"));
        clock.Advance(TimeSpan.FromSeconds(1));
        store.GetOrCreate("operator-a", Profile("env-2"));
        clock.Advance(TimeSpan.FromSeconds(1));
        store.GetOrCreate("operator-a", Profile("env-3"));

        Assert.False(store.TryGet("operator-a", Profile("env-1"), out _));
        Assert.True(store.TryGet("operator-a", Profile("env-2"), out _));
        Assert.True(store.TryGet("operator-a", Profile("env-3"), out _));

        store.GetOrCreate("operator-b", Profile("env-1"));
        store.GetOrCreate("operator-b", Profile("env-2"));
        var blocked = Assert.Throws<InvalidOperationException>(() => store.GetOrCreate("operator-c", Profile("env-1")));
        Assert.Contains("capacity is exhausted", blocked.Message, StringComparison.Ordinal);
        Assert.True(store.TryGet("operator-a", Profile("env-2"), out _));
    }

    [Fact]
    public async Task BeginSignIn_FailedServerConfig_DoesNotKeepTheNewPartition()
    {
        var context = new SwitchableOperatorContext("operator-a");
        var profiles = new OperatorScopedEnvironmentProfileStore(context);
        var sessions = new OperatorScopedAccountSessionStore(context);
        await using var serverSessions = new ConsoleServerSessionClientStore(
            TimeProvider.System,
            static (_, _) => new FixedStatusHandler(HttpStatusCode.InternalServerError),
            maximumPartitions: 1);
        var coordinator = new ConsoleServerSessionBffCoordinator(
            context,
            profiles,
            sessions,
            serverSessions,
            TimeProvider.System);
        await SeedOperatorAsync(context, profiles, sessions, "operator-a", "env");

        var begin = await coordinator.BeginSignInAsync("env", providerKey: null, "/operate");

        Assert.Equal(ConsoleServerSignInStatus.Unavailable, begin.Status);
        Assert.False(serverSessions.TryGet("operator-a", Profile("env"), out _));
        context.OperatorKey = "operator-b";
        var other = serverSessions.GetOrCreate("operator-b", Profile("env"));
        Assert.NotNull(other);
    }

    [Fact]
    public async Task TryRemoveIfUnadopted_LeavesAPartitionAnotherAttemptObserved()
    {
        await using var store = new ConsoleServerSessionClientStore(TimeProvider.System);
        var createdPartition = store.GetOrCreate("operator-a", Profile("env"), out var created);
        Assert.True(created);
        var observed = store.GetOrCreate("operator-a", Profile("env"), out var observedCreated);
        Assert.False(observedCreated);
        Assert.Same(createdPartition, observed);

        Assert.False(store.TryRemoveIfUnadopted(createdPartition));
        Assert.True(store.TryGet("operator-a", Profile("env"), out var stillThere));
        Assert.Same(createdPartition, stillThere);
    }

    [Fact]
    public async Task TryRemoveIfUnadopted_DropsTheUnobservedPartitionItCreated()
    {
        await using var store = new ConsoleServerSessionClientStore(TimeProvider.System);
        var createdPartition = store.GetOrCreate("operator-a", Profile("env"), out var created);
        Assert.True(created);

        Assert.True(store.TryRemoveIfUnadopted(createdPartition));
        Assert.False(store.TryGet("operator-a", Profile("env"), out _));
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

    [Fact]
    public async Task Exchange_WithoutServerSession_DeniesLocallyWithoutCreatingAnAnonymousPartition()
    {
        var context = new SwitchableOperatorContext("operator-a");
        var partitionsCreated = 0;
        await using var store = new ConsoleServerSessionClientStore(TimeProvider.System, (key, cookies) =>
        {
            partitionsCreated++;
            return new SimulatedServerAuthHandler(key, cookies);
        });
        var exchange = new PartitionedConsoleOperatorBearerExchange(context, store);
        var result = await exchange.ExchangeAsync(Profile("env"));
        Assert.Equal(ConsoleOperatorBearerExchangeStatus.Denied, result.Status);
        Assert.Contains("Sign in", result.Message);
        Assert.Equal(0, partitionsCreated);
        Assert.False(store.TryGet("operator-a", Profile("env"), out _));
    }

    [Fact]
    public async Task ManagementSignIn_UsesItsOwnOriginWithoutReplacingServingCredentials()
    {
        var context = new SwitchableOperatorContext("operator");
        var profile = Profile("environment");
        var profiles = new InMemoryConsoleEnvironmentProfileStore([profile], activeProfileId: profile.Id);
        var accounts = new InMemoryConsoleAccountSessionStore();
        await accounts.SaveSessionAsync(new ConsoleAccountSession
        {
            ProfileId = profile.Id, ServerBaseUri = profile.ServerBaseUri, AccessToken = "serving-cached-bearer"
        });
        var handlers = new List<SimulatedServerAuthHandler>();
        await using var serverSessions = new ConsoleServerSessionClientStore(TimeProvider.System, (key, cookies) =>
        {
            var handler = new SimulatedServerAuthHandler(key, cookies);
            handlers.Add(handler);
            return handler;
        });
        var management = new MutableManagementEndpoint();
        var coordinator = new ConsoleServerSessionBffCoordinator(context, profiles, accounts, serverSessions,
            TimeProvider.System, management: management);

        var begin = await coordinator.BeginAuthoritySignInAsync(
            new ConsoleServerSignInTarget(profile.Id, ConsoleServerAuthority.Management, null), "/inbox");
        var complete = await coordinator.CompleteSignInAsync("code", State(begin.RedirectUri), null);

        Assert.Equal(ConsoleServerSignInStatus.Redirect, complete.Status);
        Assert.Equal("serving-cached-bearer", (await accounts.GetSessionAsync(profile.Id))!.AccessToken);
        Assert.True(serverSessions.TryGet("operator", profile with { ServerBaseUri = management.Target }, out var managerSession));
        Assert.Contains("honua_admin_session=", managerSession!.Cookies.GetCookieHeader(management.Target), StringComparison.Ordinal);
        Assert.False(serverSessions.TryGet("operator", profile, out _));
        Assert.Equal(0, Assert.Single(handlers).BearerCalls);
    }

    [Theory]
    [InlineData("manager")]
    [InlineData("profile")]
    public async Task ManagementSignIn_ChangedBinding_RefusesCallbackBeforeTokenExchange(string change)
    {
        var context = new SwitchableOperatorContext("operator");
        var profile = Profile("environment");
        var profiles = new InMemoryConsoleEnvironmentProfileStore([profile], activeProfileId: profile.Id);
        var accounts = new InMemoryConsoleAccountSessionStore();
        SimulatedServerAuthHandler? handler = null;
        await using var serverSessions = new ConsoleServerSessionClientStore(TimeProvider.System, (key, cookies) =>
            handler = new SimulatedServerAuthHandler(key, cookies));
        var management = new MutableManagementEndpoint();
        var coordinator = new ConsoleServerSessionBffCoordinator(context, profiles, accounts, serverSessions,
            TimeProvider.System, management: management);
        var begin = await coordinator.BeginAuthoritySignInAsync(
            new ConsoleServerSignInTarget(profile.Id, ConsoleServerAuthority.Management, null), "/inbox");
        if (change == "manager") management.Target = new Uri("https://other-manager.example/");
        else await profiles.UpsertProfileAsync(profile with { TenantId = "changed-tenant" });

        var result = await coordinator.CompleteSignInAsync("code", State(begin.RedirectUri), null);

        Assert.Equal(ConsoleServerSignInStatus.Denied, result.Status);
        Assert.Equal(0, handler!.TokenCalls);
    }

    [Fact]
    public async Task PendingState_DifferentAuthorityFlows_DoNotReplaceEachOther()
    {
        await using var store = new ConsoleServerSessionClientStore(TimeProvider.System);
        var serving = new ConsoleServerAuthPendingFlow("serving-state", "operator", "profile", "oidc", ServerOrigin,
            "/inbox", DateTimeOffset.UtcNow.AddMinutes(5));
        var manager = serving with { State = "manager-state", ServerBaseUri = new Uri("https://manager.example/") };

        store.RegisterPending(serving);
        store.RegisterPending(manager);

        Assert.True(store.TryConsumePending(serving.State, "operator", out var originalServing));
        Assert.Equal(serving, originalServing);
        Assert.True(store.TryConsumePending(manager.State, "operator", out var originalManager));
        Assert.Equal(manager, originalManager);
    }

    [Fact]
    public async Task PendingState_AuthorityCollision_PreservesTheOriginalFlow()
    {
        await using var store = new ConsoleServerSessionClientStore(TimeProvider.System);
        var original = new ConsoleServerAuthPendingFlow("same-state", "operator", "profile", "oidc", ServerOrigin,
            "/inbox", DateTimeOffset.UtcNow.AddMinutes(5));
        store.RegisterPending(original);

        Assert.Throws<InvalidOperationException>(() => store.RegisterPending(
            original with { ServerBaseUri = new Uri("https://manager.example/") }));

        Assert.True(store.TryConsumePending(original.State, "operator", out var retained));
        Assert.Equal(original, retained);
    }

    private sealed class MutableManagementEndpoint : IConsoleManagerEndpoint
    {
        public Uri Target { get; set; } = new("https://manager.example/");
        public Uri? Resolve(Uri servingBaseUri) => Target;
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

    private sealed class FixedStatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status));
    }

    private sealed class SimulatedServerAuthHandler(
        ConsoleServerSessionPartitionKey key,
        CookieContainer cookies,
        bool rejectToken = false) : HttpMessageHandler
    {
        private readonly Uri _origin = new(key.ServerOrigin);
        public int TokenCalls { get; private set; }
        public int BearerCalls { get; private set; }

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
                cookies.SetCookies(_origin, $"honua_admin_pending=pending-{key.OperatorKey}-{key.ProfileId}; Path=/; HttpOnly");
                return Json($"{{\"authorizeUrl\":\"https://idp.example/authorize?state={state}\"}}");
            }

            if (path.EndsWith("/token", StringComparison.Ordinal))
            {
                TokenCalls++;
                if (rejectToken)
                {
                    return new HttpResponseMessage(HttpStatusCode.BadRequest);
                }

                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                Assert.Contains($"state-{key.OperatorKey}-{key.ProfileId}", body, StringComparison.Ordinal);
                Assert.Contains($"pending-{key.OperatorKey}-{key.ProfileId}", cookies.GetCookieHeader(_origin), StringComparison.Ordinal);
                cookies.SetCookies(_origin, $"honua_admin_session=session-{key.OperatorKey}-{key.ProfileId}; Path=/; HttpOnly");
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (path.EndsWith("/bearer", StringComparison.Ordinal))
            {
                BearerCalls++;
                Assert.Contains($"session-{key.OperatorKey}-{key.ProfileId}", cookies.GetCookieHeader(_origin), StringComparison.Ordinal);
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
