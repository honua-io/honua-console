using System.Security.Claims;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Security;
using Honua.Console.Shell.Services;
using Honua.Console.Web.Auth;
using Microsoft.AspNetCore.Http;

namespace Honua.Console.IntegrationTests;

// SignInAsync does not set HttpContext.User for the same request, so the test signs in, assigns the principal, then synchronizes operator state or login fails closed.
public sealed class ConsoleDevLoginOperatorSyncTests
{
    // Mirrors ConsoleAuthentication.BuildDevPrincipal(): the Development dev operator identity.
    private const string DevOperatorSubject = "dev-operator";

    [Fact]
    public async Task DevLoginSync_WithAnonymousRequestUser_FailsClosed()
    {
        // Reproduces trunk: immediately after SignInAsync the request User is still unauthenticated, so the
        // operator-scoped profile write cannot resolve an operator and fails closed. This is the 500.
        var httpContext = new DefaultHttpContext();
        var (bridge, _) = BuildDevLoginStack(httpContext);

        await Assert.ThrowsAsync<ConsoleOperatorContextUnresolvedException>(
            async () => await bridge.SyncAsync(BuildDevPrincipal()));
    }

    [Fact]
    public async Task DevLoginSync_WithPrincipalSetOnRequest_ResolvesOperatorAndPersistsProfile()
    {
        // Reproduces the fix: the dev-login branch sets context.User = principal before syncing, so the
        // operator resolves on the same request and the profile/session writes land in THIS operator's
        // partition without throwing.
        var httpContext = new DefaultHttpContext();
        var (bridge, profiles) = BuildDevLoginStack(httpContext);
        var principal = BuildDevPrincipal();

        // The fix's action: the operator is now resolvable on HttpContext.User for this request.
        httpContext.User = principal;

        var exception = await Record.ExceptionAsync(async () => await bridge.SyncAsync(principal));
        Assert.Null(exception);

        // The dev operator's partition now holds an active profile rebound to the dev operator identity
        // (the write that previously threw). This is what the interactive routes read after sign-in.
        var active = await profiles.GetActiveProfileAsync();
        Assert.NotNull(active);
        Assert.Equal(ConsoleAccountAuthMode.AccountRbac, active!.Account.AuthMode);
        Assert.Equal(DevOperatorSubject, active.Account.AccountId);
    }

    [Fact]
    public async Task DevLoginSync_ServiceProfile_IsReboundToHumanAccountMode()
    {
        var httpContext = new DefaultHttpContext();
        var (bridge, profiles) = BuildDevLoginStack(httpContext, ConsoleAccountAuthMode.ServiceApiKey);
        var principal = BuildDevPrincipal();
        httpContext.User = principal;

        await bridge.SyncAsync(principal);

        var active = await profiles.GetActiveProfileAsync();
        Assert.Equal(ConsoleAccountAuthMode.AccountRbac, active!.Account.AuthMode);
        Assert.Equal(DevOperatorSubject, active.Account.AccountId);
    }

    // Wires the production dev-login server-sync stack for the given request:
    //  - the real ConsoleOperatorContext resolving from HttpContext.User (via IHttpContextAccessor), and
    //  - the operator-scoped profile/session stores seeded exactly like the Development browser dev-seed
    //    (BuildBrowserDevSeed): one active "Local honua-server" profile bound to the seed account, so the
    //    sync's rebind-to-operator write path (the one that fails closed on an unresolved operator) runs.
    private static (ConsoleOperatorSessionBridge Bridge, IConsoleEnvironmentProfileStore Profiles) BuildDevLoginStack(
        HttpContext httpContext,
        ConsoleAccountAuthMode authMode = ConsoleAccountAuthMode.AccountRbac)
    {
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var operatorContext = new ConsoleOperatorContext(accessor);

        var profiles = new OperatorScopedEnvironmentProfileStore(operatorContext, () => DevSeedFactory(authMode));
        var sessions = new OperatorScopedAccountSessionStore(operatorContext);
        return (new ConsoleOperatorSessionBridge(profiles, sessions), profiles);
    }

    private static InMemoryConsoleEnvironmentProfileStore DevSeedFactory(ConsoleAccountAuthMode authMode)
    {
        var devProfile = new ConsoleEnvironmentProfile
        {
            Id = "local-dev",
            DisplayName = "Local honua-server",
            ServerBaseUri = new Uri("https://localhost:5001"),
            EnvironmentKind = "development",
            Account = new ConsoleAccountBinding
            {
                AuthMode = authMode,
                AccountId = "console-user",
                DisplayName = "Console User",
            },
        };
        return new InMemoryConsoleEnvironmentProfileStore([devProfile], activeProfileId: devProfile.Id);
    }

    private static ClaimsPrincipal BuildDevPrincipal() =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, DevOperatorSubject),
                new Claim(ClaimTypes.Name, "Developer"),
            ],
            authenticationType: ConsoleAuthConstants.CookieScheme));
}
