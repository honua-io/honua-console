using System.Security.Claims;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Security;
using Honua.Console.Web.Auth;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Regression coverage for the S1 circuit-partition-collapse defect (honua-console#256).
///
/// honua-console#252 partitioned the multi-operator browser host's profile/session/bearer state by the
/// authenticated operator, but resolved the operator key from <see cref="HttpContext"/> with an
/// <c>AsyncLocal</c> ambient set on the sign-in HTTP thread. In Blazor <b>Server interactive</b> rendering
/// there is NO active <see cref="HttpContext"/> and the sign-in thread's execution context does not flow to
/// the circuit, so every interactive operator collapsed to the shared anonymous partition — re-introducing
/// the cross-operator bearer/identity bleed #252 was meant to fix.
///
/// #252's tests missed this because they drove the operator context through a settable test double
/// (<c>SwitchableOperatorContext</c>), never exercising the real <see cref="ConsoleOperatorContext"/> with
/// no <see cref="HttpContext"/>. These tests drive the REAL context the way a circuit does: the operator
/// identity comes only from the circuit's <see cref="AuthenticationStateProvider"/> via the production
/// <see cref="CircuitOperatorContextHandler"/>, with the <see cref="IHttpContextAccessor"/> deliberately
/// returning <c>null</c> (interactive rendering has no request).
/// </summary>
public sealed class ConsoleOperatorCircuitPartitionTests
{
    [Fact]
    public async Task TwoConcurrentInteractiveCircuits_GetDistinctSessionPartitionsAndBearers()
    {
        // Singleton context + stores, exactly as the Web host registers them (multi-operator browser host).
        // No HttpContext anywhere: this is the interactive-circuit path, not the request pipeline.
        var context = new ConsoleOperatorContext(new NullHttpContextAccessor());
        var sessions = new OperatorScopedAccountSessionStore(context);
        var profiles = new OperatorScopedEnvironmentProfileStore(context);

        var circuitA = new CircuitOperatorContextHandler(AuthState("operator-a"));
        var circuitB = new CircuitOperatorContextHandler(AuthState("operator-b"));

        // Each circuit, inside one of its inbound activities, binds its environment + writes its own bearer
        // — the work a signed-in operator's interactive component does. The two activities run concurrently
        // to prove the partition follows the circuit's auth state, not a process-/thread-shared ambient.
        var aWork = RunInboundActivityAsync(circuitA, async () =>
        {
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
            await sessions.SaveSessionAsync(new ConsoleAccountSession
            {
                ProfileId = "env-shared-id",
                AccountId = "operator-a",
                AccessToken = "bearer-A",
            });
        });

        var bWork = RunInboundActivityAsync(circuitB, async () =>
        {
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
            await sessions.SaveSessionAsync(new ConsoleAccountSession
            {
                ProfileId = "env-shared-id",
                AccountId = "operator-b",
                AccessToken = "bearer-B",
            });
        });

        await Task.WhenAll(aWork, bWork);

        // Now each circuit reads back. If the per-operator key had collapsed to the shared anonymous
        // partition (the regression), both circuits would observe the SAME (last-writer) bearer/profile.
        var aReadBearer = await RunInboundActivityAsync(circuitA, () =>
            sessions.GetSessionAsync("env-shared-id"));
        var bReadBearer = await RunInboundActivityAsync(circuitB, () =>
            sessions.GetSessionAsync("env-shared-id"));

        var aActive = await RunInboundActivityAsync(circuitA, () => profiles.GetActiveProfileAsync());
        var bActive = await RunInboundActivityAsync(circuitB, () => profiles.GetActiveProfileAsync());

        // Distinct bearers — no cross-operator bleed.
        Assert.Equal("bearer-A", aReadBearer?.AccessToken);
        Assert.Equal("bearer-B", bReadBearer?.AccessToken);
        Assert.NotEqual(aReadBearer?.AccessToken, bReadBearer?.AccessToken);

        // Distinct active profiles / bound identities.
        Assert.Equal("operator-a", aActive?.Account.AccountId);
        Assert.Equal("operator-b", bActive?.Account.AccountId);
        Assert.Equal(new Uri("https://a.honua.test"), aActive?.ServerBaseUri);
        Assert.Equal(new Uri("https://b.honua.test"), bActive?.ServerBaseUri);
    }

    [Fact]
    public async Task InsideAuthenticatedCircuitActivity_OperatorKeyResolvesFromCircuitAuthState_NotAnonymous()
    {
        var context = new ConsoleOperatorContext(new NullHttpContextAccessor());
        var circuit = new CircuitOperatorContextHandler(AuthState("operator-c"));

        var keyInside = await RunInboundActivityAsync(circuit, () => Task.FromResult(context.CurrentOperatorKey));

        Assert.Equal("operator-c", keyInside);
        Assert.NotEqual(ConsoleOperatorContext.AnonymousKey, keyInside);
    }

    [Fact]
    public async Task CircuitActivityAmbient_IsClearedAfterTheActivityCompletes()
    {
        // The ambient must be scoped to the activity: outside any circuit activity (e.g. a stray
        // server-bound call with no request and no active circuit) the key MUST NOT linger as some
        // operator's identity. It falls back to anonymous, and RequireOperatorKey fails closed.
        var context = new ConsoleOperatorContext(new NullHttpContextAccessor());
        var circuit = new CircuitOperatorContextHandler(AuthState("operator-d"));

        await RunInboundActivityAsync(circuit, () => Task.CompletedTask);

        Assert.Equal(ConsoleOperatorContext.AnonymousKey, context.CurrentOperatorKey);
        Assert.False(context.HasOperator);
        Assert.Throws<ConsoleOperatorContextUnresolvedException>(() => context.RequireOperatorKey());
    }

    [Fact]
    public async Task UnauthenticatedCircuit_FailsClosedOnServerBoundWrite()
    {
        // A circuit whose auth state is NOT authenticated (e.g. the operator signed out) must not be able
        // to write operator state into the shared anonymous partition: the write fails closed.
        var context = new ConsoleOperatorContext(new NullHttpContextAccessor());
        var sessions = new OperatorScopedAccountSessionStore(context);
        var anonymousCircuit = new CircuitOperatorContextHandler(
            new StubAuthenticationStateProvider(new ClaimsPrincipal(new ClaimsIdentity())));

        await Assert.ThrowsAsync<ConsoleOperatorContextUnresolvedException>(() =>
            RunInboundActivityAsync(anonymousCircuit, () => sessions.SaveSessionAsync(new ConsoleAccountSession
            {
                ProfileId = "env-shared-id",
                AccountId = "nobody",
                AccessToken = "should-never-be-written",
            })));
    }

    // Drives a unit of work through the production CircuitOperatorContextHandler's inbound-activity
    // pipeline, so the operator-key ambient is established from the circuit's auth state for the duration of
    // the work exactly as it is for a real render/event on the circuit. The activity context is internal and
    // not constructable in a unit test, but the handler never reads it (it only wraps the next delegate), so
    // passing null exercises the production code path faithfully.
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

    private sealed class StubAuthenticationStateProvider : AuthenticationStateProvider
    {
        private readonly AuthenticationState _state;

        public StubAuthenticationStateProvider(ClaimsPrincipal user) => _state = new AuthenticationState(user);

        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(_state);
    }

    private sealed class NullHttpContextAccessor : IHttpContextAccessor
    {
        // Interactive rendering has no request: HttpContext is always null here.
        public HttpContext? HttpContext { get => null; set { } }
    }
}
