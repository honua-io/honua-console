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
/// Docker-free render coverage for the Share home surface (<c>/share</c>, <see cref="SharePage"/>) matching
/// the <c>screens-share.jsx</c> <c>ShareHome</c> mockup: a KPI summary strip, a shared-items toolbar, and a
/// shared-items table with unambiguous visibility/open-data state and a per-item Configure action. Drives the
/// real <see cref="HonuaServerConsoleCatalogClient"/> over a recording handler so the binding + mapper run end
/// to end; a server-unavailable read renders the empty surface rather than fabricating shared content.
/// </summary>
public sealed class ShareHomeRenderTests
{
    private static readonly Uri BaseUri = new("https://console.honua.test");

    [Fact]
    public void ShareHome_BoundToServer_RendersKpiStripAndSharedItemsTable()
    {
        var handler = new StubHandler(_ => Envelope(new HonuaConsoleContentListResponse
        {
            Total = 2,
            Items =
            [
                OpenDataItem("svc-parcels", "Tax Parcels (FY 2024)", "service"),
                OpenDataItem("lyr-wetlands", "Wetlands Inventory", "layer")
            ]
        }));
        using var ctx = new Bunit.TestContext();
        RegisterCatalog(ctx, handler);

        var page = ctx.RenderComponent<SharePage>();

        page.WaitForAssertion(
            () => Assert.Contains("data-share-home-table", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // KPI summary strip per the ShareHome mockup.
        Assert.Contains("data-share-kpis", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-share-kpi=\"shared-items\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-share-kpi=\"open-data-pages\"", page.Markup, StringComparison.Ordinal);

        // Each shared item renders a row with an unambiguous public/open-data state and a Configure link.
        Assert.Contains("data-share-row=\"svc-parcels\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-share-row=\"lyr-wetlands\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Tax Parcels (FY 2024)", page.Markup, StringComparison.Ordinal);
        Assert.Contains("/share/manage?itemId=svc-parcels", page.Markup, StringComparison.Ordinal);
        // Visibility column is unambiguous for the public open-data items.
        Assert.Contains("data-share-visibility", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShareHome_WhenServerUnavailable_RendersEmptySurfaceNotMockData()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var ctx = new Bunit.TestContext();
        RegisterCatalog(ctx, handler);

        var page = ctx.RenderComponent<SharePage>();

        page.WaitForAssertion(
            () => Assert.Contains("No public open-data items", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        // The merged runtime never falls back to seeded demo content on a server failure.
        Assert.DoesNotContain("data-share-home-table", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("data-share-kpis", page.Markup, StringComparison.Ordinal);
    }

    private static void RegisterCatalog(Bunit.TestContext ctx, StubHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = BaseUri };
        var client = new HonuaConsoleContentHttpClient(httpClient, new HonuaConsoleContentClientOptions(BaseUri, "admin-key"));
        ctx.Services.AddSingleton<IConsoleCatalogClient>(new HonuaServerConsoleCatalogClient(client));
    }

    private static HonuaConsoleContentItem OpenDataItem(string id, string title, string itemType) =>
        new()
        {
            Id = id,
            Name = id,
            Title = title,
            Description = $"{title} open-data description",
            ItemType = itemType,
            Visibility = "public",
            OwnerId = "owner-1",
            Actions = ["view"],
            Tags = ["open-data"],
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

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
