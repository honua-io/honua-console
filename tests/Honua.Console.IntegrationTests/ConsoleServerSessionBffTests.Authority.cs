using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Honua.Console.Web.Auth;

namespace Honua.Console.IntegrationTests;

public sealed partial class ConsoleServerSessionBffTests
{
    [Fact]
    public async Task SessionStore_SameOperatorAndProfileDifferentPath_IsolatesCookieJars()
    {
        await using var store = new ConsoleServerSessionClientStore(TimeProvider.System);
        var servingProfile = Profile("shared") with { ServerBaseUri = new Uri(ServerOrigin, "serving/") };
        var managerProfile = servingProfile with { ServerBaseUri = new Uri(ServerOrigin, "management/") };
        var serving = store.GetOrCreate("operator", servingProfile);
        var manager = store.GetOrCreate("operator", managerProfile);
        serving.Cookies.SetCookies(ServerOrigin, "honua_admin_session=serving-session; Path=/; HttpOnly");
        manager.Cookies.SetCookies(ServerOrigin, "honua_admin_session=manager-session; Path=/; HttpOnly");

        Assert.NotSame(serving.Cookies, manager.Cookies);
        Assert.Contains("serving-session", serving.Cookies.GetCookieHeader(ServerOrigin), StringComparison.Ordinal);
        Assert.DoesNotContain("manager-session", serving.Cookies.GetCookieHeader(ServerOrigin), StringComparison.Ordinal);
        store.Remove("operator", servingProfile);
        Assert.True(store.TryGet("operator", managerProfile, out var remaining));
        Assert.Same(manager, remaining);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://server.example/")]
    public async Task ManagementSignIn_WithoutSeparateAuthority_RefusesBeforeCreatingASession(string? managerBaseUri)
    {
        var profile = Profile("environment");
        var profiles = new InMemoryConsoleEnvironmentProfileStore([profile], activeProfileId: profile.Id);
        var created = 0;
        await using var store = new ConsoleServerSessionClientStore(TimeProvider.System, (key, cookies) =>
        {
            created++;
            return new SimulatedServerAuthHandler(key, cookies);
        });
        IConsoleManagerEndpoint? management = managerBaseUri is null ? null
            : new MutableManagementEndpoint { Target = new Uri(managerBaseUri) };
        var coordinator = new ConsoleServerSessionBffCoordinator(new SwitchableOperatorContext("operator"), profiles,
            new InMemoryConsoleAccountSessionStore(), store, TimeProvider.System, management: management);

        var result = await coordinator.BeginAuthoritySignInAsync(
            new ConsoleServerSignInTarget(profile.Id, ConsoleServerAuthority.Management, null), "/inbox");

        Assert.Equal(ConsoleServerSignInStatus.Unavailable, result.Status);
        Assert.Equal(0, created);
    }
}
