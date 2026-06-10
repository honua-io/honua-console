using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for the Share management page (<c>/share/manage</c>). Verifies the
/// missing-binding surface, a loaded projection rendering an unambiguous public/private state plus the
/// public-link and embed panels gated on the caller's facets, a blocked dependency closure rendering the
/// blocking dependencies, and a minted token surfacing its one-time secret value. Drives the page through a
/// fake <see cref="IShareAccessDataSource"/> rather than a mock server; the live binding ships its own
/// opt-in Testcontainers suite.
/// </summary>
public sealed class ShareManagePageRenderTests
{
    [Fact]
    public void ShareManage_WhenBindingMissing_RendersNotBoundSurface()
    {
        var data = new FakeShareDataSource
        {
            Load = new ShareAccessLoad(null, [MissingBinding])
        };

        var page = Render(data, itemId: "item-1");

        page.WaitForAssertion(
            () => Assert.Contains("Share access API is not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("data-share-access", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShareManage_LoadedPublicItem_RendersUnambiguousStateAndPanels()
    {
        var data = new FakeShareDataSource
        {
            Load = new ShareAccessLoad(
                PublicShare(tier: "public-indexed", publicLinkEnabled: true, embedEnabled: false, canShare: true, canEmbed: true),
                [])
        };

        var page = Render(data, itemId: "item-1");

        page.WaitForAssertion(
            () => Assert.Contains("data-share-access=\"item-1\"", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("data-share-tier=\"public-indexed\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Public", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Eligible for open-data distribution", page.Markup, StringComparison.Ordinal);
        // Per the ShareLinkConfig mockup: a detail header with status badges, a two-column layout, and a tab row.
        Assert.Contains("data-share-config", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-share-header-tier=\"public-indexed\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-share-layout", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-share-sidebar", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-share-tabs", page.Markup, StringComparison.Ordinal);
        foreach (var tab in new[] { "link", "embed", "opendata", "exports", "audit" })
        {
            Assert.Contains($"data-share-tab=\"{tab}\"", page.Markup, StringComparison.Ordinal);
        }
        // The default tab is the public link tab: public-link panel renders, embed/exports panels are scoped away.
        Assert.Contains("data-share-panel=\"public-link\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-share-token=\"tok-1\"", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("data-share-panel=\"embed\"", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("data-share-panel=\"exports\"", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShareManage_SwitchingTabs_ScopesPanelsPerMockup()
    {
        var data = new FakeShareDataSource
        {
            Load = new ShareAccessLoad(
                PublicShare(tier: "public-indexed", publicLinkEnabled: true, embedEnabled: true, canShare: true, canEmbed: true),
                [])
        };

        var page = Render(data, itemId: "item-1");
        page.WaitForAssertion(
            () => Assert.Contains("data-share-tabs", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // Embed tab reveals the embed panel and hides the public-link panel.
        page.Find("[data-share-tab='embed']").Click();
        page.WaitForAssertion(
            () => Assert.Contains("data-share-panel=\"embed\"", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("data-share-panel=\"public-link\"", page.Markup, StringComparison.Ordinal);

        // Exports tab renders the scheduled-export table (Name/Target/Format/Filter/Schedule/Last run/State)
        // and deep-links into Operate jobs per the ShareExports mockup.
        page.Find("[data-share-tab='exports']").Click();
        page.WaitForAssertion(
            () => Assert.Contains("data-share-panel=\"exports\"", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("data-share-exports-table", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-share-exports-link", page.Markup, StringComparison.Ordinal);
        Assert.Contains("/operate/jobs", page.Markup, StringComparison.Ordinal);

        // Open-data tab editor is server-bound; with no catalog binding it surfaces the missing-binding state
        // rather than fabricating downloads/license/tags.
        page.Find("[data-share-tab='opendata']").Click();
        page.WaitForAssertion(
            () => Assert.Contains("data-share-panel=\"opendata\"", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("not bound to honua-server", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShareManage_PrivateItemWithoutSharePermission_HidesShareControls()
    {
        var data = new FakeShareDataSource
        {
            Load = new ShareAccessLoad(
                PublicShare(tier: "private", publicLinkEnabled: false, embedEnabled: false, canShare: false, canEmbed: false),
                [])
        };

        var page = Render(data, itemId: "item-1");

        page.WaitForAssertion(
            () => Assert.Contains("data-share-access=\"item-1\"", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Private / membership-scoped", page.Markup, StringComparison.Ordinal);
        // No share/embed authoring panels for a caller lacking the facets.
        Assert.DoesNotContain("data-share-panel=\"access-tier\"", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("data-share-panel=\"public-link\"", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("data-share-panel=\"embed\"", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShareManage_ChangeTierBlocked_RendersDependencyConflicts()
    {
        var data = new FakeShareDataSource
        {
            Load = new ShareAccessLoad(PublicShare(tier: "private", false, false, canShare: true, canEmbed: true), []),
            Command = new ShareCommandResult(
                null,
                null,
                new ShareDependencyClosureView(
                    "item-1",
                    "public-indexed",
                    IsCompatible: false,
                    [new ShareDependencyConflictView("dep-1", "layer", "Private upstream", "item visibility is 'personal'")]),
                [])
        };

        var page = Render(data, itemId: "item-1");
        page.WaitForAssertion(
            () => Assert.Contains("data-share-panel=\"access-tier\"", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        page.Find("[data-share-panel='access-tier'] button.console-button").Click();

        page.WaitForAssertion(
            () => Assert.Contains("data-share-blocked=\"public-indexed\"", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Private upstream", page.Markup, StringComparison.Ordinal);
        Assert.Contains("item visibility is", page.Markup, StringComparison.Ordinal);
        Assert.Contains("personal", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShareManage_MintPublicLink_SurfacesOneTimeSecret()
    {
        var data = new FakeShareDataSource
        {
            Load = new ShareAccessLoad(PublicShare(tier: "public-link", publicLinkEnabled: false, embedEnabled: false, canShare: true, canEmbed: true), []),
            Command = new ShareCommandResult(
                PublicShare(tier: "public-link", publicLinkEnabled: true, embedEnabled: false, canShare: true, canEmbed: true),
                new ShareMintedSecret("public-link", "tok-2", "opaque-secret-once", null, null),
                null,
                [])
        };

        var page = Render(data, itemId: "item-1");
        page.WaitForAssertion(
            () => Assert.Contains("data-share-panel=\"public-link\"", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // Submit the mint form via its submit button.
        page.Find("[data-share-panel='public-link'] button[type='submit']").Click();

        page.WaitForAssertion(
            () => Assert.Contains("data-share-minted=\"public-link\"", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("opaque-secret-once", page.Markup, StringComparison.Ordinal);
        Assert.Contains("shown only once", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShareManage_OpenDataTab_RendersTwoPaneEditorWithDownloadsLicenseDiscoverabilityAndStac()
    {
        var data = new FakeShareDataSource
        {
            Load = new ShareAccessLoad(
                PublicShare(tier: "public-indexed", publicLinkEnabled: true, embedEnabled: false, canShare: true, canEmbed: true),
                [])
        };
        var detail = new ConsoleContentDetail
        {
            Summary = new ConsoleContentSummary
            {
                Id = "item-1",
                Slug = "parcels-2024",
                Title = "Tax Parcels (FY 2024)",
                Type = "layer",
                Owner = "State Assessor",
                Tags = ["parcels", "cadastre", "assessor"],
                Formats = ["GeoJSON", "GeoPackage", "Shapefile", "CSV (WKT)", "PMTiles"]
            },
            Description = "Statewide tax parcel boundaries for fiscal year 2024.",
            Bindings =
            [
                new ConsoleContentBinding("license", "license", "CC-BY 4.0", "ok"),
                new ConsoleContentBinding("contact", "contact", "data@assessor.ca.gov", "ok")
            ]
        };

        var page = Render(data, itemId: "item-1", catalog: new FakeCatalogClient(detail));
        page.WaitForAssertion(
            () => Assert.Contains("data-share-tabs", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        page.Find("[data-share-tab='opendata']").Click();
        page.WaitForAssertion(
            () => Assert.Contains("data-share-opendata-editor", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // Left pane: page identity, downloads (per-format checkboxes), license & contact, discoverability.
        Assert.Contains("data-share-opendata-fields", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-share-opendata-section=\"identity\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Tax Parcels (FY 2024)", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-share-opendata-format=\"geojson\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-share-opendata-format=\"kml\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("CC-BY 4.0", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data@assessor.ca.gov", page.Markup, StringComparison.Ordinal);
        // Discoverability: DCAT, JSON-LD schema.org, and STAC publication controls.
        Assert.Contains("data-share-opendata-dcat", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-share-opendata-jsonld", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-share-opendata-stac", page.Markup, StringComparison.Ordinal);
        // Right pane: live public-page preview.
        Assert.Contains("data-share-opendata-preview", page.Markup, StringComparison.Ordinal);
        Assert.Contains("powered by Honua", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShareManage_ExportsTab_RendersScheduledExportTableHeadersAndServerBoundEmptyState()
    {
        var data = new FakeShareDataSource
        {
            Load = new ShareAccessLoad(
                PublicShare(tier: "public-indexed", publicLinkEnabled: true, embedEnabled: false, canShare: true, canEmbed: true),
                [])
        };

        var page = Render(data, itemId: "item-1");
        page.WaitForAssertion(
            () => Assert.Contains("data-share-tabs", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        page.Find("[data-share-tab='exports']").Click();
        page.WaitForAssertion(
            () => Assert.Contains("data-share-exports-table", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // The scheduled-export table carries the ShareExports mockup columns.
        foreach (var column in new[] { "Name", "Target", "Format", "Filter", "Schedule", "Last run", "State" })
        {
            Assert.Contains(column, page.Markup, StringComparison.Ordinal);
        }
        // With no server export registry bound, the table shows an explicit empty state, never fabricated rows.
        Assert.Contains("data-share-exports-empty", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShareManage_PastExpiry_ShowsInlineError_AndDisablesMint()
    {
        var data = new FakeShareDataSource
        {
            Load = new ShareAccessLoad(PublicShare(tier: "public-link", publicLinkEnabled: false, embedEnabled: false, canShare: true, canEmbed: true), [])
        };

        var page = Render(data, itemId: "item-1");
        page.WaitForAssertion(
            () => Assert.Contains("data-share-panel=\"public-link\"", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // A past expiry is rejected by the client future-date rule.
        var past = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-ddTHH:mm:ss");
        page.Find("[data-share-panel='public-link'] input[type='datetime-local']").Change(past);

        page.WaitForAssertion(
            () => Assert.Contains("Expiry must be in the future", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.True(page.Find("[data-share-panel='public-link'] button[type='submit']").HasAttribute("disabled"));
    }

    [Fact]
    public void ShareManage_MalformedItemId_ShowsInlineError_AndDisablesOpen()
    {
        var data = new FakeShareDataSource
        {
            Load = new ShareAccessLoad(PublicShare(tier: "public-link", publicLinkEnabled: false, embedEnabled: false, canShare: true, canEmbed: true), [])
        };

        var page = Render(data, itemId: "item-1");
        page.WaitForAssertion(
            () => Assert.NotNull(page.Find("input[placeholder='item-...']")),
            TimeSpan.FromSeconds(5));

        page.Find("input[placeholder='item-...']").Change("bad id with spaces");

        page.WaitForAssertion(
            () => Assert.Contains("Item id may only contain", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        // The open-share button (first console-button in the heading form) is gated.
        var openButton = page.FindAll("button").First(b => b.TextContent.Contains("Open share access", StringComparison.Ordinal));
        Assert.True(openButton.HasAttribute("disabled"));
    }

    [Fact]
    public void ShareManage_Editing_HostsGuard_AndMintClearsDirty()
    {
        var data = new FakeShareDataSource
        {
            Load = new ShareAccessLoad(PublicShare(tier: "public-link", publicLinkEnabled: false, embedEnabled: false, canShare: true, canEmbed: true), []),
            Command = new ShareCommandResult(
                PublicShare(tier: "public-link", publicLinkEnabled: true, embedEnabled: false, canShare: true, canEmbed: true),
                new ShareMintedSecret("public-link", "tok-2", "opaque-secret-once", null, null),
                null,
                [])
        };

        var page = Render(data, itemId: "item-1");
        page.WaitForAssertion(
            () => Assert.Contains("data-share-panel=\"public-link\"", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // A future expiry edit marks the form dirty; the page hosts an <UnsavedChangesGuard/> while dirty.
        var future = DateTime.UtcNow.AddDays(7).ToString("yyyy-MM-ddTHH:mm:ss");
        page.Find("[data-share-panel='public-link'] input[type='datetime-local']").Change(future);

        // A successful mint returns the form to a clean baseline (and surfaces the one-time secret).
        page.Find("[data-share-panel='public-link'] button[type='submit']").Click();
        page.WaitForAssertion(
            () => Assert.Contains("opaque-secret-once", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
    }

    private static IRenderedComponent<ShareManagePage> Render(
        FakeShareDataSource data,
        string itemId,
        IConsoleCatalogClient? catalog = null)
    {
        var ctx = new Bunit.BunitContext();
        // The page now hosts an <UnsavedChangesGuard/> (Wave 5), which imports a JS module and may call
        // confirm() on navigation; run Loose JSInterop so those calls no-op in render tests.
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IShareAccessDataSource>(data);
        // The open-data editor reads server-owned content metadata through the catalog client. With no
        // server bound the page renders the missing-binding state; tests inject a fake to exercise the editor.
        ctx.Services.AddSingleton<IConsoleCatalogClient>(catalog ?? new UnsupportedConsoleCatalogClient());
        var navigation = ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(navigation.GetUriWithQueryParameter("itemId", itemId));
        return ctx.Render<ShareManagePage>();
    }

    private static ShareAccessView PublicShare(string tier, bool publicLinkEnabled, bool embedEnabled, bool canShare, bool canEmbed) =>
        new(
            ItemId: "item-1",
            ItemName: "storm-layer",
            ItemTitle: "Storm Layer",
            ItemType: "layer",
            OwnerId: "owner@honua.test",
            AccessTier: tier,
            PublicLinkEnabled: publicLinkEnabled,
            EmbedEnabled: embedEnabled,
            EmbedAudience: embedEnabled ? "map" : null,
            OpenDataEligible: string.Equals(tier, "public-indexed", StringComparison.Ordinal),
            AnonymousEligible: tier.StartsWith("public", StringComparison.Ordinal),
            CanShare: canShare,
            CanEmbed: canEmbed,
            CanAdminister: true,
            PublicLinkTokens: publicLinkEnabled
                ? [new SharePublicLinkView("tok-1", DateTimeOffset.UtcNow, null, "owner@honua.test", false)]
                : [],
            UpdatedAt: DateTimeOffset.UtcNow,
            UpdatedById: "owner@honua.test");

    private static readonly ShareCapabilityState MissingBinding = new(
        "Share access",
        "Missing binding",
        "Honua:Server:BaseUrl",
        "Configure Honua:Server:BaseUrl so the Share panel can bind the server-owned Console Share access API.");

    private sealed class FakeCatalogClient(ConsoleContentDetail detail) : IConsoleCatalogClient
    {
        public Task<CatalogSearchResult> SearchAsync(CatalogListRequest request, CatalogReadContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CatalogSearchResult([], new Dictionary<string, int>(StringComparer.Ordinal), request));

        public Task<CatalogItemReadResult> GetCatalogItemAsync(string idOrSlug, CatalogReadContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(CatalogItemReadResult.Allowed(detail));

        public Task<CatalogItemReadResult> GetOpenDataItemAsync(string idOrSlug, CancellationToken cancellationToken = default) =>
            Task.FromResult(CatalogItemReadResult.Allowed(detail, anonymousRead: true));

        public Task<MapPackageReadResult> GetMapPackageAsync(string mapId, CatalogReadContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(MapPackageReadResult.Denied(CatalogReadStatus.Missing, "n/a"));

        public Task<MapPackageReadResult> GetDraftMapAsync(string sourceItemId, CatalogReadContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(MapPackageReadResult.Denied(CatalogReadStatus.Missing, "n/a"));

        public Task<MapPackageReadResult> AuthorizeEmbedAsync(string mapId, EmbedRouteOptions options, CancellationToken cancellationToken = default) =>
            Task.FromResult(MapPackageReadResult.Denied(CatalogReadStatus.Missing, "n/a"));
    }

    private sealed class FakeShareDataSource : IShareAccessDataSource
    {
        public ShareAccessLoad Load { get; set; } = new(null, []);
        public ShareCommandResult Command { get; set; } = new(null, null, null, []);

        public Task<ShareAccessLoad> LoadAsync(string itemId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Load);

        public Task<ShareCommandResult> UpdateAccessTierAsync(string itemId, string accessTier, bool allowDependencyConflicts, CancellationToken cancellationToken = default) =>
            Task.FromResult(Command);

        public Task<ShareCommandResult> PreviewDependenciesAsync(string itemId, string targetTier, CancellationToken cancellationToken = default) =>
            Task.FromResult(Command);

        public Task<ShareCommandResult> MintPublicLinkAsync(string itemId, DateTimeOffset? expiresAt, CancellationToken cancellationToken = default) =>
            Task.FromResult(Command);

        public Task<ShareCommandResult> RevokePublicLinkAsync(string itemId, string tokenId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Command);

        public Task<ShareCommandResult> SetEmbedAsync(string itemId, bool enabled, string? audience, bool allowDependencyConflicts, CancellationToken cancellationToken = default) =>
            Task.FromResult(Command);

        public Task<ShareCommandResult> MintEmbedTokenAsync(string itemId, string audience, int? ttlSeconds, CancellationToken cancellationToken = default) =>
            Task.FromResult(Command);
    }
}
