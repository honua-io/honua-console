using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class OperateLayerStylePageRenderTests
{
    [Fact]
    public void StyleEditor_WhenOverrideBindingMissing_RendersNotBoundButShowsRealStyleList()
    {
        var page = Render(
            new FakeOverrideDataSource
            {
                View = new OperateLayerStyleOverrideView("res-1", [], OverrideMissing)
            },
            new FakeStyleCatalog
            {
                Catalog = new StudioMapStyleCatalog([new StudioMapStyleOption("streets", "Streets")], "streets", null)
            });

        page.WaitForAssertion(
            () => Assert.Contains("Layer overrides are not available", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        // The real /ogc/styles list still renders even though the override portion is unbound.
        Assert.Contains("Streets", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-style-catalog-list", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void StyleEditor_MergedBuildPage_RendersOverrideMissingBindingThroughRealDi()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.AddConsoleNotifications();
        ctx.Services.AddSingleton<IOperateLayerStyleOverrideDataSource, UnsupportedOperateLayerStyleOverrideDataSource>();
        ctx.Services.AddSingleton<IStudioMapStyleCatalogDataSource, UnsupportedStudioMapStyleCatalogDataSource>();

        var page = ctx.Render<OperateLayerStylePage>(parameters =>
            parameters.Add(p => p.ResourceId, "res-1"));

        page.WaitForAssertion(
            () => Assert.Contains("Layer overrides are not available", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void StyleEditor_WhenLayersBound_RendersPerLayerEditor()
    {
        var page = Render(
            new FakeOverrideDataSource
            {
                View = new OperateLayerStyleOverrideView("res-1",
                [
                    new OperateLayerStyleOverride("layer-1", "parcels-fs", "Parcels FeatureServer", "streets", null)
                ])
            },
            new FakeStyleCatalog
            {
                Catalog = new StudioMapStyleCatalog([new StudioMapStyleOption("streets", "Streets")], "streets", null)
            });

        page.WaitForAssertion(
            () => Assert.Contains("data-layer-style=\"layer-1\"", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Parcels FeatureServer", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Layer overrides are not available", page.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, "/")]
    [InlineData(true, "/")]
    [InlineData(false, "/console/")]
    [InlineData(true, "/console/")]
    public async Task StyleEditor_PreservesSavePayloadAndReportsCountsWithoutJson(bool enabled, string mount)
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<Microsoft.AspNetCore.Components.NavigationManager>(new MountedNavigationManager(mount));
        context.AddConsoleNotifications();
        var registry = new ConsoleAgentContextRegistry();
        context.Services.AddSingleton(registry);
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        var source = new FakeOverrideDataSource
        {
            View = new("res-1", [new("layer-1", "parcels-fs", "Parcels FeatureServer")]) { LayerId = 7, CanonicalResourceId = "res-1" },
            SaveResult = new(true)
        };
        context.Services.AddSingleton<IOperateLayerStyleOverrideDataSource>(source);
        context.Services.AddSingleton<IStudioMapStyleCatalogDataSource>(new FakeStyleCatalog
        {
            Catalog = new([new("streets", "Streets")], "streets", null)
        });
        var page = context.Render<OperateLayerStylePage>(parameters => parameters.Add(p => p.ResourceId, "res-1"));
        Assert.Equal(enabled ? 2 : 0, page.FindAll("section.console-panel[aria-labelledby]").Count);
        Assert.Equal(mount + "operate/layers/res-1", page.Find("a").GetAttribute("href"));
        Assert.Equal(enabled ? "/operate/layers/res-1/style" : "/", registry.Current.Route);
        if (enabled)
        {
            var entity = Assert.Single(registry.Current.Entities);
            Assert.Equal(new ConsoleAgentEntity { Kind = ConsoleAgentEntityKind.Layer, Id = "7", ResourceId = "res-1" }, entity);
            Assert.Equal("/operate/layers/res-1", ConsoleAgentRoutes.ForEntity(entity));
            if (mount == "/console/") CatalogAdminPreview.Capture("data-layer-style", page.Markup);
        }
        page.Find("[data-layer-style-popup]").Change("{\"title\":\"Private field\"}");
        page.Find("[data-layer-style-drawing]").Change("{\"renderer\":{}}");
        Assert.Equal(enabled ? 2 : 0, registry.Current.Unsaved.ChangedFieldCount);
        Assert.DoesNotContain("Private field", System.Text.Json.JsonSerializer.Serialize(registry.Current));
        page.Find("[data-layer-style-save]").Click();
        Assert.Equal(new OperateLayerStyleOverrideEdit("res-1", "layer-1", PopupInfoJson: "{\"title\":\"Private field\"}", DrawingInfoJson: "{\"renderer\":{}}"), source.SavedEdit);
        Assert.Single(page.FindAll("[data-layer-style-saved]"));
        Assert.Equal(0, registry.Current.Unsaved.ChangedFieldCount);
        Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount);
        if (enabled && mount == "/console/") CatalogAdminPreview.Capture("data-layer-style-saved", page.Markup);
        page.Find("[data-layer-style-popup]").Change("Unsaved before disposal");
        Assert.Equal(enabled ? 1 : 0, registry.Current.Unsaved.ChangedFieldCount);
        await context.DisposeComponentsAsync();
        Assert.Empty(registry.Current.Entities);
        Assert.Equal(0, registry.Current.Unsaved.ChangedFieldCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StyleEditor_BlockedSaveKeepsEditsDirty(bool enabled)
    {
        using var context = new BunitContext();
        context.AddConsoleNotifications();
        var registry = new ConsoleAgentContextRegistry();
        context.Services.AddSingleton(registry);
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        context.Services.AddSingleton<IOperateLayerStyleOverrideDataSource>(new FakeOverrideDataSource
        {
            View = new("res-1", [new("layer-1", "parcels-fs", "Parcels FeatureServer")])
        });
        context.Services.AddSingleton<IStudioMapStyleCatalogDataSource>(new FakeStyleCatalog());
        var page = context.Render<OperateLayerStylePage>(parameters => parameters.Add(p => p.ResourceId, "res-1"));
        page.Find("[data-layer-style-popup]").Change("private draft");
        page.Find("[data-layer-style-save]").Click();
        Assert.Equal("private draft", page.Find("[data-layer-style-popup]").GetAttribute("value"));
        Assert.Contains("Override save is not bound", page.Markup, StringComparison.Ordinal);
        Assert.Equal(enabled ? 1 : 0, registry.Current.Unsaved.ChangedFieldCount);
        Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StyleEditor_UnboundShowsNoEditors(bool enabled)
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<Microsoft.AspNetCore.Components.NavigationManager>(new MountedNavigationManager("/console/"));
        context.AddConsoleNotifications();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        context.Services.AddSingleton<IOperateLayerStyleOverrideDataSource, UnsupportedOperateLayerStyleOverrideDataSource>();
        context.Services.AddSingleton<IStudioMapStyleCatalogDataSource, UnsupportedStudioMapStyleCatalogDataSource>();
        var page = context.Render<OperateLayerStylePage>(parameters => parameters.Add(p => p.ResourceId, "res-1"));
        Assert.Contains("Layer overrides are not available", page.Markup, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("[data-layer-style-save], [data-layer-style-popup]"));
        if (enabled)
        {
            Assert.Equal("Layer overrides are not available", page.Find("section[aria-labelledby=layer-overrides] h3").TextContent);
            CatalogAdminPreview.Capture("data-layer-style-unbound", page.Markup);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StyleEditor_PendingSaveCountsAndPreservesNewerEdits(bool enabled)
    {
        using var context = new BunitContext();
        context.AddConsoleNotifications();
        var registry = new ConsoleAgentContextRegistry();
        context.Services.AddSingleton(registry);
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        var completion = new TaskCompletionSource<OperateLayerStyleOverrideSaveResult>();
        var source = new FakeOverrideDataSource
        {
            View = new("res-1", [new("layer-1", "parcels-fs", "Parcels FeatureServer")]),
            PendingSave = completion.Task
        };
        context.Services.AddSingleton<IOperateLayerStyleOverrideDataSource>(source);
        context.Services.AddSingleton<IStudioMapStyleCatalogDataSource>(new FakeStyleCatalog());
        var page = context.Render<OperateLayerStylePage>(parameters => parameters.Add(p => p.ResourceId, "res-1"));
        page.Find("[data-layer-style-popup]").Change("submitted draft");
        var saving = page.Find("[data-layer-style-save]").ClickAsync(new());
        page.WaitForAssertion(() => Assert.True(page.Find("[data-layer-style-save]").HasAttribute("disabled")));
        Assert.Equal(enabled ? 1 : 0, registry.Current.Unsaved.PendingOperationCount);
        page.Find("[data-layer-style-popup]").Change("newer draft");
        completion.SetResult(new(true));
        await saving;
        Assert.Equal("submitted draft", source.SavedEdit?.PopupInfoJson);
        Assert.Equal("newer draft", page.Find("[data-layer-style-popup]").GetAttribute("value"));
        Assert.Equal(enabled ? 1 : 0, registry.Current.Unsaved.ChangedFieldCount);
        Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount);
    }

    private static IRenderedComponent<OperateLayerStylePage> Render(
        IOperateLayerStyleOverrideDataSource overrides,
        IStudioMapStyleCatalogDataSource catalog)
    {
        var ctx = new Bunit.BunitContext();
        ctx.AddConsoleNotifications();
        ctx.Services.AddSingleton(overrides);
        ctx.Services.AddSingleton(catalog);
        return ctx.Render<OperateLayerStylePage>(parameters => parameters.Add(p => p.ResourceId, "res-1"));
    }

    private static readonly OperateLayerStyleBindingState OverrideMissing = new(
        "Layer presentation overrides", OperateLayerStyleBindingState.MissingBinding,
        "honua-server layer style overrides (pending)",
        "Per-layer style/popup overrides bind to a server-owned contract that has not shipped.");

    private sealed class FakeOverrideDataSource : IOperateLayerStyleOverrideDataSource
    {
        public OperateLayerStyleOverrideView View { get; set; } = new("res-1", []);
        public OperateLayerStyleOverrideSaveResult? SaveResult { get; set; }
        public OperateLayerStyleOverrideEdit? SavedEdit { get; private set; }
        public Task<OperateLayerStyleOverrideSaveResult>? PendingSave { get; set; }

        public Task<OperateLayerStyleOverrideView> GetOverridesAsync(string resourceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(View);

        public Task<OperateLayerStyleOverrideSaveResult> SaveOverrideAsync(OperateLayerStyleOverrideEdit edit, CancellationToken cancellationToken = default)
        {
            SavedEdit = edit;
            return PendingSave ?? Task.FromResult(SaveResult ?? OperateLayerStyleOverrideSaveResult.Blocked(OverrideMissing));
        }
    }

    private sealed class FakeStyleCatalog : IStudioMapStyleCatalogDataSource
    {
        public StudioMapStyleCatalog Catalog { get; set; } = StudioMapStyleCatalog.Empty;

        public Task<StudioMapStyleCatalog> GetStyleCatalogAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Catalog);

        public Task<HonuaAdminEndpointResult<HonuaOgcStylesheet>> GetStylesheetAsync(
            string styleId, HonuaOgcStyleEncoding encoding, CancellationToken cancellationToken = default) =>
            Task.FromResult(HonuaAdminEndpointResult<HonuaOgcStylesheet>.FromIssue(
                new HonuaAdminEndpointIssue("Missing binding", "GET /ogc/styles/{styleId}", "test")));

        public Task<HonuaOgcStyleSaveResult> SaveStylesheetAsync(
            string styleId, HonuaOgcStyleEncoding encoding, string content, bool strict, CancellationToken cancellationToken = default) =>
            Task.FromResult(HonuaOgcStyleSaveResult.Fail("Missing binding", "test"));
    }
}
