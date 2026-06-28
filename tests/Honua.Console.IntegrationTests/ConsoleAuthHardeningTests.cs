using System.Security.Claims;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Security;
using Honua.Console.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Regression coverage for the two S1 console-auth defects introduced by the fail-closed operator-auth
/// change (honua-console#233 / PR #242):
/// <list type="number">
/// <item>S1 #1 — the process-wide singleton profile/session stores let one operator's active profile and
/// forwarded bearer bleed into another operator's requests on the multi-operator browser host.</item>
/// <item>S1 #2 — the bare <c>RequireAuthenticatedUser</c> fallback policy blocked the legitimately-anonymous
/// public embed / public-link / open-data surfaces.</item>
/// </list>
/// </summary>
public sealed class ConsoleAuthHardeningTests
{
    // ---- S1 #1: cross-operator bearer/identity isolation -------------------------------------------------

    [Fact]
    public async Task OperatorScopedSessionStore_DoesNotLeakBearerAcrossOperators()
    {
        var context = new SwitchableOperatorContext();
        var sessions = new OperatorScopedAccountSessionStore(context);

        // Operator A signs in and a real bearer is written for the active profile.
        context.CurrentOperatorKey = "operator-a";
        await sessions.SaveSessionAsync(new ConsoleAccountSession
        {
            ProfileId = "env-1",
            AccountId = "operator-a",
            AccessToken = "bearer-A",
        });

        // Operator B (same active profile id) must NOT observe operator A's session/bearer.
        context.CurrentOperatorKey = "operator-b";
        Assert.Null(await sessions.GetSessionAsync("env-1"));

        await sessions.SaveSessionAsync(new ConsoleAccountSession
        {
            ProfileId = "env-1",
            AccountId = "operator-b",
            AccessToken = "bearer-B",
        });

        // Each operator only ever reads back their own bearer for the same profile id.
        context.CurrentOperatorKey = "operator-a";
        Assert.Equal("bearer-A", (await sessions.GetSessionAsync("env-1"))!.AccessToken);

        context.CurrentOperatorKey = "operator-b";
        Assert.Equal("bearer-B", (await sessions.GetSessionAsync("env-1"))!.AccessToken);
    }

    [Fact]
    public async Task OperatorScopedProfileStore_DoesNotLeakActiveProfileOrIdentityAcrossOperators()
    {
        var context = new SwitchableOperatorContext();
        var profiles = new OperatorScopedEnvironmentProfileStore(context);

        // Operator A connects an environment bound to their own identity and activates it.
        context.CurrentOperatorKey = "operator-a";
        await profiles.UpsertProfileAsync(new ConsoleEnvironmentProfile
        {
            Id = "env-shared-id",
            DisplayName = "A's environment",
            ServerBaseUri = new Uri("https://a.honua.test"),
            Account = new ConsoleAccountBinding
            {
                AuthMode = ConsoleAccountAuthMode.AccountRbac,
                AccountId = "operator-a",
            },
        });
        await profiles.ActivateProfileAsync("env-shared-id");

        // Operator B shares neither the active-profile pointer nor the account binding: their store is empty
        // until they connect their own, and the active profile they connect carries THEIR identity only.
        context.CurrentOperatorKey = "operator-b";
        Assert.Empty(await profiles.ListProfilesAsync());
        Assert.Null(await profiles.GetActiveProfileAsync());

        await profiles.UpsertProfileAsync(new ConsoleEnvironmentProfile
        {
            Id = "env-shared-id",
            DisplayName = "B's environment",
            ServerBaseUri = new Uri("https://b.honua.test"),
            Account = new ConsoleAccountBinding
            {
                AuthMode = ConsoleAccountAuthMode.AccountRbac,
                AccountId = "operator-b",
            },
        });
        await profiles.ActivateProfileAsync("env-shared-id");

        context.CurrentOperatorKey = "operator-a";
        var aActive = await profiles.GetActiveProfileAsync();
        Assert.Equal("operator-a", aActive!.Account.AccountId);
        Assert.Equal(new Uri("https://a.honua.test"), aActive.ServerBaseUri);

        context.CurrentOperatorKey = "operator-b";
        var bActive = await profiles.GetActiveProfileAsync();
        Assert.Equal("operator-b", bActive!.Account.AccountId);
        Assert.Equal(new Uri("https://b.honua.test"), bActive.ServerBaseUri);
    }

