using System.Net;
using System.Text;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Shell.Tests;

public sealed class OperateEnvironmentEndpointTests
{
    [Theory]
    [InlineData("services")]
    [InlineData("layers")]
    [InlineData("workspace")]
    public async Task ServiceEndpointsFollowTheEnvironmentUsedForTheRead(string surface)
    {
        using var fixture = new EnvironmentFixture();
        await fixture.Profiles.ActivateProfileAsync("active");
        var (services, _) = await ReadAsync(fixture.Source, surface);
        var service = Assert.Single(services);
        Assert.Equal("https://active.example/active-mount/rest/services/Places%20North/FeatureServer",
            service.PublicationSlots.Single(slot => slot.Name == "FeatureServer").Url);
        Assert.Equal("https://active.example/active-mount/ogc/services/Places%20North/wms",
            service.PublicationSlots.Single(slot => slot.Name == "Wms").Url);
        Assert.Equal("https://active.example/active-mount/ogc/features",
            service.PublicationSlots.Single(slot => slot.Name == "OgcFeatures").Url);
        Assert.NotEmpty(fixture.Handler.Requests);
        Assert.All(fixture.Handler.Requests, uri => Assert.StartsWith("https://active.example/active-mount/", uri.AbsoluteUri));
    }

    [Theory]
    [InlineData("services")]
    [InlineData("layers")]
    [InlineData("workspace")]
    public async Task FolderPublicPathsUseTheActiveEnvironmentAndKeepLogicalIdentity(string surface)
    {
        using var fixture = new EnvironmentFixture();
        fixture.Handler.ServiceJson = """{"success":true,"data":{"items":[{"serviceName":"logical-id","title":"Display title","publicName":"Public parcels","folderId":"survey","folderName":"Survey North","folderMutationIssues":["Review publication path."],"layerCount":0,"enabledProtocols":["FeatureServer","Wms"]}],"page":1,"pageSize":100,"total":1}}""";
        await fixture.Profiles.ActivateProfileAsync("active");
        var (services, _) = await ReadAsync(fixture.Source, surface);
        var service = Assert.Single(services);
        Assert.Equal("logical-id", service.Name);
        Assert.Equal("Display title", service.DisplayName);
        Assert.Equal("survey", service.FolderId);
        Assert.Equal("Review publication path.", Assert.Single(service.FolderMutationIssues));
        Assert.Equal("https://active.example/active-mount/rest/services/Survey%20North/Public%20parcels/FeatureServer",
            service.PublicationSlots.Single(slot => slot.Name == "FeatureServer").Url);
        Assert.Equal("https://active.example/active-mount/ogc/services/Survey%20North/Public%20parcels/wms",
            service.PublicationSlots.Single(slot => slot.Name == "Wms").Url);
    }

    [Theory]
    [InlineData("services", "switch")]
    [InlineData("layers", "switch")]
    [InlineData("workspace", "switch")]
    [InlineData("services", "switch-back")]
    [InlineData("services", "account")]
    [InlineData("services", "mount")]
    public async Task AnEnvironmentChangeDuringTheReadDiscardsItsProjection(string surface, string change)
    {
        using var fixture = new EnvironmentFixture();
        await fixture.Profiles.ActivateProfileAsync("active");
        fixture.Handler.BeforeServiceResponse = async () =>
        {
            if (change is "switch" or "switch-back")
            {
                await fixture.Profiles.ActivateProfileAsync("other");
                if (change == "switch-back") await fixture.Profiles.ActivateProfileAsync("active");
            }
            else
            {
                var profile = (await fixture.Profiles.GetActiveProfileAsync())!;
                await fixture.Profiles.UpsertProfileAsync(change == "account"
                    ? profile with { Account = profile.Account with { AccountId = "other-account" } }
                    : profile with { ServerBaseUri = new Uri("https://active.example/other-mount/") });
            }
        };
        var (services, states) = await ReadAsync(fixture.Source, surface);
        Assert.Empty(services);
        Assert.Contains(states, state => state.State == "Unavailable" && state.Contract == "Active Environment");
    }

    [Theory]
    [InlineData("services")]
    [InlineData("layers")]
    [InlineData("workspace")]
    public async Task EmptyProfileStorePreservesTheStartupBinding(string surface)
    {
        using var fixture = new EnvironmentFixture(new InMemoryConsoleEnvironmentProfileStore([]));
        var (services, states) = await ReadAsync(fixture.Source, surface);
        AssertStartupBinding(services, states);
    }

    [Theory]
    [InlineData("services")]
    [InlineData("layers")]
    [InlineData("workspace")]
    public async Task NoProfileStorePreservesTheStartupBinding(string surface)
    {
        using var fixture = new EnvironmentFixture();
        var source = new HonuaServerOperateTransitionDataSource(fixture.Client);
        var (services, states) = await ReadAsync(source, surface);
        AssertStartupBinding(services, states);
    }

