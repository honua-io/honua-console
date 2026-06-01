using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for the Operate &gt; Catalogs discovery-endpoints surfaces driven through the
/// real production data source — <see cref="HonuaServerCatalogDiscoveryDataSource"/> over a stub
/// <see cref="IHonuaCatalogDiscoveryClient"/> — rather than a fake at the data-source seam. This proves the
/// live binding maps the honua-server catalog discovery-endpoints registry projection (honua-server#1279)
/// into the rendered list/detail/item surfaces, and that the unsupported binding renders the explicit
/// missing-binding state when no server base URL is configured. Complements
/// <see cref="CatalogsDiscoveryPageRenderTests"/> (which exercises the page chrome through the interface)
/// and <see cref="CatalogsDiscoveryLiveServerTests"/> (which drives the live binding against a real server).
/// </summary>
public sealed class CatalogsDiscoveryLiveBindingRenderTests
{
    private const string Contract = "GET /api/v1/console/catalog-endpoints/{workspaceId}";

    [Fact]
    public void List_LiveBinding_RendersRegistryFromStubClient()
    {
        var data = new HonuaServerCatalogDiscoveryDataSource(new StubCatalogDiscoveryClient
        {
            Registry = HonuaAdminEndpointResult<HonuaCatalogDiscoveryRegistry>.FromData(SampleRegistry()),
        });

        var page = RenderList(data);

        page.WaitForAssertion(
            () => Assert.Contains("data-catalogs-grid", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // The live mapper projects the wire registry into all five dialect cards with their state/registration.
        Assert.Contains("data-catalog-endpoint=\"esri\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-catalog-endpoint=\"ogc\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-catalog-state=\"on\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-catalog-state=\"off\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-catalog-registration=\"auto-default\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-catalog-registration=\"opt-in\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("2 catalogs auto-default-on", page.Markup, StringComparison.Ordinal);
        Assert.Contains("3 opt-in", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("console-state-missing", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Detail_LiveBinding_RendersMirroredItemsFromStubClient()
    {
        var data = new HonuaServerCatalogDiscoveryDataSource(new StubCatalogDiscoveryClient
        {
            EndpointDetail = HonuaAdminEndpointResult<HonuaCatalogEndpointDetail>.FromData(SampleEndpointDetail()),
        });

        var page = RenderDetail(data, "esri");

        page.WaitForAssertion(
            () => Assert.Contains("data-catalog-items-table", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        Assert.Contains("data-catalog-item=\"a3bf-0214\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Parcels (FY 2024)", page.Markup, StringComparison.Ordinal);
        Assert.Contains("/operate/catalogs/esri/items/a3bf-0214", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-catalog-auto-mirror", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Item_LiveBinding_RendersFieldGroupsFromStubClient()
    {
        var data = new HonuaServerCatalogDiscoveryDataSource(new StubCatalogDiscoveryClient
        {
            Item = HonuaAdminEndpointResult<HonuaCatalogItem>.FromData(SampleItem()),
        });

        var page = RenderItem(data, "esri", "a3bf-0214");

        page.WaitForAssertion(
            () => Assert.Contains("data-catalog-field-legend", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        Assert.Contains("data-catalog-field-group=\"Identity\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-catalog-field-state=\"system\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-catalog-field-state=\"calculated\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("derived from Resource.Metadata.Title", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void List_LiveBinding_WhenServerReportsNotFound_RendersCapabilityStateNotGrid()
    {
        // An admin-authorized request to a workspace with no published registry returns the structured
        // NotFound envelope, which the live shim maps to the "Unsupported" capability state. The list renders
        // that state instead of the grid — and never fabricates endpoint cards.
        var data = new HonuaServerCatalogDiscoveryDataSource(new StubCatalogDiscoveryClient
        {
            Registry = HonuaAdminEndpointResult<HonuaCatalogDiscoveryRegistry>.FromIssue(
                new HonuaAdminEndpointIssue("Unsupported", Contract, "not published", 404)),
        });

        var page = RenderList(data);

        page.WaitForAssertion(
            () => Assert.DoesNotContain("data-catalogs-grid", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Catalog discovery endpoints are not bound", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void List_UnsupportedBinding_RendersMissingBindingState()
    {
        // The no-server-configured composition: the unsupported source surfaces the missing-binding state.
        var data = new UnsupportedCatalogDiscoveryDataSource();

        var page = RenderList(data);

        page.WaitForAssertion(
            () => Assert.Contains("Catalog discovery endpoints are not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("console-state-missing", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Honua:Server:BaseUrl", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("data-catalogs-grid", page.Markup, StringComparison.Ordinal);
    }

    private static IRenderedComponent<CatalogsListPage> RenderList(ICatalogDiscoveryDataSource data)
    {
        var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton(data);
        ctx.Services.GetRequiredService<Bunit.TestDoubles.FakeNavigationManager>().NavigateTo("operate/catalogs");
        return ctx.RenderComponent<CatalogsListPage>();
    }

    private static IRenderedComponent<CatalogsEndpointDetailPage> RenderDetail(ICatalogDiscoveryDataSource data, string key)
    {
        var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton(data);
        ctx.Services.GetRequiredService<Bunit.TestDoubles.FakeNavigationManager>().NavigateTo($"operate/catalogs/{key}");
        return ctx.RenderComponent<CatalogsEndpointDetailPage>(parameters => parameters.Add(p => p.Key, key));
    }

    private static IRenderedComponent<CatalogItemEditorPage> RenderItem(ICatalogDiscoveryDataSource data, string key, string itemId)
    {
        var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton(data);
        ctx.Services.GetRequiredService<Bunit.TestDoubles.FakeNavigationManager>().NavigateTo($"operate/catalogs/{key}/items/{itemId}");
        return ctx.RenderComponent<CatalogItemEditorPage>(parameters => parameters
            .Add(p => p.Key, key)
            .Add(p => p.ItemId, itemId));
    }

    private static HonuaCatalogDiscoveryRegistry SampleRegistry() => new()
    {
        WorkspaceId = "ws-public-works",
        WorkspaceName = "Public Works",
        PublicHost = "https://honua.example.gov",
        AutoDefaultCount = 2,
        OptInCount = 3,
        Endpoints =
        [
            new HonuaCatalogEndpoint
            {
                Key = HonuaCatalogDialects.Esri,
                Title = "Esri catalog",
                Dialect = HonuaCatalogDialects.Esri,
                Enabled = true,
                AutoDefault = true,
                Url = "/catalog",
                Entries = 38,
                IssueCount = 1,
                Feeders = [new HonuaCatalogFeeder { Kind = "FeatureServer", Label = "public-works-fs · 8" }],
            },
            new HonuaCatalogEndpoint
            {
                Key = HonuaCatalogDialects.Ogc,
                Title = "OGC API Records",
                Dialect = HonuaCatalogDialects.Ogc,
                Enabled = true,
                AutoDefault = true,
                Url = "/records",
                Entries = 38,
                Feeders = [new HonuaCatalogFeeder { Kind = "OGC API Features", Label = "features-public · 38" }],
            },
            new HonuaCatalogEndpoint
            {
                Key = HonuaCatalogDialects.ODataV4,
                Title = "OData catalog",
                Dialect = HonuaCatalogDialects.ODataV4,
                Enabled = true,
                AutoDefault = false,
                Url = "/odata",
                Entries = 3,
            },
            new HonuaCatalogEndpoint
            {
                Key = HonuaCatalogDialects.Stac,
                Title = "STAC",
                Dialect = HonuaCatalogDialects.Stac,
                Enabled = false,
                AutoDefault = false,
                Url = "/stac",
            },
            new HonuaCatalogEndpoint
            {
                Key = HonuaCatalogDialects.Dcat,
                Title = "DCAT",
                Dialect = HonuaCatalogDialects.Dcat,
                Enabled = false,
                AutoDefault = false,
                Url = "/dcat",
            },
        ],
    };

    private static HonuaCatalogEndpointDetail SampleEndpointDetail() => new()
    {
        Endpoint = new HonuaCatalogEndpoint
        {
            Key = HonuaCatalogDialects.Esri,
            Title = "Esri catalog",
            Dialect = HonuaCatalogDialects.Esri,
            Enabled = true,
            AutoDefault = true,
            Url = "/catalog",
            Entries = 38,
            IssueCount = 1,
        },
        LastRebuild = "14m ago",
        AutoMirror = true,
        Items =
        [
            new HonuaCatalogEndpointItem
            {
                Id = "a3bf-0214",
                Title = "Parcels (FY 2024)",
                FromService = "public-works-fs / 0",
                Resource = "parcels_2024",
                Tags = ["parcels", "cadastre"],
                HasThumbnail = true,
                License = "CC-BY 4.0",
                Updated = "2m",
            },
        ],
    };

    private static HonuaCatalogItem SampleItem() => new()
    {
        Id = "a3bf-2024-0214-cdef-7820",
        Title = "Parcels (FY 2024)",
        AutoMirror = true,
        Live = true,
        BackingServiceCount = 2,
        TagCount = 4,
        IssueCount = 1,
        Groups =
        [
            new HonuaCatalogItemFieldGroup
            {
                Title = "Identity",
                Scope = "resource",
                Fields =
                [
                    new HonuaCatalogItemField { Label = "Item ID", State = HonuaCatalogFieldStates.System, Value = "a3bf-2024-0214-cdef-7820" },
                    new HonuaCatalogItemField { Label = "Title", State = HonuaCatalogFieldStates.Calculated, Value = "Parcels (FY 2024)", DerivedFrom = "Resource.Metadata.Title" },
                ],
            },
        ],
    };

    private sealed class StubCatalogDiscoveryClient : IHonuaCatalogDiscoveryClient
    {
        public Uri BaseUri { get; } = new("https://server.test");

        public HonuaAdminEndpointResult<HonuaCatalogDiscoveryRegistry> Registry { get; set; } =
            HonuaAdminEndpointResult<HonuaCatalogDiscoveryRegistry>.FromIssue(new HonuaAdminEndpointIssue("Unavailable", Contract, "not set"));

        public HonuaAdminEndpointResult<HonuaCatalogEndpointDetail> EndpointDetail { get; set; } =
            HonuaAdminEndpointResult<HonuaCatalogEndpointDetail>.FromIssue(new HonuaAdminEndpointIssue("Unavailable", Contract, "not set"));

        public HonuaAdminEndpointResult<HonuaCatalogItem> Item { get; set; } =
            HonuaAdminEndpointResult<HonuaCatalogItem>.FromIssue(new HonuaAdminEndpointIssue("Unavailable", Contract, "not set"));

        public Task<HonuaAdminEndpointResult<HonuaCatalogDiscoveryRegistry>> GetRegistryAsync(string workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Registry);

        public Task<HonuaAdminEndpointResult<HonuaCatalogEndpointDetail>> GetEndpointAsync(string workspaceId, string endpointKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(EndpointDetail);

        public Task<HonuaAdminEndpointResult<HonuaCatalogItem>> GetItemAsync(string workspaceId, string endpointKey, string itemId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Item);
    }
}
