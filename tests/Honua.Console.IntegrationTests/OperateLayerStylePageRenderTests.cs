using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for the Operate resource-presentation style editor
/// (/operate/layers/{id}/style, UI-032). Asserts the merged-build missing-binding state for per-slot
/// overrides (no fabricated overrides) while the page still reuses the REAL /ogc/styles list, plus the
/// bound per-slot editor. Drives the page through fakes (never a mock server). The merged-build
/// UnsupportedOperateLayerStyleOverrideDataSource is exercised through real DI.
/// </summary>
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
            () => Assert.Contains("Per-slot overrides are not bound", page.Markup, StringComparison.Ordinal),
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
            () => Assert.Contains("Per-slot overrides are not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void StyleEditor_WhenSlotsBound_RendersPerSlotEditor()
    {
        var page = Render(
            new FakeOverrideDataSource
            {
                View = new OperateLayerStyleOverrideView("res-1",
                [
                    new OperateLayerSlotStyleOverride("slot-1", "parcels-fs", "Parcels FeatureServer", "streets", null)
                ])
            },
            new FakeStyleCatalog
            {
                Catalog = new StudioMapStyleCatalog([new StudioMapStyleOption("streets", "Streets")], "streets", null)
            });

        page.WaitForAssertion(
            () => Assert.Contains("data-style-slot=\"slot-1\"", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Parcels FeatureServer", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Per-slot overrides are not bound", page.Markup, StringComparison.Ordinal);
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
        "Resource presentation overrides", OperateLayerStyleBindingState.MissingBinding,
        "honua-server resource-presentation slot overrides (pending)",
        "Per-slot style/popup overrides bind to a server-owned contract that has not shipped.");

    private sealed class FakeOverrideDataSource : IOperateLayerStyleOverrideDataSource
    {
        public OperateLayerStyleOverrideView View { get; set; } = new("res-1", []);
        public OperateLayerStyleOverrideSaveResult? SaveResult { get; set; }

        public Task<OperateLayerStyleOverrideView> GetOverridesAsync(string resourceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(View);

        public Task<OperateLayerStyleOverrideSaveResult> SaveOverrideAsync(OperateLayerSlotStyleOverrideEdit edit, CancellationToken cancellationToken = default) =>
            Task.FromResult(SaveResult ?? OperateLayerStyleOverrideSaveResult.Blocked(OverrideMissing));
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
