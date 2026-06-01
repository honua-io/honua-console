using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for the Operate &gt; Catalogs discovery-endpoints surfaces —
/// <c>/operate/catalogs</c> (CatalogsList), <c>/operate/catalogs/{key}</c> (EsriCatalogDetail), and
/// <c>/operate/catalogs/{key}/items/{itemId}</c> (CatalogItemEditor). Asserts the design landmarks from the
/// screens-catalogs.jsx mockup — the endpoint list, the ON/OFF state badge, the auto-default-on vs opt-in
/// distinction, the feeder list, per-endpoint issues, the detail drill-down items table, the auto-mirror
/// item field groups with the field-state legend — plus the missing-binding surface. Drives the pages
/// through a fake <see cref="ICatalogDiscoveryDataSource"/> rather than a mock server. The live binding is
/// covered by <see cref="CatalogsDiscoveryLiveBindingRenderTests"/> (production data source over a stub
/// client) and <see cref="CatalogsDiscoveryLiveServerTests"/> (opt-in Testcontainers against honua-server
/// #1279, now shipped).
/// </summary>
public sealed class CatalogsDiscoveryPageRenderTests
{
    private static readonly CatalogDiscoveryCapabilityState MissingBinding = new(
        "Catalogs (discovery endpoints)",
        "Missing binding",
        "Honua:Server:BaseUrl",
        "Configure Honua:Server:BaseUrl so the Catalogs surface can bind the catalog discovery-endpoints registry (honua-server#1279).");

