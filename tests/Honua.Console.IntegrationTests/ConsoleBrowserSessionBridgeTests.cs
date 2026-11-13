using System.Security.Claims;
using Honua.Console.Web.Runtime;

namespace Honua.Console.IntegrationTests;

public sealed class ConsoleBrowserSessionBridgeTests
{
    [Fact]
    public void SessionChangesInvalidateCapturedAuthorityAndClearCredentials()
    {
        var session = new ConsoleBrowserSession();
        var bridge = new ConsoleBrowserSessionBridge(session);
        var anonymous = bridge.GetSnapshot();
        Assert.False(anonymous.Authenticated);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "operator")], "test"));
        session.Set(principal, new ConsoleSessionCredentials("csrf", "bearer", DateTimeOffset.UtcNow.AddMinutes(5)));
        var authenticated = bridge.GetSnapshot();
        Assert.True(authenticated.Authenticated);
        Assert.Equal("operator", authenticated.PrincipalId);
        Assert.Equal("bearer", authenticated.AccessToken);
        Assert.Equal("csrf", authenticated.CsrfToken);
        Assert.True(authenticated.Revision > anonymous.Revision);
        session.Clear();
        var cleared = bridge.GetSnapshot();
        Assert.False(cleared.Authenticated);
        Assert.Null(cleared.AccessToken);
        Assert.Null(cleared.CsrfToken);
        Assert.True(cleared.Revision > authenticated.Revision);
    }

    [Fact]
    public void ExpiredSessionDoesNotExposeCredentialsToJavaScript()
    {
        var session = new ConsoleBrowserSession();
        session.Set(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "operator")], "test")),
            new ConsoleSessionCredentials("csrf", "bearer", DateTimeOffset.UtcNow.AddMinutes(-1)));
        var snapshot = new ConsoleBrowserSessionBridge(session).GetSnapshot();
        Assert.False(snapshot.Authenticated);
        Assert.Null(snapshot.AccessToken);
        Assert.Null(snapshot.CsrfToken);
    }
}
