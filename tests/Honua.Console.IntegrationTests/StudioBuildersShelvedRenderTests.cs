using System.Reflection;
using Bunit;
using Honua.Console.Shell.Layout;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free coverage for the SHELVED Console Studio builder surfaces.
///
/// Product decision: "Studio" is now the realtime, SDK-driven app builder, which is not a Console
/// surface. The Console's non-realtime builders are shelved — gated OFF by default behind the
/// <c>studio-builders</c> capability and removed from navigation — but NOT deleted. These tests pin
/// both halves of that contract: with the capability unadvertised (the shipped default) every gated
/// route renders the first-class shelved state instead of the builder, and the Studio area is absent
/// from the primary rail; advertising the capability restores the surfaces unchanged (which the
/// existing builder suites already cover through <see cref="ConsoleCapabilityTestManifest"/>).
/// </summary>
public sealed class StudioBuildersShelvedRenderTests
{
    private const string ShelvedKicker = "Shelved";

    private static BunitContext ShelvedContext()
    {
        var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.AddConsoleNotifications();

        // Empty manifest = the shipped default: studio-builders is not advertised.
        ctx.Services.AddSingleton<IConsoleCapabilityManifest>(new ConsoleCapabilityManifest());

        // The shelved pages keep their real service seams; the honest "unsupported/unbound" shells stand
        // in so a failure to gate would surface as a missing-binding surface, not a DI error.
        ctx.Services.AddSingleton<IStudioAuthoringShell, UnsupportedStudioAuthoringShell>();
        ctx.Services.AddSingleton<IStudioMapPackageDataSource, UnsupportedStudioMapPackageDataSource>();
        ctx.Services.AddSingleton<IStudioMapStyleCatalogDataSource, UnsupportedStudioMapStyleCatalogDataSource>();
        ctx.Services.AddSingleton<IStudioAppPackageDataSource, UnsupportedStudioAppPackageDataSource>();
        ctx.Services.AddSingleton<IStudioDashboardPackageDataSource, UnsupportedStudioDashboardPackageDataSource>();
        ctx.Services.AddSingleton<IStudioAnalysisPackageDataSource, UnsupportedStudioAnalysisPackageDataSource>();
        ctx.Services.AddSingleton<IStudioQueryPackageDataSource, UnsupportedStudioQueryPackageDataSource>();
        ctx.Services.AddSingleton<IStudioFormPackageDataSource, UnsupportedStudioFormPackageDataSource>();
        ctx.Services.AddSingleton<IStudioReportPublicationDataSource, UnsupportedStudioReportPublicationDataSource>();
        ctx.Services.AddSingleton<IStudioWorkflowPackageClient, UnsupportedStudioWorkflowPackageClient>();
        return ctx;
    }

    private static void AssertShelved(string markup, string title)
    {
        Assert.Contains("console-state-unsupported", markup, StringComparison.Ordinal);
        Assert.Contains(ShelvedKicker, markup, StringComparison.Ordinal);
        Assert.Contains(title, markup, StringComparison.Ordinal);
        // Honest degradation: an explicit shelved state, never a blank page.
        Assert.False(string.IsNullOrWhiteSpace(markup));
    }

