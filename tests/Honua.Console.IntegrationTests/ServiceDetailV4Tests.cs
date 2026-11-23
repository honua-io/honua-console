using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class ServiceDetailV4Tests
{
    private static readonly OperateServiceDetail Emergency =
        new("emergency", "Emergency management", "FeatureServer, MapServer", "Configured", "honua-server",
            [
                new(1, "shelters", "Point", "conn-1-layer-1", "Shelters") { Srid = 4326, ConnectionId = "conn-1", ConnectionName = "county-postgis" },
                new(2, "flood-zones", "MultiPolygon", "conn-1-layer-2", "Flood zones", Enabled: false) { ConnectionId = "conn-1", ConnectionName = "county-postgis" }
            ],
            [],
            [
                new("FeatureServer", "https://server.example.test/rest/services/public-safety/emergency/FeatureServer", "Exposed"),
                new("OGC API Features", null, "Collections only")
            ])
        {
            FolderId = "folder-1",
            FolderName = "public-safety",
            Description = "Shelters and flood zones for the Emergency Operations Center."
        };

    [Fact]
    public void Header_SummarisesPathCountsAccessAndConnection()
    {
        using var ctx = CreateContext();
        var page = Render(ctx);

        var summary = page.Find("[data-service-summary]").TextContent;
        Assert.Contains("Service public-safety/emergency · 2 layers · 3 protocols · anonymous read", summary, StringComparison.Ordinal);
        Assert.Equal("/console/operate/connections/conn-1", page.Find("[data-service-connection] a").GetAttribute("href"));
        Assert.Single(page.FindAll("[data-service-connection]"));
        Assert.Contains("Emergency Operations Center", page.Find(".console-service-description").TextContent, StringComparison.Ordinal);
        Assert.Equal(new[] { "2", "3" }, page.FindAll("[data-tab-count]").Select(count => count.TextContent).ToArray());
        Assert.All(page.FindAll("[data-tab-count]"), count => Assert.Equal("true", count.GetAttribute("aria-hidden")));
    }

    [Fact]
    public void LayersTab_ShowsTypePathSrsEnabledAndPreview()
    {
        using var ctx = CreateContext();
        var page = Render(ctx);

        var rows = page.FindAll(".console-service-layer-table tbody tr");
        Assert.Equal(2, rows.Count);
        Assert.Equal("point", rows[0].QuerySelector("[data-icon]")!.GetAttribute("data-icon"));
        Assert.Equal("polygon", rows[1].QuerySelector("[data-icon]")!.GetAttribute("data-icon"));
        Assert.Equal("public-safety/emergency/shelters", rows[0].QuerySelector(".console-service-layer-path")!.TextContent);
        Assert.Equal("/console/operate/layers/conn-1-layer-1", rows[0].QuerySelector("th a")!.GetAttribute("href"));
        Assert.Contains("EPSG:4326", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("—", rows[1].TextContent, StringComparison.Ordinal);
        Assert.Contains("Disabled", rows[1].TextContent, StringComparison.Ordinal);
        Assert.NotNull(rows[0].QuerySelector("a[href='/console/operate/resources/conn-1-layer-1']"));
        Assert.Equal("Preview shelters", rows[0].QuerySelector("a[aria-label^='Preview']")!.GetAttribute("aria-label"));
        Assert.Equal("/console/operate/publishing/quick", page.Find("[data-service-add-layer]").GetAttribute("href"));
    }

    [Fact]
    public void WebServices_ListEndpointsAndChangeProtocolsOpensTheTab()
    {
        using var ctx = CreateContext();
        var page = Render(ctx);

        var cards = page.FindAll("[data-service-web-services] li");
        Assert.Equal(2, cards.Count);
        Assert.Equal(Emergency.Endpoints[0].Url, cards[0].QuerySelector("a")!.GetAttribute("href"));
        Assert.Null(cards[1].QuerySelector("a"));
        Assert.Contains("Collections only", cards[1].TextContent, StringComparison.Ordinal);

        page.Find("[data-service-web-services] .console-link-button").Click();
        Assert.Equal("true", page.Find("#service-protocols-tab").GetAttribute("aria-selected"));
        ctx.JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public void UnreadSettingsAreNotPresentedAsValues()
    {
        using var ctx = CreateContext(new UnsupportedServiceConfigurationOperation());
        var page = ctx.Render<OperateServiceDetailPage>(p => p.Add(c => c.ServiceName, Emergency.Name));
        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-service-summary]")));

        Assert.Contains("2 protocols", page.Find("[data-service-summary]").TextContent, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("[data-service-access-summary]"));
        var values = page.FindAll(".console-service-kv dd").Select(value => value.TextContent.Trim()).ToArray();
        Assert.Equal("Not available: service settings were not read", values[1]);
        Assert.Equal("Not available: service settings were not read", values[2]);
    }

    [Fact]
    public void ServiceWithoutLayersOrProtocolsSaysSoAndUsesItsPublicPath()
    {
        var empty = new OperateServiceDetail("roads", "Roads", "Geo service", "No layers", "honua-server", [], [], [])
        { FolderId = "folder-1", FolderName = "transport", PublicName = "roads-public" };
        using var ctx = CreateContext(new UnsupportedServiceConfigurationOperation(), empty);
        var page = ctx.Render<OperateServiceDetailPage>(p => p.Add(c => c.ServiceName, empty.Name));
        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-service-summary]")));

        Assert.Contains("Service transport/roads-public · 0 layers · 0 protocols", page.Find("[data-service-summary]").TextContent, StringComparison.Ordinal);
        Assert.Equal("This service has no layers yet.", page.Find("[data-service-no-layers]").TextContent);
        Assert.Empty(page.FindAll(".console-service-layer-table"));
        Assert.Contains("No protocol endpoints were reported", page.Find("[data-service-web-services]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Administration_ShowsFolderAccessLimitsAndConnection()
    {
        using var ctx = CreateContext();
        var page = Render(ctx);

        var values = page.FindAll(".console-service-kv dd").Select(value => value.TextContent.Trim()).ToArray();
        Assert.StartsWith("public-safety · Move…", values[0], StringComparison.Ordinal);
        Assert.Equal("Anonymous read · read: viewer · write: emd-editors", values[1]);
        Assert.Equal("4096 × 4096 px · 10,000 features per layer", values[2]);
        Assert.Equal("county-postgis", values[3]);
        Assert.EndsWith("#service-folder-editor", page.Find(".console-service-kv a").GetAttribute("href"), StringComparison.Ordinal);
        Assert.NotNull(page.Find("#service-folder-editor"));
        Assert.NotNull(page.Find("#service-metadata-editor"));

        page.Find("[data-service-admin-access]").Click();
        Assert.Equal("true", page.Find("#service-access-tab").GetAttribute("aria-selected"));
    }

    private static IRenderedComponent<OperateServiceDetailPage> Render(BunitContext ctx)
    {
        var page = ctx.Render<OperateServiceDetailPage>(p => p.Add(c => c.ServiceName, Emergency.Name));
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-service-access-summary]")));
        return page;
    }

    [Fact]
    public void UnreturnedLayersAndUnresolvedFolderAreNotShownAsEmptyOrRoot()
    {
        var unread = new OperateServiceDetail("bridges", "Bridges", "FeatureServer", "Configured", "honua-server", [], [], [])
        { FolderId = "folder-9", ReportedLayerCount = 3 };
        using var ctx = CreateContext(new UnsupportedServiceConfigurationOperation(), unread);
        var page = ctx.Render<OperateServiceDetailPage>(p => p.Add(c => c.ServiceName, unread.Name));
        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-service-summary]")));

        Assert.Contains("Service …/bridges · 3 layers", page.Find("[data-service-summary]").TextContent, StringComparison.Ordinal);
        Assert.Equal("This service reports 3 layers, but their details were not returned.", page.Find("[data-service-no-layers]").TextContent);
        Assert.StartsWith("Folder name not reported", page.Find(".console-service-kv dd").TextContent.Trim(), StringComparison.Ordinal);
    }

    [Fact]
    public void AccessWordingFollowsRoleLimitedReadAndAnonymousWrite()
    {
        using var ctx = CreateContext(new BoundSettings(view => view with { AllowAnonymous = false, AllowAnonymousWrite = true }));
        var page = ctx.Render<OperateServiceDetailPage>(p => p.Add(c => c.ServiceName, Emergency.Name));
        page.WaitForAssertion(() => Assert.NotNull(page.FindAll("[data-service-access-summary]").SingleOrDefault()));

        Assert.Equal(" · read limited to roles · anonymous write", page.Find("[data-service-access-summary]").TextContent);
        Assert.Equal("Read limited to roles · read: viewer · anonymous write · write: emd-editors",
            page.FindAll(".console-service-kv dd")[1].TextContent.Trim());
    }

    private static BunitContext CreateContext(IServiceConfigurationOperation? settings = null, OperateServiceDetail? service = null)
    {
        var ctx = new BunitContext();
        ctx.Services.AddSingleton<Microsoft.AspNetCore.Components.NavigationManager>(new MountedNavigationManager("/console/"));
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        ctx.Services.AddSingleton<ConsoleAgentContextRegistry>();
        ctx.AddConsoleNotifications();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new FixedServicesDataSource(service ?? Emergency));
        ctx.Services.AddSingleton(settings ?? new BoundSettings());
        return ctx;
    }

    private sealed class BoundSettings(Func<ServiceSettingsView, ServiceSettingsView>? adjust = null) : IServiceConfigurationOperation
    {
        private readonly UnsupportedServiceConfigurationOperation _unsupported = new();

        public Task<ServiceSettingsView> GetSettingsAsync(string serviceName, CancellationToken cancellationToken = default) =>
            Task.FromResult((adjust ?? (view => view))(new ServiceSettingsView
            {
                Bound = true,
                ServiceName = serviceName,
                AvailableProtocols = ["FeatureServer", "MapServer", "OgcFeatures"],
                EnabledProtocols = ["FeatureServer", "MapServer", "OgcFeatures"],
                AllowAnonymous = true,
                AllowedRoles = ["viewer"],
                AllowedWriteRoles = ["emd-editors"],
                MapServer = new() { MaxImageWidth = 4096, MaxImageHeight = 4096, MaxFeaturesPerLayer = 10_000 }
            }));

        public Task<ServiceConfigurationResult> UpdateProtocolsAsync(ServiceProtocolsCommand command, CancellationToken cancellationToken = default) =>
            _unsupported.UpdateProtocolsAsync(command, cancellationToken);

        public Task<ServiceConfigurationResult> SetLayerEnabledAsync(ServiceLayerEnableCommand command, CancellationToken cancellationToken = default) =>
            _unsupported.SetLayerEnabledAsync(command, cancellationToken);

        public Task<ServiceConfigurationResult> UpdateAccessPolicyAsync(ServiceAccessPolicyCommand command, CancellationToken cancellationToken = default) =>
            _unsupported.UpdateAccessPolicyAsync(command, cancellationToken);

        public Task<ServiceConfigurationResult> UpdateMapServerSettingsAsync(ServiceMapServerSettingsCommand command, CancellationToken cancellationToken = default) =>
            _unsupported.UpdateMapServerSettingsAsync(command, cancellationToken);
    }
}