    [Theory]
    [InlineData("services")]
    [InlineData("layers")]
    [InlineData("workspace")]
    public async Task ProfileChangesWithoutAnEventDiscardTheRead(string surface)
    {
        var profiles = new QuietProfileStore();
        using var fixture = new EnvironmentFixture(profiles);
        fixture.Handler.BeforeServiceResponse = () =>
        {
            profiles.Active = profiles.Active with { ServerBaseUri = new Uri("https://other.example/") };
            return Task.CompletedTask;
        };
        var (services, states) = await ReadAsync(fixture.Source, surface);
        Assert.Empty(services);
        Assert.Contains(states, state => state.Contract == "Active Environment");
    }

    private static void AssertStartupBinding(
        IReadOnlyList<OperateServiceDetail> services, IReadOnlyList<OperateCapabilityState> states)
    {
        var service = Assert.Single(services);
        Assert.Equal("https://startup.example/startup-mount/rest/services/Places%20North/FeatureServer",
            service.PublicationSlots.Single(slot => slot.Name == "FeatureServer").Url);
        Assert.DoesNotContain(states, state => state.Contract == "Active Environment");
    }

    private sealed class QuietProfileStore : IConsoleEnvironmentProfileStore
    {
        public ConsoleEnvironmentProfile Active { get; set; } = new()
        {
            Id = "active",
            ServerBaseUri = new Uri("https://active.example/active-mount/")
        };

        public Task<ConsoleEnvironmentProfile?> GetActiveProfileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<ConsoleEnvironmentProfile?>(Active);
        public Task<IReadOnlyList<ConsoleEnvironmentProfile>> ListProfilesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ConsoleEnvironmentProfile>>([Active]);
        public Task<ConsoleEnvironmentProfile?> GetProfileAsync(string profileId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ConsoleEnvironmentProfile?>(Active.Id == profileId ? Active : null);
        public Task UpsertProfileAsync(ConsoleEnvironmentProfile profile, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task ActivateProfileAsync(string profileId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<ConsoleEnvironmentState?> GetStateAsync(string profileId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task SaveStateAsync(ConsoleEnvironmentState state, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static async Task<(IReadOnlyList<OperateServiceDetail>, IReadOnlyList<OperateCapabilityState>)> ReadAsync(
        HonuaServerOperateTransitionDataSource source, string surface)
    {
        if (surface == "workspace")
        {
            var workspace = await source.GetWorkspaceAsync();
            return (workspace.Services, workspace.CapabilityStates);
        }
        var view = surface == "layers" ? await source.GetLayersViewAsync() : await source.GetServicesViewAsync();
        return (view.Services, view.CapabilityStates);
    }

    private sealed class EnvironmentFixture : IDisposable
    {
        private readonly HttpClient _http;
        private readonly ServiceProvider _provider;
        public IConsoleEnvironmentProfileStore Profiles { get; }
        public HonuaAdminOperateHttpClient Client { get; }
        public EndpointHandler Handler { get; } = new();
        public HonuaServerOperateTransitionDataSource Source { get; }

        public EnvironmentFixture(IConsoleEnvironmentProfileStore? profiles = null)
        {
            Profiles = profiles ?? new InMemoryConsoleEnvironmentProfileStore([
                new() { Id = "startup", ServerBaseUri = new Uri("https://startup.example/startup-mount/") },
                new() { Id = "active", ServerBaseUri = new Uri("https://active.example/active-mount/") },
                new() { Id = "other", ServerBaseUri = new Uri("https://other.example/third-mount/") }
            ]);
            var startup = new Uri("https://startup.example/startup-mount/");
            _http = new HttpClient(new HonuaServerBindingHandler(Profiles, new InMemoryConsoleAccountSessionStore(), startup)
            { InnerHandler = Handler })
            { BaseAddress = startup };
            Client = new HonuaAdminOperateHttpClient(_http, new HonuaAdminOperateClientOptions(startup, null));
            _provider = new ServiceCollection().AddSingleton<IConsoleEnvironmentProfileStore>(Profiles)
                .AddSingleton<IHonuaAdminOperateClient>(Client).AddSingleton<HonuaServerOperateTransitionDataSource>()
                .BuildServiceProvider();
            Source = _provider.GetRequiredService<HonuaServerOperateTransitionDataSource>();
        }

        public void Dispose()
        {
            _provider.Dispose();
            _http.Dispose();
        }
    }

    private sealed class EndpointHandler : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        public Func<Task>? BeforeServiceResponse { get; set; }
        public string? ServiceJson { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            var path = request.RequestUri!.AbsolutePath;
            string json;
            if (path.EndsWith("/services/", StringComparison.Ordinal))
            {
                if (BeforeServiceResponse is { } before) await before();
                json = ServiceJson ?? """{"success":true,"data":{"items":[{"serviceName":"Places North","layerCount":0,"enabledProtocols":["FeatureServer","Wms","OgcFeatures"]}],"page":1,"pageSize":100,"total":1}}""";
            }
            else if (path.EndsWith("/connections/", StringComparison.Ordinal)) json = """{"success":true,"data":[]}""";
            else if (path.EndsWith("/settings", StringComparison.Ordinal)) json = """{"success":true,"data":{"serviceName":"Places North"}}""";
            else return new(HttpStatusCode.NotFound);
            return new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
