using System.Security.Claims;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Security;
using Honua.Console.Shell.Services;
using Honua.Console.Web.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Honua.Console.IntegrationTests;

public sealed partial class ConsoleServerSessionIdentityBindingTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("subject")]
    [InlineData("missing-expiry")]
    [InlineData("expired")]
    [InlineData("missing-state")]
    [InlineData("agent-token")]
    public async Task HostedCallback_StudioEditProof_RequiresBoundValidatedHumanSession(string scenario)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ConsoleAuthConstants.AuthModeKey] = "EdgeForwarded",
            [ConsoleAuthConstants.EdgeServerSessionIssuerKey] = Issuer
        });
        builder.Services.AddSingleton<IConsoleOperatorContext>(new OperatorContext());
        builder.Services.AddSingleton(new ConsoleServerSessionClientStore(TimeProvider.System, (_, cookies) =>
            new SimulatedServer(cookies) { SessionJson = scenario is "subject" or "missing-expiry" or "expired" ? InvalidSession(scenario) : Session() }));
        builder.AddConsoleAuthentication();
        builder.AddConsoleServerSessionBff();
        await using var app = builder.Build();
        var profile = Profile("env");
        var profiles = app.Services.GetRequiredService<IConsoleEnvironmentProfileStore>();
        await profiles.UpsertProfileAsync(profile);
        profile = (await profiles.GetProfileAsync(profile.Id))!;
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("console.example");
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "operator-a")], "browser-cookie"));
        if (scenario == "agent-token") context.Request.Headers.Authorization = "Bearer agent-token";
        var accessor = app.Services.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = context;
        try
        {
            var coordinator = app.Services.GetRequiredService<ConsoleServerSessionBffCoordinator>();
            var begin = await coordinator.BeginSignInAsync(profile.Id, null, "/studio");
            var state = QueryHelpers.ParseQuery(new Uri(begin.RedirectUri).Query)["state"].ToString();
            await coordinator.CompleteSignInAsync("code", scenario == "missing-state" ? null : state, null);

            if (scenario == "success") AssertStudioCallbackProofs(app, context, profile);
            else Assert.Equal(0, context.Response.Headers.SetCookie.Count);
        }
        finally { accessor.HttpContext = null; }
    }

    private static void AssertStudioCallbackProofs(WebApplication app, HttpContext context, ConsoleEnvironmentProfile profile)
    {
        var cookies = context.Response.Headers.SetCookie.Select(cookie => SetCookieHeaderValue.Parse(cookie!)).ToArray();
        Assert.Equal(2, cookies.Length);
        var edit = Assert.Single(cookies, cookie => cookie.Name.Value!.StartsWith("__Host-HonuaStudioEdit.", StringComparison.Ordinal));
        Assert.True(edit.Secure);
        Assert.True(edit.HttpOnly);
        Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Strict, edit.SameSite);
        Assert.Equal("/", edit.Path.Value);
        Assert.False(edit.Domain.HasValue);
        var partitions = app.Services.GetRequiredService<ConsoleServerSessionClientStore>();
        Assert.True(partitions.TryGet("operator-a", profile, out var partition));
        Assert.Equal(partition!.AuthenticatedSessionExpiresAt!.Value.ToUnixTimeSeconds(), edit.Expires!.Value.ToUnixTimeSeconds());
        context.Request.Headers.Cookie = string.Join("; ", cookies.Select(cookie => cookie.Name + "=" + cookie.Value));
        var target = new ConsoleHumanBrowserTarget
        {
            SelectedProfile = profile, AuthorityProfile = profile,
            Authority = ConsoleServerAuthority.Serving, Partition = partition
        };
        Assert.True(app.Services.GetRequiredService<ConsoleStudioEditBrowserChannel>().Authorizes(target));
        Assert.False(app.Services.GetRequiredService<ConsoleStudioEditBrowserChannel>().Authorizes(
            target with { SelectedProfile = profile with { UpdatedAt = profile.UpdatedAt.AddTicks(1) } }));
        Assert.True(app.Services.GetRequiredService<ConsoleApprovalBrowserChannel>().Authorizes(target));
    }
}
