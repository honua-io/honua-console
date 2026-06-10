using Bunit;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Cross-cutting missing-binding completeness sweep (console-integration-test-plan.md §5.2, Wave 6).
///
/// With NO honua-server configured, a representative set of live surfaces — across Operate, Studio, and Share
/// — must render the EXPLICIT missing-binding state and NEVER fabricate data (Charter §11: missing-binding is
/// a first-class state). Each page is wired to its production <c>Unsupported*</c> data source (the binding
/// the DI container resolves when no server base URL is configured) and asserted to surface the missing-binding
/// copy while NOT rendering any data table / projection that would imply fabricated content. This is a focused
/// completeness check spanning families, complementing the per-surface render tests.
///
/// These are pure bUnit render facts: they need NO Docker and run in PR CI as well as the nightly lane (the
/// no-fabrication invariant must hold everywhere), so they are plain <see cref="FactAttribute"/>s.
/// </summary>
public sealed class MissingBindingCompletenessCrossCuttingTests
{
    [Fact]
    public void OperateTemporal_WithNoServer_RendersMissingBinding_AndNoFabricatedData()
    {
        using var ctx = new Bunit.TestContext();
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<ITemporalCapabilityClient, UnsupportedTemporalCapabilityClient>();

        var page = ctx.RenderComponent<OperateTemporalPage>();

        page.WaitForAssertion(
            () => Assert.Contains("Temporal capability is not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        // No data grid is fabricated when unbound.
        Assert.DoesNotContain("<table", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void OperatePublishing_WithNoServer_RendersMissingBinding_AndNoFabricatedMatrix()
    {
        using var ctx = new Bunit.TestContext();
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IPublishingWorkspaceDataSource>(new UnsupportedPublishingWorkspaceDataSource());
        ctx.Services.AddSingleton<IServiceLayerPublishOperation>(new UnsupportedServiceLayerPublishOperation());
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new UnsupportedOperateTransitionDataSource());

        var page = ctx.RenderComponent<OperatePublishingPage>();

        page.WaitForAssertion(
            () => Assert.Contains("Missing binding", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Honua:Server:BaseUrl", page.Markup, StringComparison.Ordinal);
        // No publication projection is fabricated when unbound.
        Assert.DoesNotContain("Publication Matrix", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Publication Review", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void StudioAnalysisBuilder_WithNoServer_RendersMissingBinding_AndNoFabricatedList()
    {
        using var ctx = new Bunit.TestContext();
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioAnalysisPackageDataSource>(new UnsupportedStudioAnalysisPackageDataSource());

        var page = ctx.RenderComponent<StudioAnalysisBuilderPage>();

        page.WaitForAssertion(
            () => Assert.Contains("Analysis content lifecycle is not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ShareManage_WithNoServer_RendersMissingBinding_AndNoFabricatedPanels()
    {
        using var ctx = new Bunit.TestContext();
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IShareAccessDataSource>(new UnsupportedShareAccessDataSource());
        ctx.Services.AddSingleton<IConsoleCatalogClient>(new UnsupportedConsoleCatalogClient());
        var navigation = ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(navigation.GetUriWithQueryParameter("itemId", "item-cross-cutting"));

        var page = ctx.RenderComponent<ShareManagePage>();

        page.WaitForAssertion(
            () => Assert.Contains("Share access API is not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        // No share-management panels are fabricated when unbound.
        Assert.DoesNotContain("data-share-access", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("data-share-panel=\"public-link\"", page.Markup, StringComparison.Ordinal);
    }
}
