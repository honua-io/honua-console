using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Security;
using Honua.Console.Shell.Services;
using Honua.Console.Web.Auth;
using Honua.Console.Web;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Honua.Console.IntegrationTests;

public sealed class ConsoleServerSessionIdentityBindingTests
{
    private const string Issuer = "https://cognito-idp.us-west-2.amazonaws.com/pool";
    private static readonly Uri Server = new("https://server.example/");

    [Fact]
    public async Task HostedBinding_RegisteredHost_EnforcesConfiguredIdentityBeforeIssuance()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ConsoleAuthConstants.AuthModeKey] = "EdgeForwarded",
            [ConsoleAuthConstants.EdgeServerSessionIssuerKey] = Issuer
        });
        builder.Services.AddSingleton<IConsoleOperatorContext>(new OperatorContext());
        SimulatedServer? handler = null;
        builder.Services.AddSingleton(new ConsoleServerSessionClientStore(TimeProvider.System, (_, cookies) =>
            handler = new SimulatedServer(cookies) { SessionJson = InvalidSession("subject") }));
        builder.AddConsoleAuthentication();
        builder.AddConsoleServerSessionBff();
        await using var app = builder.Build();
        var profiles = app.Services.GetRequiredService<IConsoleEnvironmentProfileStore>();
        await profiles.UpsertProfileAsync(Profile("env"));
        var coordinator = app.Services.GetRequiredService<ConsoleServerSessionBffCoordinator>();
        var begin = await coordinator.BeginSignInAsync("env", null, "/operate");
        var state = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(begin.RedirectUri).Query)["state"];

        var result = await coordinator.CompleteSignInAsync("code", state, null);

        Assert.Equal(ConsoleServerSignInStatus.Denied, result.Status);
        Assert.DoesNotContain("bearer", handler!.Requests);
    }

    [Fact]
    public async Task HostedBinding_WithoutEdgeAuthentication_RefusesConfiguration()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ConsoleAuthConstants.AuthModeKey] = "Dev",
            [ConsoleAuthConstants.EdgeServerSessionIssuerKey] = Issuer
        });
        builder.AddConsoleAuthentication();
        await using var app = builder.Build();

        Assert.Throws<OptionsValidationException>(() =>
            app.Services.GetRequiredService<IOptions<ConsoleEdgeAuthOptions>>().Value);
    }

    [Fact]
    public async Task Callback_MatchingAuthenticatedCookieSession_IsReadBeforeBearerIssuance()
    {
        await using var harness = await Harness.CreateAsync();
        var result = await harness.SignInAsync();

        Assert.Equal(ConsoleServerSignInStatus.Redirect, result.Status);
        Assert.Equal(new[] { "token", "session", "bearer" }, harness.Handler.Requests);
        Assert.Equal("opaque-bearer-1", (await harness.Sessions.GetSessionAsync("env"))!.AccessToken);
    }

    [Theory]
    [InlineData("subject")]
    [InlineData("issuer")]
    [InlineData("issuer-case")]
    [InlineData("issuer-slash")]
    [InlineData("missing-subject")]
    [InlineData("missing-issuer")]
    [InlineData("conflicting-subject")]
    [InlineData("conflicting-issuer")]
    [InlineData("blank-subject")]
    [InlineData("missing-claims")]
    [InlineData("missing-expiry")]
    [InlineData("expired")]
    [InlineData("unauthenticated")]
    [InlineData("provider")]
    [InlineData("malformed")]
    [InlineData("denied-read")]
    public async Task Callback_InvalidCookieIdentity_DeniesBeforeIssuanceAndDiscardsSession(string failure)
    {
        await using var harness = await Harness.CreateAsync();
        harness.Handler.SessionJson = InvalidSession(failure);
        harness.Handler.DenySessionRead = failure == "denied-read";
        await harness.Sessions.SaveSessionAsync(new ConsoleAccountSession
        {
            ProfileId = "env",
            AccountId = "operator-a",
            AccessToken = "previous-bearer"
        });
        var other = harness.Partitions.GetOrCreate("operator-b", Profile("env"));

        var result = await harness.SignInAsync();

        Assert.Equal(ConsoleServerSignInStatus.Denied, result.Status);
        Assert.DoesNotContain("bearer", harness.Handler.Requests);
        Assert.Null(await harness.Sessions.GetSessionAsync("env"));
        Assert.False(harness.Partitions.TryGet("operator-a", Profile("env"), out _));
        Assert.True(harness.Handler.Disposed);
        Assert.True(harness.Partitions.TryGet("operator-b", Profile("env"), out var remaining));
        Assert.Same(other, remaining);
    }

    [Fact]
    public async Task Refresh_MatchingCookieIdentity_IsRevalidatedBeforeNewBearer()
    {
        await using var harness = await Harness.CreateAsync();
        await harness.SignInAsync();
        await harness.ExpireBearerAsync();
        harness.Handler.Requests.Clear();

        var result = await harness.Bearers.ResolveAsync(Profile("env"));

        Assert.Equal("opaque-bearer-2", result.AccessToken);
        Assert.Equal(new[] { "session", "bearer" }, harness.Handler.Requests);
    }

    [Fact]
    public async Task Refresh_OverlappingCallback_CannotReplaceCookiesBetweenIdentityReadAndIssuance()
    {
        await using var harness = await Harness.CreateAsync();
        await harness.SignInAsync();
        await harness.ExpireBearerAsync();
        var begin = await harness.Coordinator.BeginSignInAsync("env", null, "/operate");
        var state = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(begin.RedirectUri).Query)["state"];
        harness.Handler.Requests.Clear();
        harness.Handler.PauseBearer = true;
        var refresh = harness.Bearers.ResolveAsync(Profile("env"));
        await harness.Handler.BearerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var callback = harness.Coordinator.CompleteSignInAsync("code", state, null);
        try
        {
            Assert.Equal(new[] { "session", "bearer" }, harness.Handler.Requests);
        }
        finally
        {
            harness.Handler.ReleaseBearer.TrySetResult();
        }

        Assert.Equal("opaque-bearer-2", (await refresh).AccessToken);
        Assert.Equal(ConsoleServerSignInStatus.Redirect, (await callback).Status);
        Assert.Equal(new[] { "session", "bearer", "token", "session", "bearer" }, harness.Handler.Requests);
    }

    [Fact]
    public async Task Invalidation_WhileBridgeWriteIsPaused_DoesNotRestoreThePreviousBearer()
    {
        await using var harness = await Harness.CreateAsync();
        await harness.SignInAsync();
        await harness.Profiles.ActivateProfileAsync("env");
        var begin = await harness.Coordinator.BeginSignInAsync("env", null, "/operate");
        var state = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(begin.RedirectUri).Query)["state"];
        harness.Sessions.PauseNextWrite = true;
        var bridge = new ConsoleOperatorSessionBridge(harness.Profiles, harness.Sessions);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "operator-a"), new Claim(ClaimTypes.Name, "Updated display")], "EdgeForwarded"));
        var sync = bridge.SyncAsync(principal);
        await harness.Sessions.WriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            harness.Handler.SessionJson = InvalidSession("subject");
            var callback = await harness.Coordinator.CompleteSignInAsync("code", state, null);
            Assert.Equal(ConsoleServerSignInStatus.Denied, callback.Status);
        }
        finally
        {
            harness.Sessions.ReleaseWrite.TrySetResult();
        }
        await sync;

        Assert.Null(await harness.Sessions.GetSessionAsync("env"));
        Assert.False(harness.Partitions.TryGet("operator-a", Profile("env"), out _));
    }

    [Fact]
    public async Task Invalidation_AfterExchangeBeforeProviderPersistence_DiscardsTheIssuedResult()
    {
        await using var harness = await Harness.CreateAsync();
        await harness.SignInAsync();
        await harness.ExpireBearerAsync();
        var begin = await harness.Coordinator.BeginSignInAsync("env", null, "/operate");
        var state = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(begin.RedirectUri).Query)["state"];
        harness.Sessions.PauseNextWrite = true;
        var refresh = harness.Bearers.ResolveAsync(Profile("env"));
        await harness.Sessions.WriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            harness.Handler.SessionJson = InvalidSession("subject");
            var callback = await harness.Coordinator.CompleteSignInAsync("code", state, null);
            Assert.Equal(ConsoleServerSignInStatus.Denied, callback.Status);
        }
        finally
        {
            harness.Sessions.ReleaseWrite.TrySetResult();
        }

        Assert.Null((await refresh).AccessToken);
        Assert.Null(await harness.Sessions.GetSessionAsync("env"));
        Assert.False(harness.Partitions.TryGet("operator-a", Profile("env"), out _));
    }

    [Fact]
    public async Task Invalidation_AfterCachedReadBeforeReturn_DoesNotReturnTheOldBearer()
    {
        await using var harness = await Harness.CreateAsync();
        await harness.SignInAsync();
        harness.Sessions.PauseNextCapture = true;
        var resolution = harness.Bearers.ResolveAsync(Profile("env"));
        await harness.Sessions.CaptureEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await harness.Sessions.ClearSessionAsync("env");
        harness.Sessions.ReleaseCapture.TrySetResult();

        Assert.Null((await resolution).AccessToken);
        Assert.Null(await harness.Sessions.GetSessionAsync("env"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SessionSnapshot_ClearRejectsEarlierWritesIncludingAnAbsentSession(bool initialSession)
    {
        var store = new InMemoryConsoleAccountSessionStore();
        var session = new ConsoleAccountSession { ProfileId = "env", AccountId = "operator-a", AccessToken = "bearer" };
        if (initialSession)
        {
            await store.SaveSessionAsync(session);
        }
        var snapshot = await store.CaptureSessionAsync("env");
        await store.ClearSessionAsync("env");

        Assert.False(await store.IsCurrentSessionAsync(snapshot));
        Assert.False(await store.TrySaveSessionAsync(snapshot, session));
        Assert.Null(await store.GetSessionAsync("env"));
    }

    [Fact]
    public async Task SessionSnapshot_PartitionEvictionRejectsTheOldStoreSnapshot()
    {
        var context = new OperatorContext();
        var store = new OperatorScopedAccountSessionStore(context);
        var session = new ConsoleAccountSession { ProfileId = "env", AccountId = "operator-a", AccessToken = "bearer" };
        await store.SaveSessionAsync(session);
        var snapshot = await store.CaptureSessionAsync("env");
        store.EvictOperator("operator-a");

        Assert.False(await store.TrySaveSessionAsync(snapshot, session));
        Assert.Null(await store.GetSessionAsync("env"));
    }

    [Theory]
    [InlineData("subject")]
    [InlineData("issuer")]
    [InlineData("provider")]
    [InlineData("missing-subject")]
    public async Task GaApply_ChangedCookieIdentity_IsRefusedBeforeSendingApply(string failure)
    {
        await using var harness = await Harness.CreateAsync();
        await harness.SignInAsync();
        await using var host = await ApplyHost.StartAsync(harness);
        harness.Handler.Requests.Clear();
        harness.Handler.SessionJson = InvalidSession(failure);

        using var response = await host.Client.PostAsync("/api/agent-workspace/ga/plans/plan/apply", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(new[] { "session" }, harness.Handler.Requests);
        Assert.Null(await harness.Sessions.GetSessionAsync("env"));
        Assert.False(harness.Partitions.TryGet("operator-a", Profile("env"), out _));
    }

    [Fact]
    public async Task GaApply_UnconfiguredBinding_PreservesIndependentServerIdentity()
    {
        await using var harness = await Harness.CreateAsync(bindIdentity: false);
        await harness.SignInAsync();
        await using var host = await ApplyHost.StartAsync(harness);
        harness.Handler.Requests.Clear();
        harness.Handler.SessionJson = InvalidSession("subject");

        using var response = await host.Client.PostAsync("/api/agent-workspace/ga/plans/plan/apply", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { "apply" }, harness.Handler.Requests);
        Assert.True(harness.Handler.ApplyUsedCookieOnly);
    }

    [Fact]
    public async Task GaApply_WithoutCookiePartition_RefusesTheCachedBearer()
    {
        await using var harness = await Harness.CreateAsync();
        await harness.SignInAsync();
        await using var host = await ApplyHost.StartAsync(harness);
        harness.Partitions.Remove("operator-a", Profile("env"));
        harness.Handler.Requests.Clear();

        using var response = await host.Client.PostAsync("/api/agent-workspace/ga/plans/plan/apply", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(harness.Handler.Requests);
    }

    [Fact]
    public async Task GaApply_OverlappingCallback_WaitsForTheCheckedCookieIdentity()
    {
        await using var harness = await Harness.CreateAsync();
        await harness.SignInAsync();
        await using var host = await ApplyHost.StartAsync(harness);
        var begin = await harness.Coordinator.BeginSignInAsync("env", null, "/operate");
        var state = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(begin.RedirectUri).Query)["state"];
        harness.Handler.Requests.Clear();
        harness.Handler.PauseToken = true;
        harness.Handler.SessionJson = InvalidSession("subject");
        var callback = harness.Coordinator.CompleteSignInAsync("code", state, null);
        await harness.Handler.TokenEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var apply = host.Client.PostAsync("/api/agent-workspace/ga/plans/plan/apply", new StringContent("{}"));
        try
        {
            Assert.NotSame(apply, await Task.WhenAny(apply, Task.Delay(100)));
            Assert.Equal(new[] { "token" }, harness.Handler.Requests);
            harness.Handler.SessionJson = Session();
        }
        finally
        {
            harness.Handler.ReleaseToken.TrySetResult();
        }

        Assert.Equal(ConsoleServerSignInStatus.Redirect, (await callback).Status);
        using var response = await apply;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { "token", "session", "bearer", "session", "apply" }, harness.Handler.Requests);
        Assert.True(harness.Handler.ApplyUsedCookieOnly);
    }

    [Fact]
    public async Task GaApply_WhileSending_HoldsTheLeaseUntilApplyCompletes()
    {
        await using var harness = await Harness.CreateAsync();
        await harness.SignInAsync();
        await using var host = await ApplyHost.StartAsync(harness);
        var begin = await harness.Coordinator.BeginSignInAsync("env", null, "/operate");
        var state = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(begin.RedirectUri).Query)["state"];
        harness.Handler.Requests.Clear();
        harness.Handler.PauseApply = true;
        var apply = host.Client.PostAsync("/api/agent-workspace/ga/plans/plan/apply", new StringContent("{}"));
        await harness.Handler.ApplyEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var callback = harness.Coordinator.CompleteSignInAsync("code", state, null);
        try
        {
            Assert.NotSame(callback, await Task.WhenAny(callback, Task.Delay(100)));
            Assert.Equal(new[] { "session", "apply" }, harness.Handler.Requests);
        }
        finally
        {
            harness.Handler.ReleaseApply.TrySetResult();
        }

        using var response = await apply;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ConsoleServerSignInStatus.Redirect, (await callback).Status);
        Assert.Equal(new[] { "session", "apply", "token", "session", "bearer" }, harness.Handler.Requests);
    }

    [Theory]
    [InlineData("subject")]
    [InlineData("issuer")]
    [InlineData("provider")]
    [InlineData("missing-subject")]
    [InlineData("conflicting-subject")]
    [InlineData("expired")]
    public async Task Refresh_ChangedCookieIdentity_DiscardsOldBearerAndCookiePartition(string failure)
    {
        await using var harness = await Harness.CreateAsync();
        await harness.SignInAsync();
        await harness.ExpireBearerAsync();
        harness.Handler.Requests.Clear();
        harness.Handler.SessionJson = InvalidSession(failure);

        var result = await harness.Bearers.ResolveAsync(Profile("env"));

        Assert.Null(result.AccessToken);
        Assert.Equal(new[] { "session" }, harness.Handler.Requests);
        Assert.Null(await harness.Sessions.GetSessionAsync("env"));
        Assert.False(harness.Partitions.TryGet("operator-a", Profile("env"), out _));
        Assert.True(harness.Handler.Disposed);
    }

    [Fact]
    public async Task UnconfiguredBinding_PreservesIndependentProfileIdentities()
    {
        await using var harness = await Harness.CreateAsync(bindIdentity: false);
        harness.Handler.SessionJson = InvalidSession("subject");

        var first = await harness.SignInAsync();
        await harness.Profiles.UpsertProfileAsync(Profile("other-env"));
        var second = await harness.SignInAsync("other-env");

        Assert.Equal(ConsoleServerSignInStatus.Redirect, first.Status);
        Assert.Equal(ConsoleServerSignInStatus.Redirect, second.Status);
        Assert.NotNull(await harness.Sessions.GetSessionAsync("env"));
        Assert.NotNull(await harness.Sessions.GetSessionAsync("other-env"));
        Assert.DoesNotContain("session", harness.Handler.Requests);
    }

    [Theory]
    [InlineData("missing-user")]
    [InlineData("multiple-users")]
    [InlineData("missing-trust")]
    public async Task HostedEdge_InvalidCurrentAssertion_DoesNotUseCookieOrEmailFallback(string failure)
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "stale-cookie")], "Cookie"));
        context.Request.Headers[ConsoleAuthConstants.EdgeTrustHeader] = "proxy-secret";
        context.Request.Headers[ConsoleAuthConstants.DefaultUserHeader] = "operator-a";
        context.Request.Headers[ConsoleAuthConstants.DefaultEmailHeader] = "operator-a@example.com";
        if (failure == "missing-user")
        {
            context.Request.Headers.Remove(ConsoleAuthConstants.DefaultUserHeader);
        }
        else if (failure == "multiple-users")
        {
            context.Request.Headers[ConsoleAuthConstants.DefaultUserHeader] = new StringValues(["operator-a", "operator-b"]);
        }
        else
        {
            context.Request.Headers.Remove(ConsoleAuthConstants.EdgeTrustHeader);
        }

        await EdgeMiddleware().InvokeAsync(context);

        Assert.False(context.User.Identity?.IsAuthenticated);
    }

    [Fact]
    public async Task HostedEdge_ValidCurrentAssertion_ReplacesStaleCookieWithRawSubject()
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "stale-cookie")], "Cookie"));
        context.Request.Headers[ConsoleAuthConstants.EdgeTrustHeader] = "proxy-secret";
        context.Request.Headers[ConsoleAuthConstants.DefaultUserHeader] = "operator-a";

        await EdgeMiddleware().InvokeAsync(context);

        Assert.Equal("operator-a", context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.Equal("EdgeForwarded", context.User.Identity?.AuthenticationType);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData(Issuer, true)]
    [InlineData("http://idp.example/pool", false)]
    [InlineData("https://idp.example/pool?", false)]
    [InlineData("https://idp.example/pool#", false)]
    [InlineData("https://user:secret@idp.example/pool", false)]
    [InlineData(" https://idp.example/pool", false)]
    public void IssuerConfiguration_BindsAndValidatesExactDeploymentValue(string? issuer, bool valid)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ConsoleAuthConstants.EdgeServerSessionIssuerKey] = issuer
        }).Build();
        var options = new ConsoleEdgeAuthOptions();
        new ConsoleEdgeAuthOptionsSetup(config).Configure(options);
        Assert.Equal(issuer, options.ServerSessionIssuer);
        Assert.Equal(valid, ConsoleEdgeAuthOptions.IsValidServerSessionIssuer(options));
    }

    private static ConsoleEdgeIdentityMiddleware EdgeMiddleware() => new(
        _ => Task.CompletedTask,
        Options.Create(new ConsoleEdgeAuthOptions
        {
            Enabled = true,
            SharedSecret = "proxy-secret",
            ServerSessionIssuer = Issuer
        }),
        new ConsoleOperatorSessionBridge(new InMemoryConsoleEnvironmentProfileStore([]), new InMemoryConsoleAccountSessionStore()),
        NullLogger<ConsoleEdgeIdentityMiddleware>.Instance);

    private static string Session(
        string subject = "operator-a", string issuer = Issuer, string provider = "oidc",
        bool authenticated = true, string? expires = "2099-01-01T00:00:00Z",
        string? additionalType = null, string? additionalValue = null,
        bool includeSubject = true, bool includeIssuer = true, bool includeClaims = true)
    {
        var claims = new List<object>();
        if (includeSubject)
        {
            claims.Add(new { type = "sub", value = subject });
            claims.Add(new { type = ClaimTypes.NameIdentifier, value = subject });
        }
        if (includeIssuer)
        {
            claims.Add(new { type = "iss", value = issuer });
        }
        if (additionalType is not null)
        {
            claims.Add(new { type = additionalType, value = additionalValue });
        }
        return JsonSerializer.Serialize(new
        {
            isAuthenticated = authenticated,
            providerKey = provider,
            expiresAt = expires,
            claims = includeClaims ? claims : null
        });
    }

    private static string InvalidSession(string failure) => failure switch
    {
        "subject" => Session(subject: "operator-b"),
        "issuer" => Session(issuer: "https://different.example/pool"),
        "issuer-case" => Session(issuer: Issuer.ToUpperInvariant()),
        "issuer-slash" => Session(issuer: Issuer + "/"),
        "missing-subject" => Session(includeSubject: false),
        "missing-issuer" => Session(includeIssuer: false),
        "conflicting-subject" => Session(additionalType: "sub", additionalValue: "operator-b"),
        "conflicting-issuer" => Session(additionalType: "iss", additionalValue: "https://different.example/pool"),
        "blank-subject" => Session(additionalType: "sub", additionalValue: " "),
        "missing-claims" => Session(includeClaims: false),
        "missing-expiry" => Session(expires: null),
        "expired" => Session(expires: "2000-01-01T00:00:00Z"),
        "unauthenticated" => Session(authenticated: false),
        "provider" => Session(provider: "another-provider"),
        "malformed" => "{",
        "denied-read" => Session(),
        _ => throw new ArgumentException("Unknown failure", nameof(failure))
    };

    private static ConsoleEnvironmentProfile Profile(string id) => new()
    {
        Id = id,
        DisplayName = id,
        ServerBaseUri = Server,
        Account = new ConsoleAccountBinding { AuthMode = ConsoleAccountAuthMode.AccountRbac, AccountId = "operator-a" }
    };

    private sealed class OperatorContext : IConsoleOperatorContext
    {
        public string CurrentOperatorKey => "operator-a";
        public bool HasOperator => true;
        public string RequireOperatorKey() => CurrentOperatorKey;
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly OperatorContext _context = new();
        public OperatorScopedEnvironmentProfileStore Profiles { get; }
        public PausingSessionStore Sessions { get; }
        public ConsoleServerSessionClientStore Partitions { get; }
        public SimulatedServer Handler { get; private set; } = null!;
        public ConsoleOperatorBearerProvider Bearers { get; }
        public ConsoleServerSessionBffCoordinator Coordinator { get; }
        public ConsoleServerSessionIdentityBinding Binding { get; }

        private Harness(bool bindIdentity)
        {
            Profiles = new(_context);
            Sessions = new(new OperatorScopedAccountSessionStore(_context));
            Partitions = new(TimeProvider.System, (key, cookies) =>
            {
                var handler = new SimulatedServer(cookies);
                if (key.OperatorKey == "operator-a" && key.ProfileId == "env")
                {
                    Handler = handler;
                }
                return handler;
            });
            Binding = new ConsoleServerSessionIdentityBinding(
                Options.Create(new ConsoleEdgeAuthOptions { ServerSessionIssuer = bindIdentity ? Issuer : null }),
                Sessions, Partitions, TimeProvider.System);
            Coordinator = new(_context, Profiles, Sessions, Partitions, TimeProvider.System, Binding);
            Bearers = new(Sessions, new PartitionedConsoleOperatorBearerExchange(_context, Partitions, Binding), TimeProvider.System);
        }

        public static async Task<Harness> CreateAsync(bool bindIdentity = true)
        {
            var harness = new Harness(bindIdentity);
            await harness.Profiles.UpsertProfileAsync(Profile("env"));
            harness.Partitions.GetOrCreate("operator-a", Profile("env"));
            return harness;
        }

        public async Task<ConsoleServerSignInResult> SignInAsync(string profile = "env")
        {
            var begin = await Coordinator.BeginSignInAsync(profile, null, "/operate");
            var state = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(begin.RedirectUri).Query)["state"];
            return await Coordinator.CompleteSignInAsync("code", state, null);
        }

        public async Task ExpireBearerAsync() => await Sessions.SaveSessionAsync(
            (await Sessions.GetSessionAsync("env"))! with { AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) });

        public ValueTask DisposeAsync() => Partitions.DisposeAsync();
    }

    private sealed class ApplyHost(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client => client;

        public static async Task<ApplyHost> StartAsync(Harness harness)
        {
            await harness.Profiles.ActivateProfileAsync("env");
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Honua:Agent:GaSpecWorkflowEnabled"] = "true" });
            builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-Token");
            builder.Services.AddSingleton<IConsoleEnvironmentProfileStore>(harness.Profiles);
            builder.Services.AddSingleton<IConsoleAccountSessionStore>(harness.Sessions);
            builder.Services.AddSingleton(new ConsoleOperatorSessionBridge(harness.Profiles, harness.Sessions));
            builder.Services.AddSingleton<IConsoleOperatorBearerProvider>(harness.Bearers);
            builder.Services.AddSingleton<IConsoleOperatorContext>(new OperatorContext());
            builder.Services.AddSingleton(harness.Partitions);
            builder.Services.AddSingleton(harness.Binding);
            builder.Services.AddHttpClient();
            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "operator-a")], "EdgeForwarded"));
                await next();
            });
            app.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) => antiforgery.GetAndStoreTokens(context).RequestToken!);
            app.MapAgentWorkspaceBff();
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(address) };
            client.DefaultRequestHeaders.Add("X-CSRF-Token", await client.GetStringAsync("/csrf"));
            client.DefaultRequestHeaders.Add("X-Honua-Environment", "env");
            client.DefaultRequestHeaders.Add("X-Honua-Session-Partition", AgentGatewayBinding.OperatorPartition("operator-a", "env", Server));
            return new(app, client);
        }

        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private sealed class PausingSessionStore(OperatorScopedAccountSessionStore inner)
        : IConsoleAccountSessionStore, IConsoleAccountSessionSnapshotStore
    {
        public bool PauseNextWrite { get; set; }
        public bool PauseNextCapture { get; set; }
        public TaskCompletionSource WriteEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseWrite { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CaptureEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseCapture { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ConsoleAccountSession?> GetSessionAsync(string profileId, CancellationToken cancellationToken = default) =>
            inner.GetSessionAsync(profileId, cancellationToken);

        public async Task SaveSessionAsync(ConsoleAccountSession session, CancellationToken cancellationToken = default)
        {
            await PauseWriteAsync(cancellationToken);
            await inner.SaveSessionAsync(session, cancellationToken);
        }

        public Task ClearSessionAsync(string profileId, CancellationToken cancellationToken = default) =>
            inner.ClearSessionAsync(profileId, cancellationToken);

        public async Task<ConsoleAccountSessionSnapshot> CaptureSessionAsync(string profileId, CancellationToken cancellationToken = default)
        {
            var snapshot = await inner.CaptureSessionAsync(profileId, cancellationToken);
            if (PauseNextCapture)
            {
                PauseNextCapture = false;
                CaptureEntered.TrySetResult();
                await ReleaseCapture.Task.WaitAsync(cancellationToken);
            }
            return snapshot;
        }

        public Task<bool> IsCurrentSessionAsync(ConsoleAccountSessionSnapshot snapshot, CancellationToken cancellationToken = default) =>
            inner.IsCurrentSessionAsync(snapshot, cancellationToken);

        public async Task<bool> TrySaveSessionAsync(ConsoleAccountSessionSnapshot snapshot, ConsoleAccountSession session,
            CancellationToken cancellationToken = default)
        {
            await PauseWriteAsync(cancellationToken);
            return await inner.TrySaveSessionAsync(snapshot, session, cancellationToken);
        }

        private async Task PauseWriteAsync(CancellationToken cancellationToken)
        {
            if (PauseNextWrite)
            {
                PauseNextWrite = false;
                WriteEntered.TrySetResult();
                await ReleaseWrite.Task.WaitAsync(cancellationToken);
            }
        }
    }

    private sealed class SimulatedServer(CookieContainer cookies) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public string SessionJson { get; set; } = Session();
        public bool DenySessionRead { get; set; }
        public bool Disposed { get; private set; }
        private int _issued;
        public bool PauseBearer { get; set; }
        public bool PauseToken { get; set; }
        public bool PauseApply { get; set; }
        public bool ApplyUsedCookieOnly { get; private set; }
        public TaskCompletionSource TokenEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseToken { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ApplyEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseApply { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource BearerEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseBearer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var operation = request.RequestUri!.Segments[^1];
            if (operation == "config")
            {
                return Json("""{"oidcEnabled":true,"providers":[{"key":"oidc"}]}""");
            }
            if (operation == "authorize-url")
            {
                return Json("""{"authorizeUrl":"https://idp.example/authorize?state=state"}""");
            }
            Requests.Add(operation);
            if (operation == "token")
            {
                cookies.SetCookies(Server, "honua_admin_session=server-session; Path=/; HttpOnly");
                if (PauseToken)
                {
                    TokenEntered.TrySetResult();
                    await ReleaseToken.Task.WaitAsync(cancellationToken);
                }
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            Assert.Contains("honua_admin_session=server-session", cookies.GetCookieHeader(Server));
            if (operation == "session")
            {
                return DenySessionRead ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : Json(SessionJson);
            }
            if (operation == "apply")
            {
                ApplyUsedCookieOnly = request.Headers.Authorization is null && !request.Headers.Contains("X-API-Key");
                if (PauseApply)
                {
                    ApplyEntered.TrySetResult();
                    await ReleaseApply.Task.WaitAsync(cancellationToken);
                }
                return Json("{}");
            }
            Assert.Equal("bearer", operation);
            if (PauseBearer)
            {
                BearerEntered.TrySetResult();
                await ReleaseBearer.Task.WaitAsync(cancellationToken);
            }
            return Json($$"""{"accessToken":"opaque-bearer-{{++_issued}}","tokenType":"Bearer","expiresAt":"2099-01-01T00:00:00Z","expiresIn":300}""");
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }

        private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(value, System.Text.Encoding.UTF8, "application/json")
        };
    }
}
