using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
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
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        RegisterCatalog(ctx, handler, anonymous: false);

        var page = ctx.Render<CatalogPage>();

        page.WaitForAssertion(
            () => Assert.Contains("Coastal Flood Service", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Storm Response Map", page.Markup, StringComparison.Ordinal);
        // saved-map item type projects onto the Console "Maps" type label.
        Assert.Contains("Maps", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_RendersDesignStructure_HeadActionsTypeStripAndStatusBadges()
    {
        var handler = new StubHandler(_ => Envelope(new HonuaConsoleContentListResponse
        {
            Total = 2,
            Items =
            [
                ServerItem("svc-1", "Coastal Flood Service", "service", "public", ["view"]),
                ServerItem("map-1", "Storm Response Map", "saved-map", "private", ["view", "edit"])
            ]
        }));
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        RegisterCatalog(ctx, handler, anonymous: false);

        var page = ctx.Render<CatalogPage>();

        page.WaitForAssertion(
            () => Assert.Contains("Coastal Flood Service", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // Page head exposes the mockup's primary actions.
        Assert.NotEmpty(page.FindAll("a.console-button-link[href='/operate/publishing']"));
        Assert.Contains("+ New from Studio", page.Markup, StringComparison.Ordinal);

        // Type strip carries the per-type glyphs from the design.
        Assert.NotEmpty(page.FindAll("nav.console-type-strip .console-type-glyph"));

        // Result-count toolbar is present.
        Assert.NotEmpty(page.FindAll(".console-result-count"));

        // Status column projects a published badge for a shared item and draft for a private one.
        var statusBadges = page.FindAll(".console-content-table tbody .console-status.console-state-success");
        Assert.Contains(statusBadges, badge => badge.TextContent.Contains("published", StringComparison.Ordinal));
        var draftBadges = page.FindAll(".console-content-table tbody .console-status.console-state-neutral");
        Assert.Contains(draftBadges, badge => badge.TextContent.Contains("draft", StringComparison.Ordinal));

        // Status column never fabricates a "validation" mockup-only signal.
        Assert.DoesNotContain("validation", page.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CatalogDetail_OverviewTab_RendersTwoColumnDesign()
    {
        var item = new ConsoleContentDetail
        {
            Summary = new ConsoleContentSummary
            {
                Id = "map-1",
                Slug = "storm-response-map",
                Type = "map",
                Title = "Storm Response Map",
                Summary = "Coastal storm response operations map.",
                Owner = "owner-1",
                Tags = ["storm", "coastal"],
                Access = new ConsoleShareAccess { Sharing = CatalogSharingTiers.Public, Embeddable = true },
                ResolvedRole = "editor"
            },
            Description = "Coastal storm response operations map.",
            Capabilities = ["metadata-v2"],
            Versions = [new ConsoleContentVersion("gen-2", "Current", new DateTimeOffset(2026, 5, 24, 8, 0, 0, TimeSpan.Zero), "owner-1")],
            Lineage = [new ConsoleContentLineageLink("upstream", "ds-1", "parcels_2024", "depends-on")],
            Usage = [new ConsoleContentUsage("dash-1", "Q3 land use review", "dashboard", "low")]
        };

        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        ctx.Services.AddSingleton<IConsoleCatalogClient>(new StubCatalogClient(item));
        ctx.Services.AddSingleton<IConsoleCatalogReadContextResolver>(new StubReadContextResolver(anonymous: false));

        var page = ctx.Render<CatalogDetailPage>(parameters =>
            parameters.Add(p => p.IdOrSlug, "storm-response-map"));

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll(".console-detail-overview")),
            TimeSpan.FromSeconds(5));

        // Two-column overview: a main column with cards plus a side rail.
        Assert.NotEmpty(page.FindAll(".console-detail-overview-main .console-overview-card"));
        Assert.NotEmpty(page.FindAll(".console-detail-overview-side .console-overview-card"));

        // Header carries the type glyph and a status badge.
        Assert.NotEmpty(page.FindAll(".console-detail-heading .console-type-glyph"));
        Assert.Contains("published", page.Markup, StringComparison.Ordinal);

        // Side rail surfaces Quick facts + Publication cards and the usage callout.
        Assert.Contains("Quick facts", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Publication", page.Markup, StringComparison.Ordinal);
        Assert.NotEmpty(page.FindAll(".console-usage-callout"));

        // Tab counts reflect the live detail collections.
        Assert.NotEmpty(page.FindAll(".console-tab-row .console-tab-count"));
    }

    [Fact]
    public void Catalog_WhenServerUnavailable_RendersErrorRetryState_NotEmptyCatalog()
    {
        // Regression for issue #272: a live-server failure must render a distinct error/retry state, NOT the
        // genuine empty-catalog "publish the first item" surface (a silent failure shown as success).
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        RegisterCatalog(ctx, handler, anonymous: false);

        var page = ctx.Render<CatalogPage>();

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("section.console-state-error")),
            TimeSpan.FromSeconds(5));
        // The failed read renders the error/retry terminal state with a retry action.
        Assert.Contains("Catalog could not load", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Retry", page.Markup, StringComparison.Ordinal);
        // It must NOT masquerade as a successful empty catalog.
        Assert.DoesNotContain("No content matched", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("publish the first item", page.Markup, StringComparison.OrdinalIgnoreCase);
        // The misleading "0 items shown" / type-strip counters are suppressed on a failed read.
        Assert.Empty(page.FindAll(".console-result-count"));
        // The merged runtime never falls back to seeded demo content on a server failure.
        Assert.DoesNotContain("Coastal Flood Service", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_WhenServerReturnsEmptyList_RendersEmptyCatalogState_NotError()
    {
        // The companion to the failure case: a SUCCESSFUL read that returns zero items is a genuine empty
        // catalog and must still render the "publish the first item" empty surface, not the error state.
        var handler = new StubHandler(_ => Envelope(new HonuaConsoleContentListResponse
        {
            Total = 0,
            Items = []
        }));
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        RegisterCatalog(ctx, handler, anonymous: false);

        var page = ctx.Render<CatalogPage>();

        page.WaitForAssertion(
            () => Assert.Contains("No content matched", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.NotEmpty(page.FindAll("section.console-state-empty"));
        // A genuine empty result is not an error.
        Assert.Empty(page.FindAll("section.console-state-error"));
        Assert.DoesNotContain("Catalog could not load", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_EmptyCatalog_WithBoundServerResources_BridgesToLiveResourceCount()
    {
        // UCD review 2026-07-12 (honua-console#312): an empty workspace catalog bound to a data-rich server
        // must bridge the mental model instead of reading as a flat "0 items" contradiction against
        // Operate → Data & Layers. The count comes from the existing resources projection.
        var handler = new StubHandler(_ => Envelope(new HonuaConsoleContentListResponse { Total = 0, Items = [] }));
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        RegisterCatalog(ctx, handler, anonymous: false, operateData: new StubOperateData(resourceCount: 14));

        var page = ctx.Render<CatalogPage>();

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("[data-catalog-server-bridge]")),
            TimeSpan.FromSeconds(5));

        // The bridge sentence names the live resource count and both exits (Operate + Publishing).
        Assert.Contains("the connected server has", page.Markup, StringComparison.Ordinal);
        Assert.Contains("14", page.Markup, StringComparison.Ordinal);
        Assert.NotEmpty(page.FindAll("[data-catalog-server-bridge] a[href='/operate/data']"));
        Assert.NotEmpty(page.FindAll("[data-catalog-server-bridge] a[href='/operate/publishing']"));
        // The bound-with-resources bridge replaces the plain "publish the first item" empty copy.
        Assert.DoesNotContain("Try a broader search", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_EmptyCatalog_Unbound_RendersPlainEmptyState_NoBridge()
    {
        // No server binding (empty resources projection): the existing empty state is unchanged and no
        // "N live resources" bridge is fabricated (honua-console#312 AC: unbound state remains).
        var handler = new StubHandler(_ => Envelope(new HonuaConsoleContentListResponse { Total = 0, Items = [] }));
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        RegisterCatalog(ctx, handler, anonymous: false, operateData: new UnsupportedOperateTransitionDataSource());

        var page = ctx.Render<CatalogPage>();

        page.WaitForAssertion(
            () => Assert.Contains("No content matched", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Empty(page.FindAll("[data-catalog-server-bridge]"));
        Assert.Contains("Try a broader search or publish the first item", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("the connected server has", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_EmptyCatalog_WhenResourceReadFails_OmitsBridge()
    {
        // The bridge is a best-effort mental-model aid: a resources read failure omits the line and falls
        // back to the plain empty state rather than surfacing an error (honua-console#312 AC: degrade gracefully).
        var handler = new StubHandler(_ => Envelope(new HonuaConsoleContentListResponse { Total = 0, Items = [] }));
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        RegisterCatalog(ctx, handler, anonymous: false, operateData: new StubOperateData(resourceCount: 9, throwOnRead: true));

        var page = ctx.Render<CatalogPage>();

        page.WaitForAssertion(
            () => Assert.Contains("No content matched", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Empty(page.FindAll("[data-catalog-server-bridge]"));
        Assert.DoesNotContain("the connected server has", page.Markup, StringComparison.Ordinal);
    }

    private static void RegisterCatalog(
        Bunit.BunitContext ctx,
        StubHandler handler,
        bool anonymous,
        IOperateTransitionDataSource? operateData = null)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = BaseUri };
        var client = new HonuaConsoleContentHttpClient(httpClient, new HonuaConsoleContentClientOptions(BaseUri, "admin-key"));
        ctx.Services.AddSingleton<IConsoleCatalogClient>(new HonuaServerConsoleCatalogClient(client));
        ctx.Services.AddSingleton<IConsoleCatalogReadContextResolver>(new StubReadContextResolver(anonymous));
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(operateData ?? new UnsupportedOperateTransitionDataSource());
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

    // Minimal resources-projection stub for the catalog server-binding bridge. Only the resource COUNT is
    // load-bearing; the default IOperateTransitionDataSource.GetResourcesViewAsync derives from the workspace.
    private sealed class StubOperateData(int resourceCount, bool throwOnRead = false) : IOperateTransitionDataSource
    {
        public Task<OperateTransitionWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default)
        {
            if (throwOnRead)
            {
                throw new InvalidOperationException("Simulated resources read failure.");
            }

            var edits = Enumerable.Range(0, resourceCount)
                .Select(index => new OperateResourceEditPreview(
                    $"res-{index}",
                    $"Resource {index}",
                    "source",
                    "draft",
                    "Published",
                    new OperateBlastRadius([], [], [], [], [], []),
                    [],
                    []))
                .ToArray();
            return Task.FromResult(new OperateTransitionWorkspace([], edits, [], [], []));
        }

        public Task<OperateConnectionSummary?> FindConnectionAsync(string connectionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateConnectionSummary?>(null);

        public Task<OperateResourceEditPreview?> FindResourceEditAsync(string resourceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateResourceEditPreview?>(null);

        public Task<OperateServiceDetail?> FindServiceAsync(string serviceName, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateServiceDetail?>(null);
    }

    private sealed class StubCatalogClient(ConsoleContentDetail detail) : IConsoleCatalogClient
    {
        public Task<CatalogSearchResult> SearchAsync(CatalogListRequest request, CatalogReadContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CatalogSearchResult([detail.Summary], new Dictionary<string, int>(StringComparer.Ordinal), request));

        public Task<CatalogItemReadResult> GetCatalogItemAsync(string idOrSlug, CatalogReadContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(CatalogItemReadResult.Allowed(detail));

        public Task<CatalogItemReadResult> GetOpenDataItemAsync(string idOrSlug, CancellationToken cancellationToken = default) =>
            Task.FromResult(CatalogItemReadResult.Allowed(detail, anonymousRead: true));

        public Task<MapPackageReadResult> GetMapPackageAsync(string mapId, CatalogReadContext context, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MapPackageReadResult> GetDraftMapAsync(string sourceItemId, CatalogReadContext context, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MapPackageReadResult> AuthorizeEmbedAsync(string mapId, EmbedRouteOptions options, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