    [Fact]
    public void List_WhenBindingMissing_RendersMissingBindingSurface()
    {
        var data = new FakeCatalogDiscoveryDataSource
        {
            RegistryLoad = new CatalogDiscoveryRegistryLoad(null, [MissingBinding])
        };

        var page = RenderList(data);

        page.WaitForAssertion(
            () => Assert.Contains("Catalog discovery endpoints are not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("console-state-missing", page.Markup, StringComparison.Ordinal);
        Assert.Contains("honua-server#1279", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("data-catalogs-grid", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void List_Loaded_RendersEndpointCardsWithDefaultOnVsOptInDistinction()
    {
        var data = new FakeCatalogDiscoveryDataSource
        {
            RegistryLoad = new CatalogDiscoveryRegistryLoad(SampleRegistry(), [])
        };

        var page = RenderList(data);

        page.WaitForAssertion(
            () => Assert.Contains("data-catalogs-grid", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // The endpoint list renders all five dialects.
        Assert.Contains("data-catalog-endpoint=\"esri\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-catalog-endpoint=\"ogc\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-catalog-endpoint=\"odata\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-catalog-endpoint=\"stac\"", page.Markup, StringComparison.Ordinal);

        // ON / OFF server-wide state.
        Assert.Contains("data-catalog-state=\"on\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-catalog-state=\"off\"", page.Markup, StringComparison.Ordinal);

        // The auto-default-on vs opt-in distinction is explicit on each card.
        Assert.Contains("data-catalog-registration=\"auto-default\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-catalog-registration=\"opt-in\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-catalog-default=\"auto-default\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-catalog-default=\"opt-in\"", page.Markup, StringComparison.Ordinal);

        // Aggregate counts strip.
        Assert.Contains("2 catalogs auto-default-on", page.Markup, StringComparison.Ordinal);
        Assert.Contains("3 opt-in", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void List_Loaded_RendersFeederListAndPerEndpointIssues()
    {
        var data = new FakeCatalogDiscoveryDataSource
        {
            RegistryLoad = new CatalogDiscoveryRegistryLoad(SampleRegistry(), [])
        };

        var page = RenderList(data);

        page.WaitForAssertion(
            () => Assert.Contains("data-catalog-feeders", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // Feeder list with kind tags.
        Assert.Contains("data-catalog-feeder=\"FeatureServer\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-catalog-feeder=\"MapServer\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("public-works-fs · 8", page.Markup, StringComparison.Ordinal);

        // Per-endpoint issues badge on the Esri card.
        Assert.Contains("data-catalog-issues=\"1\"", page.Markup, StringComparison.Ordinal);

        // A disabled endpoint shows the disabled callout rather than facts.
        Assert.Contains("data-catalog-disabled", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Detail_WhenBindingMissing_RendersMissingBindingSurface()
    {
        var data = new FakeCatalogDiscoveryDataSource
        {
            EndpointLoad = new CatalogEndpointDetailLoad(null, [MissingBinding])
        };

        var page = RenderDetail(data, "esri");

        page.WaitForAssertion(
            () => Assert.Contains("Catalog discovery endpoints are not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("data-catalog-items-table", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Detail_Loaded_RendersTabsAndMirroredItemsTable()
    {
        var data = new FakeCatalogDiscoveryDataSource
        {
            EndpointLoad = new CatalogEndpointDetailLoad(SampleEndpointDetail(), [])
        };

        var page = RenderDetail(data, "esri");

        page.WaitForAssertion(
            () => Assert.Contains("data-catalog-items-table", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // Tab strip with the Items / Validation counts.
        Assert.Contains("data-catalog-tabs", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-catalog-tab=\"items\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-catalog-tab=\"validation\"", page.Markup, StringComparison.Ordinal);

        // Endpoint header URL bar.
        Assert.Contains("data-catalog-endpoint-url", page.Markup, StringComparison.Ordinal);
        Assert.Contains("/catalog", page.Markup, StringComparison.Ordinal);

        // Mirrored items with drill-down links to the item editor.
        Assert.Contains("data-catalog-item=\"a3bf-0214\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Parcels (FY 2024)", page.Markup, StringComparison.Ordinal);
        Assert.Contains("/operate/catalogs/esri/items/a3bf-0214", page.Markup, StringComparison.Ordinal);

        // Thumbnail-present and thumbnail-missing both render distinct badges.
        Assert.Contains("data-catalog-thumb=\"ok\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-catalog-thumb=\"missing\"", page.Markup, StringComparison.Ordinal);

        // Auto-mirror footnote.
        Assert.Contains("data-catalog-auto-mirror", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Item_WhenBindingMissing_RendersMissingBindingSurface()
    {
        var data = new FakeCatalogDiscoveryDataSource
        {
            ItemLoad = new CatalogItemLoad(null, [MissingBinding])
        };

        var page = RenderItem(data, "esri", "a3bf-0214");

        page.WaitForAssertion(
            () => Assert.Contains("Catalog discovery endpoints are not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("data-catalog-field-group", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Item_Loaded_RendersFieldGroupsWithStateLegendAndAutoMirror()
    {
        var data = new FakeCatalogDiscoveryDataSource
        {
            ItemLoad = new CatalogItemLoad(SampleItem(), [])
        };

        var page = RenderItem(data, "esri", "a3bf-0214");

        page.WaitForAssertion(
            () => Assert.Contains("data-catalog-field-legend", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // Auto-mirror + live badges.
        Assert.Contains("data-catalog-auto-mirror", page.Markup, StringComparison.Ordinal);

        // Field-state legend distinguishes the three field origins.
        Assert.Contains("data-catalog-field-state=\"system\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-catalog-field-state=\"calculated\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-catalog-field-state=\"input\"", page.Markup, StringComparison.Ordinal);

        // Field groups from the mockup.
        Assert.Contains("data-catalog-field-group=\"Identity\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-catalog-field-group=\"Catalog presentation\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-catalog-field-group=\"Standards mapping\"", page.Markup, StringComparison.Ordinal);

        // A calculated field shows its derived-from origin.
        Assert.Contains("derived from Resource.Metadata.Title", page.Markup, StringComparison.Ordinal);

        // Standards issue callout + quick facts panel.
        Assert.Contains("data-catalog-standards-issue", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-catalog-quick-facts", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void List_WithWorkspaceQuery_PreservesWorkspaceOnDrillDownLinks()
    {
        // Regression: a non-default workspace (e.g. /operate/catalogs?workspace=tenant-a) must carry the
        // ?workspace= query through the "Browse items" drill-down so the detail page does not default the
        // missing query back to the current workspace.
        var data = new FakeCatalogDiscoveryDataSource
        {
            RegistryLoad = new CatalogDiscoveryRegistryLoad(SampleRegistry(), [])
        };

        var page = RenderList(data, workspace: "tenant-a");

        page.WaitForAssertion(
            () => Assert.Contains("data-catalogs-grid", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        var browse = page.Find("[data-catalog-browse]");
        Assert.Equal("/operate/catalogs/esri?workspace=tenant-a", browse.GetAttribute("href"));
    }

    [Fact]
    public void List_WithoutWorkspaceQuery_OmitsWorkspaceFromDrillDownLinks()
    {
        // The default workspace must not gain a spurious ?workspace=current on the drill-down link.
        var data = new FakeCatalogDiscoveryDataSource
        {
            RegistryLoad = new CatalogDiscoveryRegistryLoad(SampleRegistry(), [])
        };

        var page = RenderList(data, workspace: null);

        page.WaitForAssertion(
            () => Assert.Contains("data-catalogs-grid", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        var browse = page.Find("[data-catalog-browse]");
        Assert.Equal("/operate/catalogs/esri", browse.GetAttribute("href"));
    }

    [Fact]
    public void Detail_WithWorkspaceQuery_PreservesWorkspaceOnItemAndBreadcrumbLinks()
    {
        var data = new FakeCatalogDiscoveryDataSource
        {
            EndpointLoad = new CatalogEndpointDetailLoad(SampleEndpointDetail(), [])
        };

        var page = RenderDetail(data, "esri", workspace: "tenant-a");

        page.WaitForAssertion(
            () => Assert.Contains("data-catalog-items-table", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // Item drill-down carries the workspace.
        var itemLink = page.Find("[data-catalog-item-link]");
        Assert.Equal("/operate/catalogs/esri/items/a3bf-0214?workspace=tenant-a", itemLink.GetAttribute("href"));

        // The back-to-list breadcrumb also carries it.
        Assert.Contains("href=\"/operate/catalogs?workspace=tenant-a\"", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Item_WithWorkspaceQuery_PreservesWorkspaceOnBreadcrumbLinks()
    {
        var data = new FakeCatalogDiscoveryDataSource
        {
            ItemLoad = new CatalogItemLoad(SampleItem(), [])
        };

        var page = RenderItem(data, "esri", "a3bf-0214", workspace: "tenant-a");

        page.WaitForAssertion(
            () => Assert.Contains("data-catalog-field-legend", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // Endpoint and list breadcrumbs both carry the workspace back up the hierarchy.
        Assert.Contains("href=\"/operate/catalogs/esri?workspace=tenant-a\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("href=\"/operate/catalogs?workspace=tenant-a\"", page.Markup, StringComparison.Ordinal);
    }

    private static IRenderedComponent<CatalogsListPage> RenderList(FakeCatalogDiscoveryDataSource data, string? workspace = null)
    {
        var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<ICatalogDiscoveryDataSource>(data);
        NavigateToWorkspace(ctx, "operate/catalogs", workspace);
        return ctx.RenderComponent<CatalogsListPage>();
    }

    private static IRenderedComponent<CatalogsEndpointDetailPage> RenderDetail(FakeCatalogDiscoveryDataSource data, string key, string? workspace = null)
    {
        var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<ICatalogDiscoveryDataSource>(data);
        NavigateToWorkspace(ctx, $"operate/catalogs/{key}", workspace);
        return ctx.RenderComponent<CatalogsEndpointDetailPage>(parameters => parameters
            .Add(p => p.Key, key));
    }

    private static IRenderedComponent<CatalogItemEditorPage> RenderItem(FakeCatalogDiscoveryDataSource data, string key, string itemId, string? workspace = null)
    {
        var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<ICatalogDiscoveryDataSource>(data);
        NavigateToWorkspace(ctx, $"operate/catalogs/{key}/items/{itemId}", workspace);
        return ctx.RenderComponent<CatalogItemEditorPage>(parameters => parameters
            .Add(p => p.Key, key)
            .Add(p => p.ItemId, itemId));
    }

    // Drives the page's [SupplyParameterFromQuery] workspace via the navigation URI, since the parameter is
    // private and cannot be set through the bUnit parameter builder.
    private static void NavigateToWorkspace(Bunit.TestContext ctx, string path, string? workspace)
    {
        var nav = ctx.Services.GetRequiredService<Bunit.TestDoubles.FakeNavigationManager>();
        var query = string.IsNullOrEmpty(workspace) ? string.Empty : $"?workspace={Uri.EscapeDataString(workspace)}";
        nav.NavigateTo(path + query);
    }

    private static CatalogDiscoveryRegistryView SampleRegistry() => CatalogDiscoveryMapper.ToView(new HonuaCatalogDiscoveryRegistry
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
                Description = "Esri-style catalog. Items are FeatureServer / MapServer / ImageServer publications.",
                Dialect = HonuaCatalogDialects.Esri,
                Enabled = true,
                AutoDefault = true,
                Url = "/catalog",
                Entries = 38,
                FedBy = "auto-mirror from Esri services",
                IssueCount = 1,
                Feeders =
                [
                    new HonuaCatalogFeeder { Kind = "FeatureServer", Label = "public-works-fs · 8" },
                    new HonuaCatalogFeeder { Kind = "MapServer", Label = "public-works-ms · 8" },
                ],
            },
            new HonuaCatalogEndpoint
            {
                Key = HonuaCatalogDialects.Ogc,
                Title = "OGC API Records",
                Description = "OGC Records. Records are OGC API Features collections.",
                Dialect = HonuaCatalogDialects.Ogc,
                Enabled = true,
                AutoDefault = true,
                Url = "/records",
                Entries = 38,
                FedBy = "auto-mirror from OGC API Features",
                IssueCount = 0,
                Feeders = [new HonuaCatalogFeeder { Kind = "OGC API Features", Label = "features-public · 38" }],
            },
            new HonuaCatalogEndpoint
            {
                Key = HonuaCatalogDialects.ODataV4,
                Title = "OData catalog",
                Description = "Service document + $metadata for BI discovery.",
                Dialect = HonuaCatalogDialects.ODataV4,
                Enabled = true,
                AutoDefault = false,
                Url = "/odata",
                Entries = 3,
                FedBy = "opt-in per entity set",
                IssueCount = 0,
                Feeders = [new HonuaCatalogFeeder { Kind = "OData", Label = "odata-bi · 3 of 8 entity sets opted in" }],
            },
            new HonuaCatalogEndpoint
            {
                Key = HonuaCatalogDialects.Stac,
                Title = "STAC",
                Description = "SpatioTemporal Asset Catalog. Opt-in per resource.",
                Dialect = HonuaCatalogDialects.Stac,
                Enabled = false,
                AutoDefault = false,
                Url = "/stac",
                Entries = 0,
                FedBy = "opt-in per resource",
                IssueCount = 0,
                Feeders = [],
            },
            new HonuaCatalogEndpoint
            {
                Key = HonuaCatalogDialects.Dcat,
                Title = "DCAT",
                Description = "Open-data catalog for national / EU portals. Opt-in per resource.",
                Dialect = HonuaCatalogDialects.Dcat,
                Enabled = false,
                AutoDefault = false,
                Url = "/dcat",
                Entries = 0,
                FedBy = "opt-in per resource",
                IssueCount = 0,
                Feeders = [],
            },
        ],
    });

    private static CatalogEndpointDetailView SampleEndpointDetail() => CatalogDiscoveryMapper.ToView(new HonuaCatalogEndpointDetail
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
            Feeders = [],
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
                IssueCount = 0,
                Updated = "2m",
            },
            new HonuaCatalogEndpointItem
            {
                Id = "e7fb-5566",
                Title = "Fire perimeters",
                FromService = "public-works-fs / 4",
                Resource = "fire_perimeters",
                Tags = ["fire", "hazards"],
                HasThumbnail = false,
                License = "public domain",
                IssueCount = 1,
                Updated = "28m",
            },
        ],
    });

    private static CatalogItemView SampleItem() => CatalogDiscoveryMapper.ToView(new HonuaCatalogItem
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
                Subtitle = "from the resource · read-only here",
                Scope = "resource",
                Fields =
                [
                    new HonuaCatalogItemField { Label = "Item ID", State = HonuaCatalogFieldStates.System, Value = "a3bf-2024-0214-cdef-7820" },
                    new HonuaCatalogItemField { Label = "Title", State = HonuaCatalogFieldStates.Calculated, Value = "Parcels (FY 2024)", DerivedFrom = "Resource.Metadata.Title" },
                ],
            },
            new HonuaCatalogItemFieldGroup
            {
                Title = "Catalog presentation",
                Subtitle = "lives only in the catalog",
                Scope = "publication",
                Fields =
                [
                    new HonuaCatalogItemField { Label = "Tags", State = HonuaCatalogFieldStates.Input, Value = "parcels, cadastre, assessor, FY2024" },
                    new HonuaCatalogItemField { Label = "Featured", State = HonuaCatalogFieldStates.Input, Value = "not featured" },
                ],
            },
            new HonuaCatalogItemFieldGroup
            {
                Title = "Service bindings",
                Subtitle = "which services feed this catalog entry",
                Scope = "service",
                Fields =
                [
                    new HonuaCatalogItemField { Label = "Primary service", State = HonuaCatalogFieldStates.System, Value = "public-works-fs / layer 0" },
                ],
            },
            new HonuaCatalogItemFieldGroup
            {
                Title = "Standards mapping",
                Subtitle = "how this item appears in different catalog dialects",
                Scope = "server",
                Fields =
                [
                    new HonuaCatalogItemField { Label = "DCAT dataset", State = HonuaCatalogFieldStates.Calculated, Value = "missing publisher identifier", DerivedFrom = "resource metadata" },
                ],
            },
        ],
    });

    private sealed class FakeCatalogDiscoveryDataSource : ICatalogDiscoveryDataSource
    {
        public CatalogDiscoveryRegistryLoad RegistryLoad { get; set; } = new(null, []);
        public CatalogEndpointDetailLoad EndpointLoad { get; set; } = new(null, []);
        public CatalogItemLoad ItemLoad { get; set; } = new(null, []);

        public Task<CatalogDiscoveryRegistryLoad> LoadRegistryAsync(string workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(RegistryLoad);

        public Task<CatalogEndpointDetailLoad> LoadEndpointAsync(string workspaceId, string endpointKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(EndpointLoad);

        public Task<CatalogItemLoad> LoadItemAsync(string workspaceId, string endpointKey, string itemId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ItemLoad);
    }
}
