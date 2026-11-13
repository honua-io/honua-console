using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Web.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class ApiKeySessionLifetimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SessionChangeErasesAnAlreadyDisplayedSecret(bool reauthenticate)
    {
        using var context = new BunitContext();
        var session = Register(context);
        var page = CreateKey(context);
        if (reauthenticate) SignIn(session, null);
        else session.Clear();
        AssertCleared(page);
    }

    [Fact]
    public void SessionExpiryErasesAnAlreadyDisplayedSecretWithoutAnotherRequest()
    {
        using var context = new BunitContext();
        Register(context, DateTimeOffset.UtcNow.AddSeconds(3));
        var page = CreateKey(context);
        AssertCleared(page);
    }

    private static ConsoleBrowserSession Register(BunitContext context, DateTimeOffset? expiry = null)
    {
        var api = new Uri("https://console.example/");
        var session = new ConsoleBrowserSession();
        SignIn(session, expiry);
        context.Services.AddSingleton(session).AddBrowserAuthorityBinding(api);
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        context.Services.AddSingleton<IHonuaAdminOperateClient>(new HonuaAdminOperateHttpClient(new HttpClient(new ApiHandler()), new(api)));
        return session;
    }

    private static IRenderedComponent<OperateApiKeysPage> CreateKey(BunitContext context)
    {
        var page = context.RenderComponent<OperateApiKeysPage>();
        page.Find("#api-key-name").Change("Reporting app");
        page.Find("#api-key-permissions").Change("admin:read");
        page.Find("form").Submit();
        page.WaitForAssertion(() => Assert.Equal("sensitive-key", page.Find("#api-key-secret").GetAttribute("value")));
        return page;
    }

    private static void AssertCleared(IRenderedComponent<OperateApiKeysPage> page) => page.WaitForAssertion(() =>
    {
        Assert.Empty(page.FindAll("#api-key-secret"));
        Assert.Empty(page.FindAll("form"));
        Assert.DoesNotContain("Reporting app", page.Markup);
        Assert.Contains("Target changed", page.Markup);
    }, TimeSpan.FromSeconds(10));

    private static void SignIn(ConsoleBrowserSession session, DateTimeOffset? expiry) => session.Set(
        new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "operator")], "test")),
        new ConsoleSessionCredentials(CsrfToken: "csrf", ExpiresAt: expiry));

    private sealed class ApiHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            object data = request.Method == HttpMethod.Get ? Array.Empty<HonuaAdminApiKeyResponse>() : new
            {
                apiKey = new HonuaAdminApiKeyResponse { Id = Guid.NewGuid(), Name = "Reporting app", Permissions = ["admin:read"], Status = "active" },
                key = "sensitive-key"
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { success = true, data }), Encoding.UTF8, "application/json")
            });
        }
    }
}
