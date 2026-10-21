using Honua.Console.Web.Auth;
using Microsoft.AspNetCore.Http;

namespace Honua.Console.IntegrationTests;

public sealed class ForwardedHeadersTests
{
    [Fact]
    public async Task PrivateProxy_RewritesSchemeAndHost()
    {
        var (scheme, host) = await ConsoleForwardedHeaders.InvokeAsync(
            "10.1.2.3", "http", "console-pod:8080", "https", "console.example");

        Assert.Equal("https", scheme);
        Assert.Equal("console.example", host);
    }

    [Fact]
    public async Task UniqueLocalAndLoopbackPeers_RewriteSchemeAndHost()
    {
        var ula = await ConsoleForwardedHeaders.InvokeAsync(
            "fc00::1", "http", "console-pod:8080", "https", "console.example");
        var loopback = await ConsoleForwardedHeaders.InvokeAsync(
            "127.0.0.1", "http", "console-pod:8080", "https", "console.example");

        Assert.Equal(("https", "console.example"), ula);
        Assert.Equal(("https", "console.example"), loopback);
    }

    [Fact]
    public async Task PublicRemote_IgnoresForwardedHeaders()
    {
        var (scheme, host) = await ConsoleForwardedHeaders.InvokeAsync(
            "8.8.8.8", "http", "console-pod:8080", "https", "console.example");

        Assert.Equal("http", scheme);
        Assert.Equal("console-pod", host);
    }

    [Fact]
    public void OperatorCookie_IsSecureOutsideDevelopment()
    {
        Assert.Equal(CookieSecurePolicy.Always, ConsoleAuthentication.CookieSecurePolicyFor(isDevelopment: false));
        Assert.Equal(CookieSecurePolicy.SameAsRequest, ConsoleAuthentication.CookieSecurePolicyFor(isDevelopment: true));
    }
}
