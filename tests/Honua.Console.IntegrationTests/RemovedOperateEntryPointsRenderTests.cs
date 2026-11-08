using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class RemovedOperateEntryPointsRenderTests
{
    private static readonly OperateServiceLayerProjection Parcels =
        new(1, "Parcels", "Polygon", "conn-1-layer-1", "parcels");

    private static readonly OperateServiceDetail Cadastre =
        new("cadastre", "Cadastre", "FeatureServer", "Running", "honua-server", [Parcels],
            [new("maxFeatures", "1000", "Runtime", RequiresRestart: false)],
            [new("FeatureServer", "https://server.example.test/rest/services/a-long-service-identifier-that-must-fit-on-a-phone/FeatureServer", "Enabled")]);

    [Fact]
    public void LayerPage_LinksOnlyToSurfacesTheServerSupports()
    {
        using var ctx = CreateContext();
        ctx.Services.AddSingleton<IConsoleLayerFieldsOperation>(new UnsupportedConsoleLayerFieldsOperation());

        var page = ctx.Render<OperateLayerDetailPage>(p => p.Add(c => c.ResourceId, Parcels.CanonicalResourceId));

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-layer-style-link]")));
        var hrefs = page.FindAll(".console-detail-actions a").Select(link => link.GetAttribute("href")).ToArray();
        Assert.DoesNotContain(hrefs, href => href!.EndsWith("/extrusion") || href.EndsWith("/subtypes") || href.EndsWith("/discovery"));
    }

    [Fact]
    public void ServicePage_OffersNoOverridesDiscoveryOrSettingsCaps()
    {
        using var ctx = CreateContext();
        ctx.Services.AddSingleton<IServiceConfigurationOperation>(new UnsupportedServiceConfigurationOperation());

        var page = ctx.Render<OperateServiceDetailPage>(p => p.Add(c => c.ServiceName, Cadastre.Name));

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-service-detail]")));
        Assert.Empty(page.FindAll("[data-publication-overrides-link], [data-service-discovery-link], [data-save-settings-caps]"));
        Assert.Contains(Cadastre.PublicationSlots[0].Url!, page.Markup, StringComparison.Ordinal);
        CatalogAdminPreview.Capture("service", page.Markup);
    }

    private static BunitContext CreateContext()
    {
        var ctx = new BunitContext();
        ctx.AddConsoleNotifications();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new FixedServicesDataSource(Cadastre));
        return ctx;
    }
}
