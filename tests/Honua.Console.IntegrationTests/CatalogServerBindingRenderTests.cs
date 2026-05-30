using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for the Catalog page bound to honua-server's Console metadata v2 content
/// + RBAC API (issue #7, honua-server#1162). Drives the real <see cref="HonuaServerConsoleCatalogClient"/>
/// over a recording <see cref="HttpMessageHandler"/> so the test exercises the live binding + mapper end
/// to end, asserting the catalog table renders server-mapped items (itemType/visibility/RBAC projection)
/// and that a server-unavailable read falls back to the empty surface instead of fabricating content.
/// </summary>
public sealed class CatalogServerBindingRenderTests
{
    private static readonly Uri BaseUri = new("https://console.honua.test");

    [Fact]
    public void Catalog_BoundToServer_RendersLiveMappedItems()
    {
        var handler = new StubHandler(_ => Envelope(new HonuaConsoleContentListResponse
        {
            Total = 2,
            Items =
            [
                ServerItem("svc-1", "Coastal Flood Service", "service", "public", ["view"]),
                ServerItem("map-1", "Storm Response Map", "saved-map", "organization", ["view", "edit", "publish"])
            ]
        }));
        using var ctx = new Bunit.TestContext();
        RegisterCatalog(ctx, handler, anonymous: false);

        var page = ctx.RenderComponent<CatalogPage>();

        page.WaitForAssertion(
            () => Assert.Contains("Coastal Flood Service", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Storm Response Map", page.Markup, StringComparison.Ordinal);
        // saved-map item type projects onto the Console "Maps" type label.
        Assert.Contains("Maps", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_WhenServerUnavailable_RendersEmptySurfaceNotMockData()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var ctx = new Bunit.TestContext();
        RegisterCatalog(ctx, handler, anonymous: false);

        var page = ctx.RenderComponent<CatalogPage>();

        page.WaitForAssertion(
            () => Assert.Contains("No content matched", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        // The merged runtime never falls back to seeded demo content on a server failure.
        Assert.DoesNotContain("Coastal Flood Service", page.Markup, StringComparison.Ordinal);
    }

    private static void RegisterCatalog(Bunit.TestContext ctx, StubHandler handler, bool anonymous)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = BaseUri };
        var client = new HonuaConsoleContentHttpClient(httpClient, new HonuaConsoleContentClientOptions(BaseUri, "admin-key"));
        ctx.Services.AddSingleton<IConsoleCatalogClient>(new HonuaServerConsoleCatalogClient(client));
        ctx.Services.AddSingleton<IConsoleCatalogReadContextResolver>(new StubReadContextResolver(anonymous));
    }

    private static HonuaConsoleContentItem ServerItem(string id, string title, string itemType, string visibility, string[] actions) =>
        new()
        {
            Id = id,
            Name = id,
            Title = title,
            Description = $"{title} description",
            ItemType = itemType,
            Visibility = visibility,
            OwnerId = "owner-1",
            Actions = actions,
            UpdatedAt = new DateTimeOffset(2026, 5, 24, 8, 0, 0, TimeSpan.Zero)
        };

    private static HttpResponseMessage Envelope<T>(T data)
    {
        var json = JsonSerializer.Serialize(new EnvelopeDto<T>(true, data, null));
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed record EnvelopeDto<T>(bool success, T data, string? message);

    private sealed class StubReadContextResolver(bool anonymous) : IConsoleCatalogReadContextResolver
    {
        public Task<CatalogReadContext> ResolveAsync(string? publicLinkToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(anonymous ? CatalogReadContext.AnonymousPublicLink(publicLinkToken) : CatalogReadContext.Authenticated);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
