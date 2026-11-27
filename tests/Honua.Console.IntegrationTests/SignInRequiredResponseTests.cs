using System.Net;
using Bunit;
using Honua.Console.Web;
using Honua.Console.Web.Auth;
using Honua.Console.Web.Components;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class SignInRequiredResponseTests
{
    [Theory]
    [InlineData("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8")]
    [InlineData("*/*")]
    [InlineData(null)]
    public void BrowserRequest_GetsTheSignInPage(string? accept)
    {
        var result = ConsoleAuthentication.SignInRequired(Request(accept));

        var page = Assert.IsType<RazorComponentResult<SignInRequiredDocument>>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, page.StatusCode);
    }

    [Fact]
    public void ApiRequest_KeepsTheProblemDetails()
    {
        var result = ConsoleAuthentication.SignInRequired(Request("application/json"));

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, problem.StatusCode);
        Assert.Equal("Sign-in required", problem.ProblemDetails.Title);
    }

    [Theory]
    [InlineData("/map-proxy/env-1/styles/pub.json")]
    [InlineData("/scene-proxy/env-1/scenes/s1/tileset.json")]
    [InlineData("/api/agent-workspace/session")]
    public void ProxyAndApiPaths_Return401ProblemDetailsInsteadOfHtmlRedirect(string path)
    {
        var request = Request("text/html", path);

        Assert.True(ConsoleAuthentication.IsApiRequest(request));
        var problem = Assert.IsType<ProblemHttpResult>(ConsoleAuthentication.SignInRequired(request));
        Assert.Equal(StatusCodes.Status401Unauthorized, problem.StatusCode);
    }

    [Fact]
    public async Task LogoutEndpoint_RequiresAntiforgeryToken()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Microsoft.Extensions.Hosting.Environments.Development
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.AddLegacyConsoleServices();
        builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-Token");
        await using var app = builder.Build();
        app.UseConsoleAuthentication();
        app.UseAntiforgery();
        app.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) => antiforgery.GetAndStoreTokens(context).RequestToken!)
            .AllowAnonymous();
        app.MapConsoleAuthEndpoints();
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var cookies = new CookieContainer();
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = cookies })
        {
            BaseAddress = new Uri(address)
        };

        using var login = await client.GetAsync("/auth/login");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var missingCsrf = await client.PostAsync("/auth/logout", content: null);
        Assert.Equal(HttpStatusCode.Forbidden, missingCsrf.StatusCode);

        var token = await client.GetStringAsync("/csrf");
        using var withCsrfRequest = new HttpRequestMessage(HttpMethod.Post, "/auth/logout");
        withCsrfRequest.Headers.Add("X-CSRF-Token", token);
        using var withCsrf = await client.SendAsync(withCsrfRequest);
        Assert.Equal(HttpStatusCode.Redirect, withCsrf.StatusCode);
        Assert.Equal("/auth/login", withCsrf.Headers.Location?.OriginalString);
    }

    [Fact]
    public void SignInPage_ExplainsTheEdgeSignInAndKeepsTheDetailBehindDiagnostics()
    {
        using var ctx = new BunitContext();

        var page = ctx.Render<SignInRequiredDocument>(p => p.Add(x => x.Detail, "No forwarded operator identity."));

        Assert.Equal("Sign in to Honua Console", page.Find("h1").TextContent);
        Assert.Contains("No forwarded operator identity.", page.Find("[data-sign-in-diagnostics]").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("rfc9110", page.Markup, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpRequest Request(string? accept, string path = "/auth/login")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        if (accept is not null)
        {
            context.Request.Headers.Accept = accept;
        }

        return context.Request;
    }
}
