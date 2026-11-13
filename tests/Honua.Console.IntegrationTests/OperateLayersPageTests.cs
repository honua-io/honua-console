using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class OperateLayersPageTests
{
    [Theory]
    [InlineData(false, "/")]
    [InlineData(true, "/")]
    [InlineData(false, "/console/")]
    [InlineData(true, "/console/")]
    public void LayersRetainExposureAndResolveCanonicalAgentLinks(bool enabled, string mount)
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<Microsoft.AspNetCore.Components.NavigationManager>(new MountedNavigationManager(mount));
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        context.Services.AddSingleton<ConsoleAgentContextRegistry>();
        var layer = new OperateServiceLayerProjection(42, "Parcels", "Polygon", "connection-1-layer-42", "Parcels dataset");
        var service = new OperateServiceDetail("cadastre", "Cadastre", "FeatureServer", "Ready", "honua-server", [layer], [], []);
        context.Services.AddSingleton<IOperateTransitionDataSource>(new FixedServicesDataSource(service));
        var page = context.Render<OperateLayersPage>();
        Assert.Equal("Layers", page.Find("h1").TextContent);
        Assert.Equal(enabled ? 1 : 0, page.FindAll(".console-page-heading-v4").Count);
        var row = page.Find("[data-layer-id='42'][data-service-id='cadastre']");
        Assert.Contains("Parcels", row.TextContent);
        Assert.Contains("Polygon", row.TextContent);
        Assert.NotNull(row.QuerySelector($"a[href='{mount}operate/services/cadastre/settings']"));
        Assert.NotNull(row.QuerySelector($"a[href='{mount}operate/resources/connection-1-layer-42']"));
        var registry = context.Services.GetRequiredService<ConsoleAgentContextRegistry>();
        Assert.Equal(enabled ? "/operate/layers" : "/", registry.Current.Route);
        if (enabled)
        {
            var entity = Assert.Single(registry.Current.Entities);
            Assert.Equal(ConsoleAgentEntityKind.Layer, entity.Kind);
            Assert.Equal("42", entity.Id);
            Assert.Equal("connection-1-layer-42", entity.ResourceId);
            Assert.Equal("/operate/layers/connection-1-layer-42", ConsoleAgentRoutes.ForEntity(entity));
            Assert.Equal(2, row.QuerySelectorAll($"a[href='{mount}operate/layers/connection-1-layer-42']").Length);
            if (mount == "/console/") CatalogAdminPreview.Capture("data-layers", page.Markup);
        }
        else Assert.Empty(registry.Current.Entities);
    }
}