    [Fact]
    public void OperatorContext_ResolvesStableKeyPerPrincipal_AndAnonymousShareOnePartition()
    {
        var operatorA = Principal(subject: "op-a", name: "Operator A");
        var operatorB = Principal(subject: "op-b", name: "Operator B");

        Assert.Equal("op-a", ConsoleOperatorContext.ResolveKey(operatorA));
        Assert.Equal("op-b", ConsoleOperatorContext.ResolveKey(operatorB));
        Assert.NotEqual(ConsoleOperatorContext.ResolveKey(operatorA), ConsoleOperatorContext.ResolveKey(operatorB));

        // Unauthenticated principals have no operator key (callers fall back to the shared anonymous partition).
        Assert.Null(ConsoleOperatorContext.ResolveKey(new ClaimsPrincipal(new ClaimsIdentity())));
        Assert.Null(ConsoleOperatorContext.ResolveKey(null));
    }

    // ---- S1 #2: fail-closed default, public surfaces reachable anonymously ------------------------------

    [Theory]
    [InlineData("/embed/maps/abc")]
    [InlineData("/share/public")]
    [InlineData("/share/public/items/parcels")]
    [InlineData("/public")]
    [InlineData("/public/items/parcels")]
    public async Task PublicSurface_IsReachableAnonymously(string path)
    {
        var result = await EvaluateFallbackAsync(path, authenticated: false);
        Assert.True(result, $"Anonymous request to public surface '{path}' should be allowed by the fallback policy.");
    }

    [Theory]
    [InlineData("/operate")]
    [InlineData("/operate/deploy")]
    [InlineData("/share")]            // the operator Share home is NOT public — only /share/public is.
    [InlineData("/studio")]
    [InlineData("/")]
    public async Task OperatorRoute_IsDeniedAnonymously(string path)
    {
        var result = await EvaluateFallbackAsync(path, authenticated: false);
        Assert.False(result, $"Anonymous request to operator route '{path}' must be denied (fail-closed).");
    }

    [Theory]
    [InlineData("/operate")]
    [InlineData("/share")]
    [InlineData("/")]
    public async Task OperatorRoute_IsAllowedForAuthenticatedOperator(string path)
    {
        var result = await EvaluateFallbackAsync(path, authenticated: true);
        Assert.True(result, $"Authenticated operator request to '{path}' should be allowed.");
    }

    private static async Task<bool> EvaluateFallbackAsync(string path, bool authenticated)
    {
        var requirement = new ConsoleAuthenticatedOrPublicRequirement();
        var handler = new ConsoleAuthenticatedOrPublicHandler();

        var httpContext = new DefaultHttpContext { Request = { Path = path } };
        var user = authenticated
            ? Principal(subject: "op-1", name: "Operator One")
            : new ClaimsPrincipal(new ClaimsIdentity());
        httpContext.User = user;

        var authContext = new AuthorizationHandlerContext([requirement], user, httpContext);
        await handler.HandleAsync(authContext);
        return authContext.HasSucceeded;
    }

    private static ClaimsPrincipal Principal(string subject, string name) =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, subject),
                new Claim(ClaimTypes.Name, name),
            ],
            authenticationType: ConsoleAuthConstants.CookieScheme));

    private sealed class SwitchableOperatorContext : IConsoleOperatorContext
    {
        public string CurrentOperatorKey { get; set; } = ConsoleOperatorContext.AnonymousKey;

        public bool HasOperator =>
            !string.Equals(CurrentOperatorKey, ConsoleOperatorContext.AnonymousKey, StringComparison.Ordinal);

        public string RequireOperatorKey() =>
            HasOperator ? CurrentOperatorKey : throw new ConsoleOperatorContextUnresolvedException();
    }
}
