using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Honua.Console.Web.Auth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Honua.Console.IntegrationTests;

public sealed partial class ConsoleServerSessionBffTests
{
    [Theory]
    [InlineData("operator")]
    [InlineData("profile")]
    [InlineData("tenant")]
    [InlineData("upstream-path")]
    [InlineData("authority")]
    [InlineData("origin")]
    [InlineData("path-base")]
    [InlineData("bearer")]
    [InlineData("key")]
    [InlineData("expired")]
    [InlineData("other-session")]
    [InlineData("changed-upstream-cookie")]
    public async Task StudioEditProof_AnotherScope_Refuses(string change)
    {
        var profile = Profile("environment");
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        await using var partitions = new ConsoleServerSessionClientStore(clock);
        var partition = partitions.GetOrCreate("operator", profile);
        partition.AuthenticatedSessionExpiresAt = clock.GetUtcNow().AddHours(1);
        partition.Cookies.SetCookies(profile.ServerBaseUri, "honua_admin_session=original-session; Path=/; HttpOnly");
        var context = BrowserCallbackContext("operator");
        var accessor = new HttpContextAccessor { HttpContext = context };
        var channel = new ConsoleStudioEditBrowserChannel(new EphemeralDataProtectionProvider(), accessor, clock,
            NullLogger<ConsoleStudioEditBrowserChannel>.Instance);
        var target = new ConsoleHumanBrowserTarget
        {
            SelectedProfile = profile, AuthorityProfile = profile,
            Authority = ConsoleServerAuthority.Serving, Partition = partition
        };
        channel.CompleteCallback(target);
        ReplayCallbackCookie(context);
        Assert.True(channel.Authorizes(target));

        switch (change)
        {
            case "operator": context.User = BrowserCallbackContext("other").User; break;
            case "profile": target = target with { SelectedProfile = profile with { Id = "other" } }; break;
            case "tenant": target = target with { SelectedProfile = profile with { TenantId = "other" } }; break;
            case "upstream-path": target = target with { AuthorityProfile = profile with { ServerBaseUri = new("https://server.example/other/") } }; break;
            case "authority": target = target with { Authority = ConsoleServerAuthority.Management }; break;
            case "origin": context.Request.Host = new HostString("other-console.example"); break;
            case "path-base": context.Request.PathBase = "/other"; break;
            case "bearer": context.Request.Headers.Authorization = "Bearer agent"; break;
            case "key": context.Request.Headers["X-API-Key"] = "agent-key"; break;
            case "expired": clock.Advance(TimeSpan.FromHours(1)); break;
            case "other-session": target = target with { Partition = partitions.GetOrCreate("operator", profile with { Id = "other" }) }; break;
            case "changed-upstream-cookie": partition.Cookies.SetCookies(profile.ServerBaseUri, "honua_admin_session=replacement; Path=/; HttpOnly"); break;
        }

        Assert.False(channel.Authorizes(target));
        accessor.HttpContext = null;
    }

    [Theory]
    [InlineData("missing-expiry")]
    [InlineData("expired-session")]
    [InlineData("management-session")]
    public async Task StudioEditProof_UnverifiedOrNonServingSession_DoesNotIssueCookie(string scenario)
    {
        var profile = Profile("environment");
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        await using var partitions = new ConsoleServerSessionClientStore(clock);
        var partition = partitions.GetOrCreate("operator", profile);
        partition.AuthenticatedSessionExpiresAt = scenario == "missing-expiry" ? null
            : clock.GetUtcNow().AddMinutes(scenario == "expired-session" ? -1 : 60);
        var context = BrowserCallbackContext("operator");
        var accessor = new HttpContextAccessor { HttpContext = context };
        var channel = new ConsoleStudioEditBrowserChannel(new EphemeralDataProtectionProvider(), accessor, clock,
            NullLogger<ConsoleStudioEditBrowserChannel>.Instance);

        channel.CompleteCallback(new ConsoleHumanBrowserTarget
        {
            SelectedProfile = profile, AuthorityProfile = profile, Partition = partition,
            Authority = scenario == "management-session" ? ConsoleServerAuthority.Management : ConsoleServerAuthority.Serving
        });

        Assert.Equal(0, context.Response.Headers.SetCookie.Count);
        accessor.HttpContext = null;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BrowserProof_RenamedCookie_CannotCrossPurposes(bool approvalAsEdit)
    {
        var profile = Profile("environment");
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        await using var partitions = new ConsoleServerSessionClientStore(clock);
        var partition = partitions.GetOrCreate("operator", profile);
        partition.AuthenticatedSessionExpiresAt = clock.GetUtcNow().AddHours(1);
        var context = BrowserCallbackContext("operator");
        var accessor = new HttpContextAccessor { HttpContext = context };
        var protection = new EphemeralDataProtectionProvider();
        var approval = new ConsoleApprovalBrowserChannel(protection, accessor, clock, NullLogger<ConsoleApprovalBrowserChannel>.Instance);
        var edit = new ConsoleStudioEditBrowserChannel(protection, accessor, clock, NullLogger<ConsoleStudioEditBrowserChannel>.Instance);
        var target = new ConsoleHumanBrowserTarget
        {
            SelectedProfile = profile, AuthorityProfile = profile,
            Authority = ConsoleServerAuthority.Serving, Partition = partition
        };
        approval.CompleteCallback(target);
        edit.CompleteCallback(target);
        var sourcePrefix = approvalAsEdit ? "__Host-HonuaApproval." : "__Host-HonuaStudioEdit.";
        var targetPrefix = approvalAsEdit ? "__Host-HonuaStudioEdit." : "__Host-HonuaApproval.";
        var source = Assert.Single(context.Response.Headers.SetCookie, cookie => cookie!.StartsWith(sourcePrefix, StringComparison.Ordinal))!;
        context.Request.Headers.Cookie = source[..source.IndexOf(';')].Replace(sourcePrefix, targetPrefix, StringComparison.Ordinal);

        Assert.False((approvalAsEdit ? (ConsoleHumanBrowserChannel)edit : approval).Authorizes(target));
        accessor.HttpContext = null;
    }
}
