using Bunit;
using Honua.Console.Web.Auth;
using Honua.Console.Web.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

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

    [Fact]
    public void SignInPage_ExplainsTheEdgeSignInAndKeepsTheDetailBehindDiagnostics()
    {
        using var ctx = new BunitContext();

        var page = ctx.Render<SignInRequiredDocument>(p => p.Add(x => x.Detail, "No forwarded operator identity."));

        Assert.Equal("Sign in to Honua Console", page.Find("h1").TextContent);
        Assert.Contains("No forwarded operator identity.", page.Find("[data-sign-in-diagnostics]").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("rfc9110", page.Markup, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpRequest Request(string? accept)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/auth/login";
        if (accept is not null)
        {
            context.Request.Headers.Accept = accept;
        }

        return context.Request;
    }
}
