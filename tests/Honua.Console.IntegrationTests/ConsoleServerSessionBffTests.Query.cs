using System.Net;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Honua.Console.Web.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Honua.Console.IntegrationTests;

public sealed partial class ConsoleServerSessionBffTests
{
    [Fact]
    public async Task Login_ManagementQueryAndProviderLink_PreserveAuthorityAndOriginalManagerProposal()
    {
        var profile = Profile("environment");
        var profiles = new InMemoryConsoleEnvironmentProfileStore([profile], activeProfileId: profile.Id);
        await using var store = new ConsoleServerSessionClientStore(TimeProvider.System, (_, _) => new ProviderSelectionHandler());
        var coordinator = new ConsoleServerSessionBffCoordinator(new SwitchableOperatorContext("operator"), profiles,
            new InMemoryConsoleAccountSessionStore(), store, TimeProvider.System, management: new MutableManagementEndpoint());
        await using var host = await SignInHost.StartAsync(coordinator);
        const string returnTo = "/inbox?proposalId=manager%3Aproposal";
        var login = QueryHelpers.AddQueryString("/auth/server/login", new Dictionary<string, string?>
        {
            ["profileId"] = profile.Id, ["authority"] = "Management", ["returnTo"] = returnTo
        });

        using var choices = await host.Client.GetAsync(login);
        Assert.Equal(HttpStatusCode.OK, choices.StatusCode);
        var html = await choices.Content.ReadAsStringAsync();
        var links = html.Split("href=\"", StringSplitOptions.None).Skip(1)
            .Select(part => WebUtility.HtmlDecode(part.Split('"')[0])).ToArray();
        Assert.Equal(2, links.Length);
        foreach (var link in links)
        {
            var query = QueryHelpers.ParseQuery(new Uri(host.Client.BaseAddress!, link).Query);
            Assert.Equal("Management", query["authority"].ToString());
            Assert.Equal(returnTo, query["returnTo"].ToString());
        }
        using var selected = await host.Client.GetAsync(links[0]);
        Assert.Equal(HttpStatusCode.Redirect, selected.StatusCode);
        Assert.True(store.TryConsumePending(State(selected.Headers.Location!.AbsoluteUri), "operator", out var pending));
        Assert.Equal(ConsoleServerAuthority.Management, pending!.Authority);
        Assert.Equal(returnTo, pending.ReturnTo);
    }

    private sealed class ProviderSelectionHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("manager.example", request.RequestUri!.Host);
            var body = request.RequestUri.AbsolutePath.EndsWith("/config", StringComparison.Ordinal)
                ? """{"oidcEnabled":true,"providers":[{"key":"first","displayName":"First"},{"key":"second","displayName":"Second"}]}"""
                : """{"authorizeUrl":"https://idp.example/authorize?state=provider-query-state"}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class SignInHost(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client => client;

        public static async Task<SignInHost> StartAsync(ConsoleServerSessionBffCoordinator coordinator)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddSingleton(coordinator);
            var app = builder.Build();
            app.MapConsoleServerSessionBff();
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return new(app, new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(address) });
        }

        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await app.DisposeAsync();
        }
    }
}
