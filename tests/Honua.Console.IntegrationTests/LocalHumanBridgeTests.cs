using System.Security.Claims;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Security;
using Honua.Console.Shell.Services;
using Honua.Console.Web.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Honua.Console.IntegrationTests;

public sealed class LocalHumanBridgeTests
{
    [Theory]
    [InlineData("Development", "Dev", true, "https://server/", true)]
    [InlineData("Production", "Dev", true, "https://server/", false)]
    [InlineData("Development", "Dev", false, "https://server/", false)]
    [InlineData("Development", "Edge", true, "https://server/", false)]
    [InlineData("Development", "Dev", true, "https://other/", false)]
    public async Task ExchangeRequiresExplicitLocalCookieAndTrustedTarget(string environment, string mode,
        bool enabled, string profileUri, bool expected)
    {
        var profiles = new InMemoryConsoleEnvironmentProfileStore([new ConsoleEnvironmentProfile
        {
            Id="profile", DisplayName="local", ServerBaseUri=new Uri(profileUri),
            Account=new ConsoleAccountBinding { AccountId="operator", AuthMode=ConsoleAccountAuthMode.AccountRbac }
        }], activeProfileId: "profile");
        var sessions = new InMemoryConsoleAccountSessionStore();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Honua:Console:Auth:Mode"] = mode,
            ["Honua:Console:Auth:LocalHumanBridge:Enabled"] = enabled.ToString(),
            ["HONUA_LOCAL_HUMAN_BRIDGE_KEY"] = new string('k', 40)
        }).Build();
        var transport = new Transport();
        var bridge = new ConsoleOperatorSessionBridge(profiles, sessions, new Uri("https://server/"),
            configuration, new Host(environment), transport);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "operator")], ConsoleAuthConstants.CookieScheme));
        await bridge.SyncAsync(principal);
        var session = await sessions.GetSessionAsync("profile");
        Assert.Equal(expected, transport.Called);
        Assert.Equal(expected, session!.AccessToken == "human-token");
    }

    private sealed class Transport : HttpMessageHandler, IHttpClientFactory
    {
        public bool Called { get; private set; }
        public HttpClient CreateClient(string name) => new(this, false);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Called = true;
            Assert.Equal("https://server/api/v1/admin/auth/local-console-session", request.RequestUri!.ToString());
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("X-API-Key"));
            Assert.Equal("{\"subject\":\"operator\"}", await request.Content!.ReadAsStringAsync(token));
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                "{\"accessToken\":\"human-token\",\"expiresAt\":\"2099-01-01T00:00:00Z\"}")
            };
        }
    }
    private sealed class Host(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
