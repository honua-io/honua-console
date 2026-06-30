using System.Security.Claims;
using Honua.Console.Shell.Security;
using Honua.Console.Web.Auth;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Fail-closed-by-construction coverage for the scoped operator accessor (honua-console#254).
///
/// The recurring S1 fail-open class was that operator identity flowed through a singleton store + a
/// process-static ambient key whose <c>__anonymous__</c> sentinel silently stood in for BOTH a genuinely
/// anonymous surface AND an authenticated operator whose execution context failed to resolve. The
/// redesign replaces that, on the server-bound call path, with a per-circuit/per-request SCOPED service
/// (<see cref="ConsoleOperatorScope"/>) that reads its scope's OWN authoritative identity and returns a
/// strongly-typed <see cref="ConsoleOperatorIdentity"/> or <see langword="null"/> — never a shared
/// "anonymous" operator. These tests prove an unresolved operator is a hard deny (never an ambient
/// fallback) and that concurrent scopes stay isolated by construction.
///
/// This is the seam the map-proxy BFF endpoints (the highest-risk "acts with honua-server privileges"
/// surface, in <c>Program.cs</c>) are converted to: each resolves <see cref="IConsoleOperatorScope"/>
/// per request and denies (401) when it yields <c>null</c>.
/// </summary>
public sealed class ConsoleOperatorScopeTests
{
    [Fact]
    public async Task RequestScope_AuthenticatedUser_ResolvesThatOperator()
    {
        var scope = new ConsoleOperatorScope(
            HttpContextFor(Operator("operator-a", "Operator A")),
            UnauthenticatedCircuit());

        var resolved = await scope.ResolveAsync();
        var required = await scope.RequireAsync();

        Assert.NotNull(resolved);
        Assert.Equal("operator-a", resolved!.Key);
        Assert.Equal("operator-a", resolved.AccountId);
        Assert.Equal("Operator A", resolved.DisplayName);
        Assert.Equal("operator-a", required.Key);
    }

    [Fact]
    public async Task RequestScope_AnonymousUser_ResolvesNull_AndRequireFailsClosed()
    {
        // A request whose HttpContext.User is unauthenticated must NOT be re-resolved from any other
        // source: it is anonymous, full stop. ResolveAsync returns null (the caller's hard-deny signal)
        // and RequireAsync fails closed — even though a circuit AuthenticationStateProvider also exists.
        var scope = new ConsoleOperatorScope(
            HttpContextFor(new ClaimsPrincipal(new ClaimsIdentity())),
            AuthenticatedCircuit("circuit-operator"));

        Assert.Null(await scope.ResolveAsync());
        await Assert.ThrowsAsync<ConsoleOperatorContextUnresolvedException>(
            async () => await scope.RequireAsync());
    }

    [Fact]
    public async Task CircuitScope_NoHttpContext_ResolvesFromAuthenticationStateProvider()
    {
        // Interactive rendering: no HttpContext. The authoritative identity is the circuit's
        // AuthenticationStateProvider, read directly from this scope (not stamped onto an ambient).
        var scope = new ConsoleOperatorScope(
            NoHttpContext(),
            AuthenticatedCircuit("operator-c"));

        var resolved = await scope.ResolveAsync();

        Assert.NotNull(resolved);
        Assert.Equal("operator-c", resolved!.Key);
    }

    [Fact]
    public async Task CircuitScope_Unauthenticated_RequireFailsClosed_NeverAnonymousFallback()
    {
        // A circuit with no authenticated state (e.g. the operator signed out) must be a hard deny on the
        // server-bound path — it must never collapse to a shared anonymous operator the way the legacy
        // string key did.
        var scope = new ConsoleOperatorScope(
            NoHttpContext(),
            UnauthenticatedCircuit());

        Assert.Null(await scope.ResolveAsync());
        await Assert.ThrowsAsync<ConsoleOperatorContextUnresolvedException>(
            async () => await scope.RequireAsync());
    }

    [Fact]
    public async Task ConcurrentScopes_ResolveDistinctOperators_WithNoSharedAmbientState()
    {
        // The whole point of the redesign: two concurrent contexts cannot bleed into each other because
        // each ConsoleOperatorScope reads only its OWN injected identity source. There is no process-wide
        // ambient that interleaving could leave pointing at the wrong operator. We mix request-scoped and
        // circuit-scoped operators plus an unauthenticated scope, run them all concurrently, and assert
        // every scope sees exactly its own operator — and the unauthenticated one is denied.
        var requestOperator = new ConsoleOperatorScope(
            HttpContextFor(Operator("operator-a", "Operator A")), UnauthenticatedCircuit());
        var circuitOperator = new ConsoleOperatorScope(
            NoHttpContext(), AuthenticatedCircuit("operator-b"));
        var anonymous = new ConsoleOperatorScope(
            NoHttpContext(), UnauthenticatedCircuit());

        // Hammer each scope concurrently to surface any shared/ambient cross-talk.
        var aTasks = Enumerable.Range(0, 50).Select(_ => requestOperator.RequireAsync().AsTask());
        var bTasks = Enumerable.Range(0, 50).Select(_ => circuitOperator.RequireAsync().AsTask());
        var anonTasks = Enumerable.Range(0, 50)
            .Select(async _ => await Assert.ThrowsAsync<ConsoleOperatorContextUnresolvedException>(
                async () => await anonymous.RequireAsync()));

        var aResults = await Task.WhenAll(aTasks);
        var bResults = await Task.WhenAll(bTasks);
        await Task.WhenAll(anonTasks);

        Assert.All(aResults, identity => Assert.Equal("operator-a", identity.Key));
        Assert.All(bResults, identity => Assert.Equal("operator-b", identity.Key));
        Assert.NotEqual(aResults[0].Key, bResults[0].Key);
    }

    [Fact]
    public void ConsoleOperatorIdentity_CannotBeBuiltFromAnUnauthenticatedPrincipal()
    {
        // There is no "anonymous" ConsoleOperatorIdentity value: an unauthenticated principal yields null,
        // so the type itself cannot represent the fail-open "no operator" state on the server-bound path.
        Assert.Null(ConsoleOperatorIdentity.FromPrincipal(null));
        Assert.Null(ConsoleOperatorIdentity.FromPrincipal(new ClaimsPrincipal(new ClaimsIdentity())));

        var identity = ConsoleOperatorIdentity.FromPrincipal(Operator("op-x", "Operator X"));
        Assert.NotNull(identity);
        Assert.False(string.IsNullOrEmpty(identity!.Key));
    }

    private static ClaimsPrincipal Operator(string subject, string name) =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, subject),
                new Claim(ClaimTypes.Name, name),
            ],
            authenticationType: ConsoleAuthConstants.CookieScheme));

    private static IHttpContextAccessor HttpContextFor(ClaimsPrincipal user)
    {
        var httpContext = new DefaultHttpContext { User = user };
        return new StubHttpContextAccessor(httpContext);
    }

    private static IHttpContextAccessor NoHttpContext() => new StubHttpContextAccessor(null);

    private static AuthenticationStateProvider AuthenticatedCircuit(string operatorId) =>
        new StubAuthenticationStateProvider(Operator(operatorId, operatorId));

    private static AuthenticationStateProvider UnauthenticatedCircuit() =>
        new StubAuthenticationStateProvider(new ClaimsPrincipal(new ClaimsIdentity()));

    private sealed class StubHttpContextAccessor : IHttpContextAccessor
    {
        public StubHttpContextAccessor(HttpContext? httpContext) => HttpContext = httpContext;

        public HttpContext? HttpContext { get; set; }
    }

    private sealed class StubAuthenticationStateProvider : AuthenticationStateProvider
    {
        private readonly AuthenticationState _state;

        public StubAuthenticationStateProvider(ClaimsPrincipal user) => _state = new AuthenticationState(user);

        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(_state);
    }
}
