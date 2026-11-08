using System.Security.Claims;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Honua.Console.Web.Auth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;

namespace Honua.Console.IntegrationTests;

public sealed partial class ConsoleServerSessionBffTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("rejected-token")]
    [InlineData("missing-state")]
    [InlineData("wrong-operator")]
    [InlineData("mixed-credentials")]
    public async Task CompleteSignIn_CallbackProof_RequiresSuccessfulBoundHumanExchange(string scenario)
    {
        var profile = Profile("environment");
        var operatorContext = new SwitchableOperatorContext("operator");
        var profiles = new InMemoryConsoleEnvironmentProfileStore([profile], activeProfileId: profile.Id);
        var accounts = new InMemoryConsoleAccountSessionStore();
        await using var partitions = new ConsoleServerSessionClientStore(TimeProvider.System,
            (key, cookies) => new SimulatedServerAuthHandler(key, cookies, scenario == "rejected-token"));
        var request = BrowserCallbackContext("operator");
        var accessor = new HttpContextAccessor { HttpContext = request };
        var channel = new ConsoleApprovalBrowserChannel(new EphemeralDataProtectionProvider(), accessor, TimeProvider.System);
        var coordinator = new ConsoleServerSessionBffCoordinator(operatorContext, profiles, accounts, partitions,
            TimeProvider.System, approvalChannel: channel);
        var begin = await coordinator.BeginSignInAsync(profile.Id, null, "/inbox");
        if (scenario == "wrong-operator") operatorContext.OperatorKey = "other";
        if (scenario == "mixed-credentials") request.Request.Headers.Authorization = "Bearer agent";

        var result = await coordinator.CompleteSignInAsync("code",
            scenario == "missing-state" ? null : State(begin.RedirectUri), null);

        if (scenario == "success")
        {
            Assert.Equal(ConsoleServerSignInStatus.Redirect, result.Status);
            ReplayCallbackCookie(request);
            Assert.True(channel.HasSession(profile, partitions));
            var proof = request.Request.Headers.Cookie.ToString();
            request.Response.Headers.Clear();
            await coordinator.CompleteSignInAsync("code", State(begin.RedirectUri), null);
            Assert.Equal(0, request.Response.Headers.SetCookie.Count);
            Assert.Equal(proof, request.Request.Headers.Cookie.ToString());
        }
        else
        {
            Assert.Equal(0, request.Response.Headers.SetCookie.Count);
            Assert.False(channel.HasSession(profile, partitions));
        }
        accessor.HttpContext = null;
    }

    [Theory]
    [InlineData("operator")]
    [InlineData("profile")]
    [InlineData("tenant")]
    [InlineData("full-path")]
    [InlineData("authority")]
    [InlineData("audience")]
    [InlineData("expired")]
    [InlineData("logout")]
    public async Task BrowserProof_AnotherScope_Refuses(string change)
    {
        var profile = Profile("environment");
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        await using var partitions = new ConsoleServerSessionClientStore(clock);
        var partition = partitions.GetOrCreate("operator", profile);
        var context = BrowserCallbackContext("operator");
        var accessor = new HttpContextAccessor { HttpContext = context };
        var channel = new ConsoleApprovalBrowserChannel(new EphemeralDataProtectionProvider(), accessor, clock);
        var target = new ConsoleApprovalBrowserTarget
        {
            SelectedProfile = profile,
            AuthorityProfile = profile,
            Authority = ConsoleServerAuthority.Serving,
            Partition = partition
        };
        channel.CompleteCallback(target);
        ReplayCallbackCookie(context);
        Assert.True(channel.Authorizes(target));
        switch (change)
        {
            case "operator": context.User = BrowserCallbackContext("other").User; break;
            case "profile": target = target with { SelectedProfile = profile with { Id = "other" } }; break;
            case "tenant": target = target with { SelectedProfile = profile with { TenantId = "other" } }; break;
            case "full-path": target = target with { AuthorityProfile = profile with { ServerBaseUri = new("https://server.example/other/") } }; break;
            case "authority": target = target with { Authority = ConsoleServerAuthority.Management }; break;
            case "audience": context.Request.Host = new HostString("other-console.example"); break;
            case "expired": clock.Advance(TimeSpan.FromMinutes(11)); break;
            case "logout": partitions.Remove("operator", profile); break;
        }

        Assert.False(channel.Authorizes(target));
        accessor.HttpContext = null;
    }

    private static DefaultHttpContext BrowserCallbackContext(string actor)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("console.example");
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actor)], "cookie"));
        return context;
    }

    private static void ReplayCallbackCookie(HttpContext context)
    {
        var cookie = Assert.Single(context.Response.Headers.SetCookie)!;
        context.Request.Headers.Cookie = cookie[..cookie.IndexOf(';')];
    }
}
