using Honua.Console.Shell.DependencyInjection;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Honua.Console.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class ConsoleShellConfigurationTests
{
    [Theory]
    [InlineData("true", null, true)]
    [InlineData("false", null, false)]
    [InlineData(null, "true", true)]
    [InlineData(null, "false", false)]
    [InlineData(null, null, false)]
    [InlineData("invalid", null, false)]
    [InlineData(null, "invalid", false)]
    [InlineData("true", "false", true)]
    [InlineData("false", "true", false)]
    [InlineData("invalid", "true", false)]
    [InlineData("", "true", false)]
    [InlineData(" ", "true", false)]
    public void CatalogFlagBindsToSharedOptions(string? keyValue, string? environmentValue, bool expectedEnabled)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Honua:Console:Catalogs:Enabled"] = keyValue,
            ["HONUA_CONSOLE_CATALOGS_ENABLED"] = environmentValue
        }).Build();

        var shellOptions = ConsoleShellConfiguration.ReadOptions(configuration);

        Assert.Equal(expectedEnabled, shellOptions.CatalogAdminEnabled);
        var services = new ServiceCollection();
        services.AddHonuaConsoleShell(shellOptions);
        using var provider = services.BuildServiceProvider();
        Assert.Equal(expectedEnabled, provider.GetRequiredService<CatalogAdminOptions>().Enabled);
    }

    [Theory]
    [InlineData("true", null, true)]
    [InlineData("false", null, false)]
    [InlineData(null, "true", true)]
    [InlineData(null, "false", false)]
    [InlineData(null, null, true)]
    [InlineData("invalid", null, true)]
    [InlineData(null, "invalid", true)]
    [InlineData("true", "false", true)]
    [InlineData("false", "true", false)]
    [InlineData("invalid", "false", true)]
    [InlineData("", "false", true)]
    [InlineData(" ", "false", true)]
    public void V4FlagBindsToSharedOptions(string? keyValue, string? environmentValue, bool expectedEnabled)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Honua:Console:V4:Enabled"] = keyValue,
            ["HONUA_CONSOLE_V4_ENABLED"] = environmentValue
        }).Build();

        var shellOptions = ConsoleShellConfiguration.ReadOptions(configuration);

        Assert.Equal(expectedEnabled, shellOptions.V4Enabled);
        var services = new ServiceCollection();
        services.AddHonuaConsoleShell(shellOptions);
        using var provider = services.BuildServiceProvider();
        Assert.Equal(expectedEnabled, provider.GetRequiredService<ConsoleV4Options>().Enabled);
    }

    [Fact]
    public async Task BoundShell_ReadsPublicOpenDataThroughAnonymousRouteWithoutApiKey()
    {
        var handler = new RecordingHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IHonuaServerBoundClientFactory>(new RecordingClientFactory(handler));
        services.AddHonuaConsoleShell(new HonuaConsoleShellOptions { ServerBaseUrl = "https://server.honua.test/", ServerAdminApiKey = "shared-admin-secret" });
        using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<IConsoleCatalogClient>().GetOpenDataItemAsync("svc-1");

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/api/v1/open-data/datasets/svc-1", request.RequestUri!.AbsolutePath);
        Assert.False(request.Headers.Contains("X-API-Key"));
    }

    private sealed class RecordingClientFactory(HttpMessageHandler handler) : IHonuaServerBoundClientFactory
    {
        public HttpClient CreateServerBoundClient(Uri baseUri, TimeSpan? timeout = null) => Create(baseUri);

        public HttpClient CreatePublicClient(Uri baseUri, TimeSpan? timeout = null) => Create(baseUri);

        private HttpClient Create(Uri baseUri) => new(handler, disposeHandler: false) { BaseAddress = baseUri };
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }
    }
}
