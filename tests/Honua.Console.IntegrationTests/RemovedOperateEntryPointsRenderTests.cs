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
            [new("FeatureServer", "https://server.example.test/rest/services/a-long-service-identifier-that-must-fit-on-a-phone/FeatureServer", "Exposed by honua-server")]);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LayerPage_LinksOnlyToSurfacesTheServerSupports(bool enabled)
    {
        using var ctx = CreateContext(enabled);
        ctx.Services.AddSingleton<Microsoft.AspNetCore.Components.NavigationManager>(new MountedNavigationManager("/console/"));
        ctx.Services.AddSingleton<IConsoleLayerFieldsOperation>(new UnsupportedConsoleLayerFieldsOperation());

        var page = ctx.Render<OperateLayerDetailPage>(p => p.Add(c => c.ResourceId, Parcels.CanonicalResourceId));

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-layer-style-link]")));
        var hrefs = page.FindAll(".console-detail-actions a").Select(link => link.GetAttribute("href")).ToArray();
        Assert.DoesNotContain(hrefs, href => href!.EndsWith("/extrusion") || href.EndsWith("/subtypes") || href.EndsWith("/discovery"));
        if (enabled) CatalogAdminPreview.Capture("data-layer-detail-unbound", page.Markup);
    }

    [Theory]
    [InlineData(false, "/")]
    [InlineData(true, "/")]
    [InlineData(false, "/console/")]
    [InlineData(true, "/console/")]
    public void ServicePage_OffersNoOverridesDiscoveryOrSettingsCaps(bool enabled, string mount)
    {
        using var ctx = CreateContext(enabled, mount);
        ctx.Services.AddSingleton<IServiceConfigurationOperation>(new UnsupportedServiceConfigurationOperation());

        var page = ctx.Render<OperateServiceDetailPage>(p => p.Add(c => c.ServiceName, Cadastre.Name));

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-service-detail]")));
        Assert.NotNull(page.Find($"a[href='{mount}operate/resources/conn-1-layer-1']"));
        Assert.Empty(page.FindAll("[data-publication-overrides-link], [data-service-discovery-link], [data-save-settings-caps]"));
        Assert.Contains(Cadastre.PublicationSlots[0].Url!, page.Markup, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("fieldset[style], article[role='status']"));
        Assert.Equal(enabled ? 4 : 0, page.FindAll("[role='tab']").Count);
        if (mount == "/console/") CatalogAdminPreview.Capture(enabled ? "data-service-detail" : "service", page.Markup);
        if (enabled)
        {
            page.Find("#service-protocols-tab").Click();
            Assert.Contains("console-state-neutral", page.Find(enabled ? "#service-protocols-panel > [role='status']" : "[data-service-config] > [role='status']").ClassList);
            if (mount == "/console/") CatalogAdminPreview.Capture("data-service-detail-unbound", page.Markup);
        }
        else
        {
            Assert.Contains("console-state-neutral", page.Find(enabled ? "#service-protocols-panel > [role='status']" : "[data-service-config] > [role='status']").ClassList);
        }
    }

    [Theory]
    [InlineData("Updated", "success", false)]
    [InlineData("Rejected", "danger", false)]
    [InlineData("Updated", "success", true)]
    [InlineData("Rejected", "danger", true)]
    public void ServiceResult_PreservesItsOutcomeAndAnnouncement(string state, string appearance, bool enabled)
    {
        using var ctx = CreateContext(enabled);
        ctx.Services.AddSingleton<IServiceConfigurationOperation>(new ResultServiceConfiguration(new()
        {
            Succeeded = state == "Updated",
            State = state,
            Detail = "Protocol configuration result."
        }));
        var page = ctx.Render<OperateServiceDetailPage>(parameters => parameters.Add(component => component.ServiceName, Cadastre.Name));
        page.WaitForAssertion(() => Assert.False(page.Find("[data-save-protocols]").HasAttribute("disabled")));
        if (enabled) page.Find("#service-protocols-tab").Click();
        page.Find("[data-save-protocols]").Click();
        page.WaitForAssertion(() => Assert.Contains(state, page.Find("[data-config-result]").TextContent));
        Assert.Contains($"console-state-{appearance}", page.Find("[data-config-result]").ClassList);
        Assert.Equal("DIV", page.Find("[data-config-result]").TagName);
        CatalogAdminPreview.Capture(enabled ? $"data-service-detail-{appearance}" : $"service-{appearance}", page.Markup);
    }

    [Fact]
    public void ServiceTabs_KeepAccessEditsWhileSwitchingPanels()
    {
        using var ctx = CreateContext(true);
        ctx.Services.AddSingleton<IServiceConfigurationOperation>(new ResultServiceConfiguration(new() { State = "Updated", Succeeded = true }));
        var page = ctx.Render<OperateServiceDetailPage>(parameters => parameters.Add(component => component.ServiceName, Cadastre.Name));

        var registry = ctx.Services.GetRequiredService<ConsoleAgentContextRegistry>();
        var selected = Assert.Single(registry.Current.Selection);
        Assert.Equal(ConsoleAgentEntityKind.Service, selected.Kind);
        Assert.Equal(Cadastre.Name, selected.Id);
        Assert.Equal(Cadastre.Name, selected.ServiceName);
        Assert.Equal(ConsoleAgentRoutes.ForEntity(selected), registry.Current.Route);
        Assert.Equal(0, registry.Current.Unsaved.ChangedFieldCount);
        page.Find("#service-access-tab").Click();
        Assert.Equal("service-access-tab", registry.Current.Filters["tab"]);
        Assert.False(page.Find("#service-access-panel").HasAttribute("hidden"));
        page.Find("[data-allowed-roles]").Change("surveyors");
        Assert.Equal(1, registry.Current.Unsaved.ChangedFieldCount);
        Assert.DoesNotContain("surveyors", System.Text.Json.JsonSerializer.Serialize(registry.Current));
        page.Find("#service-layers-tab").Click();
        Assert.True(page.Find("#service-access-panel").HasAttribute("hidden"));
        page.Find("#service-access-tab").Click();
        Assert.Equal("surveyors", page.Find("[data-allowed-roles]").GetAttribute("value"));
        Assert.Single(page.FindAll("[role='tab'][aria-selected='true'][tabindex='0']"));
        CatalogAdminPreview.Capture("data-service-detail-access", page.Markup);
        page.Find("#service-protocols-tab").Click();
        CatalogAdminPreview.Capture("data-service-detail-protocols", page.Markup);
        page.Find("#service-limits-tab").Click();
        CatalogAdminPreview.Capture("data-service-detail-limits", page.Markup);
    }

    private sealed class ResultServiceConfiguration(ServiceConfigurationResult result) : IServiceConfigurationOperation
    {
        private readonly UnsupportedServiceConfigurationOperation _unsupported = new();
        public Task<ServiceSettingsView> GetSettingsAsync(string serviceName, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ServiceSettingsView
            {
                Bound = true,
                ServiceName = serviceName,
                AvailableProtocols = ["FeatureServer"],
                EnabledProtocols = ["FeatureServer"]
            });
        public Task<ServiceConfigurationResult> UpdateProtocolsAsync(ServiceProtocolsCommand command, CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
        public Task<ServiceConfigurationResult> SetLayerEnabledAsync(ServiceLayerEnableCommand command, CancellationToken cancellationToken = default) =>
            _unsupported.SetLayerEnabledAsync(command, cancellationToken);
        public Task<ServiceConfigurationResult> UpdateAccessPolicyAsync(ServiceAccessPolicyCommand command, CancellationToken cancellationToken = default) =>
            _unsupported.UpdateAccessPolicyAsync(command, cancellationToken);
        public Task<ServiceConfigurationResult> UpdateMapServerSettingsAsync(ServiceMapServerSettingsCommand command, CancellationToken cancellationToken = default) =>
            _unsupported.UpdateMapServerSettingsAsync(command, cancellationToken);
    }

    private static BunitContext CreateContext(bool enabled = false, string mount = "/console/")
    {
        var ctx = new BunitContext();
        ctx.Services.AddSingleton<Microsoft.AspNetCore.Components.NavigationManager>(new MountedNavigationManager(mount));
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        ctx.Services.AddSingleton<ConsoleAgentContextRegistry>();
        ctx.AddConsoleNotifications();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new FixedServicesDataSource(Cadastre));
        return ctx;
    }


}
