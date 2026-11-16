using System.Net;
using System.Net.Http;
using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.DependencyInjection;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class PublicCapabilityRenderTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task AnonymousPublicItem_UsesLiveManifestAndFailsClosedOnReadFailure(HttpStatusCode manifestStatus)
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        using var wire = new PublicReadFactory(manifestStatus);
        ctx.Services.AddSingleton<IHonuaServerBoundClientFactory>(wire);
        ctx.Services.AddHonuaConsoleShell(new HonuaConsoleShellOptions
        {
            ServerBaseUrl = "https://server.honua.test/",
            V4Enabled = true
        });
        var manifest = ctx.Services.GetRequiredService<IConsoleCapabilityManifest>();
        await manifest.RefreshAsync();

        var page = ctx.Render<SharePublicItemPage>(parameters => parameters.Add(p => p.IdOrSlug, "public-layer"));

        Assert.Contains("/api/v1/capabilities/manifest", wire.Paths);
        Assert.Equal(manifestStatus == HttpStatusCode.OK, manifest.IsAdvertised(ConsoleCapabilityKeys.PersistentCatalog));
        if (manifestStatus == HttpStatusCode.OK)
        {
            page.WaitForAssertion(() => Assert.Equal("Public layer", page.Find("h1").TextContent));
            Assert.Single(page.FindAll("[data-share-opendata-landing]"));
        }
        else
        {
            Assert.Equal(CatalogAvailabilityGate.Title, page.Find("h1").TextContent);
            Assert.Empty(page.FindAll("[data-share-opendata-landing]"));
        }
    }

    private sealed class PublicReadFactory(HttpStatusCode manifestStatus) : HttpMessageHandler, IHonuaServerBoundClientFactory
    {
        public List<string> Paths { get; } = [];

        public HttpClient CreateServerBoundClient(Uri baseUri, TimeSpan? timeout = null) =>
            throw new HttpRequestException("Anonymous callers cannot use privileged transport.", null, HttpStatusCode.Unauthorized);

        public HttpClient CreatePublicClient(Uri baseUri, TimeSpan? timeout = null) =>
            new(this, disposeHandler: false) { BaseAddress = baseUri };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            var isManifest = path == "/api/v1/capabilities/manifest";
            return Task.FromResult(new HttpResponseMessage(isManifest ? manifestStatus : HttpStatusCode.OK)
            {
                Content = new StringContent(isManifest ? Manifest : PublicItem)
            });
        }

        private const string Manifest = """
            {"schemaVersion":"honua.capability_manifest.v1","capabilities":[{
              "id":"console.content.persistent","lifecycle":"Implemented","optInRequired":false,
              "available":true,"supported":true}]}
            """;

        private const string PublicItem = """
            {"success":true,"data":{"id":"public-layer","name":"public-layer","title":"Public layer",
              "itemType":"layer","visibility":"public","ownerId":"publisher","actions":["view"],"tags":[]}}
            """;
    }
}