    [Fact]
    public void StudioPage_WhenShelved_RendersShelvedStateNotTheAuthoringShell()
    {
        using var ctx = ShelvedContext();

        var page = ctx.Render<StudioPage>();

        AssertShelved(page.Markup, "Console Studio is shelved");
        Assert.DoesNotContain("studio-home", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Generated Package Families", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("studio-prompt-input", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void MapBuilder_WhenShelved_RendersShelvedStateNotTheBuilder()
    {
        using var ctx = ShelvedContext();

        var page = ctx.Render<StudioMapBuilderPage>();

        AssertShelved(page.Markup, "The Studio map builder is shelved");
        Assert.DoesNotContain("data-map-builder", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Map package lifecycle is not bound", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AppBuilder_WhenShelved_RendersShelvedStateNotTheBuilder()
    {
        using var ctx = ShelvedContext();

        var page = ctx.Render<StudioAppBuilderPage>();

        AssertShelved(page.Markup, "The Studio app builder is shelved");
        Assert.DoesNotContain("studio-app-builder", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardBuilder_WhenShelved_RendersShelvedStateNotTheBuilder()
    {
        using var ctx = ShelvedContext();

        var page = ctx.Render<StudioDashboardBuilderPage>();

        AssertShelved(page.Markup, "The Studio dashboard builder is shelved");
        Assert.DoesNotContain("studio-dashboard-builder", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalysisBuilder_WhenShelved_RendersShelvedStateNotTheBuilder()
    {
        using var ctx = ShelvedContext();

        var page = ctx.Render<StudioAnalysisBuilderPage>();

        AssertShelved(page.Markup, "The Studio analysis builder is shelved");
        Assert.DoesNotContain("studio-analysis-builder", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void QueryBuilder_WhenShelved_RendersShelvedStateNotTheBuilder()
    {
        using var ctx = ShelvedContext();

        var page = ctx.Render<StudioQueryBuilderPage>();

        AssertShelved(page.Markup, "The Studio query builder is shelved");
        Assert.DoesNotContain("studio-query-builder", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportFromPrompt_WhenShelved_RendersShelvedStateNotTheConversation()
    {
        using var ctx = ShelvedContext();

        var page = ctx.Render<StudioReportAiPage>();

        AssertShelved(page.Markup, "Report from prompt is shelved");
        Assert.DoesNotContain("studio-report-ai-page", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void FormFromPrompt_WhenShelved_RendersShelvedStateNotTheConversation()
    {
        using var ctx = ShelvedContext();

        var page = ctx.Render<StudioFormAiPage>();

        AssertShelved(page.Markup, "Form from prompt is shelved");
        Assert.DoesNotContain("studio-form-ai-page", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkflowFromPrompt_WhenShelved_RendersShelvedStateNotTheConversation()
    {
        using var ctx = ShelvedContext();

        var page = ctx.Render<StudioWorkflowAiPage>();

        AssertShelved(page.Markup, "Workflow from prompt is shelved");
        Assert.DoesNotContain("studio-workflow-ai-page", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void PrimaryNav_WhenShelved_OmitsTheStudioArea()
    {
        using var ctx = ShelvedContext();
        ctx.Services.AddSingleton<IConsoleHostCapabilities, BrowserConsoleHostCapabilities>();

        ctx.AddAuthorization().SetAuthorized("synthetic-operator");
        var layout = ctx.Render<ConsoleLayout>();

        Assert.DoesNotContain("href=\"/studio\"", layout.Markup, StringComparison.Ordinal);
        // The back-office areas the Console keeps stay in the rail.
        Assert.Contains("href=\"/catalog\"", layout.Markup, StringComparison.Ordinal);
        Assert.Contains("href=\"/operate\"", layout.Markup, StringComparison.Ordinal);
        Assert.Contains("href=\"/share/public\"", layout.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void PrimaryNav_WhenStudioBuildersAdvertised_RestoresTheStudioArea()
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.AddConsoleNotifications();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        ctx.Services.AddSingleton<IConsoleHostCapabilities, BrowserConsoleHostCapabilities>();

        ctx.AddAuthorization().SetAuthorized("synthetic-operator");
        var layout = ctx.Render<ConsoleLayout>();

        Assert.Contains("href=\"/studio\"", layout.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void OmniPromptConsole_KeepsOperateRoute_AndDropsTheStudioAlias()
    {
        var routes = typeof(OmniPromptConsolePage)
            .GetCustomAttributes<RouteAttribute>(inherit: false)
            .Select(route => route.Template)
            .ToArray();

        // The omni-prompt console is an Operate surface and stays live; only its Studio-namespaced
        // alias was removed when the Console's Studio builders were shelved.
        Assert.Contains("/operate/ai", routes);
        Assert.DoesNotContain("/studio/ai", routes);
    }
}
