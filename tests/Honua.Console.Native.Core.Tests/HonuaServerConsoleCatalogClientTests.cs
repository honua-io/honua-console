using System.Net;
using System.Text;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Host-independent coverage for the server-bound catalog/content client (issue #7). Drives the real
/// <see cref="HonuaConsoleContentHttpClient"/> over a recording <see cref="HttpMessageHandler"/> and the
/// <see cref="HonuaServerConsoleCatalogClient"/> over it, asserting the live binding speaks the real
/// honua-server Console metadata v2 content + RBAC contract (honua-server#1162): query/auth wiring, the
/// {success,data,message} envelope, itemType/visibility/RBAC-verb projection, anonymous visibility
/// filtering, and the typed unsupported/unavailable/missing failure paths — never fabricated content.
/// </summary>
public sealed class HonuaServerConsoleCatalogClientTests
{
    private static readonly Uri BaseUri = new("https://console.honua.test");

    [Fact]
    public async Task Search_SendsAdminAuthAndProjectedFilters_AndMapsRbacBackedSummaries()
    {
        HttpRequestMessage? captured = null;
        var handler = new RecordingHandler(request =>
        {
            captured = request;
            return Envelope(new HonuaConsoleContentListResponse
            {
                Total = 2,
                Items =
                [
                    Item("svc-1", "coastal", "service", "public", actions: ["view", "embed"]),
                    Item("map-1", "storm", "saved-map", "organization", actions: ["view", "edit", "publish", "share", "administer"])
                ]
            });
        });
        var catalog = CreateCatalog(handler, apiKey: "admin-secret");

        var result = await catalog.SearchAsync(
            new CatalogListRequest { Query = "flood", Type = "map", Sharing = CatalogSharingTiers.Organization, Owner = "resilience" },
            CatalogReadContext.Authenticated);

        Assert.NotNull(captured);
        Assert.Equal("/api/v1/console/content/", captured!.RequestUri!.AbsolutePath);
        var query = captured.RequestUri.Query;
        // Console catalog "map" projects onto the server saved-map item type; visibility maps to the
        // server scope; owner/q pass straight through.
        Assert.Contains("itemType=saved-map", query, StringComparison.Ordinal);
        Assert.Contains("visibility=organization", query, StringComparison.Ordinal);
        Assert.Contains("owner=resilience", query, StringComparison.Ordinal);
        Assert.Contains("q=flood", query, StringComparison.Ordinal);
        Assert.True(captured.Headers.TryGetValues("X-API-Key", out var keys));
        Assert.Equal("admin-secret", keys!.Single());

        // saved-map item type projects onto the Console "map" type; RBAC verbs drive resolved role.
        var map = Assert.Single(result.Items, summary => summary.Id == "map-1");
        Assert.Equal("map", map.Type);
        Assert.Equal("owner", map.ResolvedRole);
        Assert.True(map.ViewerSupport.CanOpenInViewer);
        Assert.True(map.ViewerSupport.CanEditInStudio);
        Assert.Equal(CatalogSharingTiers.Organization, map.Access.Sharing);

        var service = Assert.Single(result.Items, summary => summary.Id == "svc-1");
        Assert.Equal("service", service.Type);
        Assert.Equal("viewer", service.ResolvedRole);
        Assert.True(service.Access.Embeddable);
        Assert.Equal(2, result.TypeCounts.Values.Sum());
    }

    [Fact]
    public async Task Search_AnonymousContext_HidesNonPublicServerItems()
    {
        var handler = new RecordingHandler(_ => Envelope(new HonuaConsoleContentListResponse
        {
            Total = 2,
            Items =
            [
                Item("pub-1", "open", "service", "public", actions: ["view"]),
                Item("org-1", "internal", "dashboard", "organization", actions: ["view"])
            ]
        }));
        var catalog = CreateCatalog(handler);

        var result = await catalog.SearchAsync(
            new CatalogListRequest(),
            CatalogReadContext.AnonymousPublicLink(token: null));

        Assert.Contains(result.Items, summary => summary.Id == "pub-1");
        Assert.DoesNotContain(result.Items, summary => summary.Id == "org-1");
    }

    [Fact]
    public async Task GetCatalogItem_MapsProvenanceAndLifecycleCapabilities()
    {
        var handler = new RecordingHandler(_ => Envelope(Item(
            "rep-1",
            "quarterly",
            "report",
            "team",
            actions: ["view", "edit"],
            lifecycle: "published",
            provenance: [new HonuaConsoleProvenanceRef { Kind = "studio-artifact", ItemId = "draft-9", Rel = "generated-by" }])));
        var catalog = CreateCatalog(handler);

        var item = await catalog.GetCatalogItemAsync("rep-1", CatalogReadContext.Authenticated);

        Assert.Equal(CatalogReadStatus.Allowed, item.Status);
        Assert.Equal("report", item.Item!.Summary.Type);
        Assert.Equal(CatalogSharingTiers.Group, item.Item.Summary.Access.Sharing);
        Assert.Contains("metadata-v2", item.Item.Capabilities);
        Assert.Contains("lifecycle:published", item.Item.Capabilities);
        var lineage = Assert.Single(item.Item.Lineage);
        Assert.Equal("draft-9", lineage.ItemId);
        Assert.Equal("consumer", lineage.Direction);
    }

    [Fact]
    public async Task GetCatalogItem_AnonymousReadOfOrganizationItem_IsUnavailable()
    {
        var handler = new RecordingHandler(_ => Envelope(Item("org-1", "internal", "dashboard", "organization", actions: ["view"])));
        var catalog = CreateCatalog(handler);

        var item = await catalog.GetCatalogItemAsync("org-1", CatalogReadContext.AnonymousPublicLink(token: null));

        Assert.Equal(CatalogReadStatus.Unavailable, item.Status);
        Assert.Null(item.Item);
    }

    [Fact]
    public async Task GetCatalogItem_ServerNotFound_MapsToMissingStatus()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var catalog = CreateCatalog(handler);

        var item = await catalog.GetCatalogItemAsync("ghost", CatalogReadContext.Authenticated);

        Assert.Equal(CatalogReadStatus.Missing, item.Status);
    }

    [Fact]
    public async Task GetCatalogItem_ServerForbidden_MapsToForbiddenStatus()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var catalog = CreateCatalog(handler);

        var item = await catalog.GetCatalogItemAsync("locked", CatalogReadContext.Authenticated);

        Assert.Equal(CatalogReadStatus.Forbidden, item.Status);
    }

    [Fact]
    public async Task Search_ServerUnavailable_ReturnsEmptyResultNotMockData()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var catalog = CreateCatalog(handler);

        var result = await catalog.SearchAsync(new CatalogListRequest(), CatalogReadContext.Authenticated);

        Assert.Empty(result.Items);
        Assert.Empty(result.TypeCounts);
    }

    [Fact]
    public async Task GetMapPackage_NonSavedMapItem_IsMissing()
    {
        var handler = new RecordingHandler(_ => Envelope(Item("svc-1", "service-item", "service", "public", actions: ["view"])));
        var catalog = CreateCatalog(handler);

        var map = await catalog.GetMapPackageAsync("svc-1", CatalogReadContext.Authenticated);

        Assert.Equal(CatalogReadStatus.Missing, map.Status);
    }

    [Fact]
    public async Task GetMapPackage_SavedMap_BindsSummary()
    {
        var handler = new RecordingHandler(_ => Envelope(Item("map-1", "storm", "saved-map", "public", actions: ["view", "edit"])));
        var catalog = CreateCatalog(handler);

        var map = await catalog.GetMapPackageAsync("map-1", CatalogReadContext.Authenticated);

        Assert.Equal(CatalogReadStatus.Allowed, map.Status);
        Assert.Equal("map", map.MapPackage!.Summary.Type);
    }

    [Fact]
    public async Task GetDraftMap_AnonymousContext_RequiresSignIn()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("must not call server"));
        var catalog = CreateCatalog(handler);

        var draft = await catalog.GetDraftMapAsync("svc-1", CatalogReadContext.AnonymousPublicLink(token: null));

        Assert.Equal(CatalogReadStatus.Unavailable, draft.Status);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetDraftMap_FromServiceSource_HydratesDraft()
    {
        var handler = new RecordingHandler(_ => Envelope(Item("svc-1", "hydrants", "service", "organization", actions: ["view", "edit"])));
        var catalog = CreateCatalog(handler);

        var draft = await catalog.GetDraftMapAsync("svc-1", CatalogReadContext.Authenticated);

        Assert.Equal(CatalogReadStatus.Allowed, draft.Status);
        Assert.Equal("draft-svc-1", draft.MapPackage!.Summary.Id);
        Assert.Equal("map", draft.MapPackage.Summary.Type);
        Assert.False(draft.MapPackage.Summary.Access.Embeddable);
    }

    [Fact]
    public async Task GetDraftMap_FromSavedMapSource_IsUnsupportedServiceMetadata()
    {
        var handler = new RecordingHandler(_ => Envelope(Item("map-1", "storm", "saved-map", "public", actions: ["view"])));
        var catalog = CreateCatalog(handler);

        var draft = await catalog.GetDraftMapAsync("map-1", CatalogReadContext.Authenticated);

        Assert.Equal(CatalogReadStatus.UnsupportedServiceMetadata, draft.Status);
    }

    [Fact]
    public async Task AuthorizeEmbed_QueryStringToken_IsRejected()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("must not call server"));
        var catalog = CreateCatalog(handler);

        var embed = await catalog.AuthorizeEmbedAsync(
            "map-1",
            EmbedRouteOptions.FromUri("https://console.honua.test/embed/maps/map-1?embedToken=abc"));

        Assert.Equal(CatalogReadStatus.Unavailable, embed.Status);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AuthorizeEmbed_NonEmbeddableMap_IsUnavailable()
    {
        var handler = new RecordingHandler(_ => Envelope(Item("map-1", "storm", "saved-map", "public", actions: ["view"])));
        var catalog = CreateCatalog(handler);

        var embed = await catalog.AuthorizeEmbedAsync(
            "map-1",
            EmbedRouteOptions.FromUri("https://console.honua.test/embed/maps/map-1#embedToken=abc"));

        Assert.Equal(CatalogReadStatus.Unavailable, embed.Status);
    }

    [Fact]
    public async Task AuthorizeEmbed_NonPublicEmbeddableMap_SurfacesShareFacetGap()
    {
        var handler = new RecordingHandler(_ => Envelope(Item("map-1", "storm", "saved-map", "organization", actions: ["view", "embed"])));
        var catalog = CreateCatalog(handler);

        var embed = await catalog.AuthorizeEmbedAsync(
            "map-1",
            EmbedRouteOptions.FromUri("https://console.honua.test/embed/maps/map-1#embedToken=abc"));

        Assert.Equal(CatalogReadStatus.UnsupportedPackageBinding, embed.Status);
        Assert.Contains("Share facet", embed.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AuthorizeEmbed_PublicEmbeddableMap_IsAllowedAnonymous()
    {
        var handler = new RecordingHandler(_ => Envelope(Item("map-1", "storm", "saved-map", "public", actions: ["view", "embed"])));
        var catalog = CreateCatalog(handler);

        var embed = await catalog.AuthorizeEmbedAsync(
            "map-1",
            EmbedRouteOptions.FromUri("https://console.honua.test/embed/maps/map-1#embedToken=abc"));

        Assert.Equal(CatalogReadStatus.Allowed, embed.Status);
        Assert.True(embed.AnonymousRead);
    }

    [Fact]
    public async Task GetOpenDataItem_NonPublicItem_IsMissing()
    {
        var handler = new RecordingHandler(_ => Envelope(Item("svc-1", "internal", "service", "organization", actions: ["view"])));
        var catalog = CreateCatalog(handler);

        var openData = await catalog.GetOpenDataItemAsync("svc-1");

        Assert.Equal(CatalogReadStatus.Missing, openData.Status);
    }

    [Fact]
    public async Task GetOpenDataItem_PublicService_IsAllowedAnonymous()
    {
        var handler = new RecordingHandler(_ => Envelope(Item("svc-1", "coastal", "service", "public", actions: ["view"])));
        var catalog = CreateCatalog(handler);

        var openData = await catalog.GetOpenDataItemAsync("svc-1");

        Assert.Equal(CatalogReadStatus.Allowed, openData.Status);
        Assert.True(openData.AnonymousRead);
        Assert.True(openData.Item!.Summary.Access.OpenData);
    }

    private static HonuaServerConsoleCatalogClient CreateCatalog(RecordingHandler handler, string? apiKey = null)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = BaseUri };
        var client = new HonuaConsoleContentHttpClient(httpClient, new HonuaConsoleContentClientOptions(BaseUri, apiKey));
        return new HonuaServerConsoleCatalogClient(client);
    }

    private static HonuaConsoleContentItem Item(
        string id,
        string name,
        string itemType,
        string visibility,
        string[]? actions = null,
        string? lifecycle = null,
        HonuaConsoleProvenanceRef[]? provenance = null)
    {
        return new HonuaConsoleContentItem
        {
            Id = id,
            Name = name,
            Title = $"{name} title",
            Description = $"{name} description",
            ItemType = itemType,
            Visibility = visibility,
            OwnerId = "owner-1",
            Lifecycle = lifecycle,
            Actions = actions ?? [],
            Provenance = provenance ?? [],
            UpdatedAt = new DateTimeOffset(2026, 5, 24, 8, 0, 0, TimeSpan.Zero),
            CreatedAt = new DateTimeOffset(2026, 5, 20, 8, 0, 0, TimeSpan.Zero),
            Generation = 3
        };
    }

    private static HttpResponseMessage Envelope<T>(T data)
    {
        var json = JsonSerializer.Serialize(new EnvelopeDto<T>(true, data, null));
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed record EnvelopeDto<T>(bool success, T data, string? message);

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(responder(request));
        }
    }
}
