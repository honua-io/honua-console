using AngleSharp.Dom;
using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for the dashboard builder page (<c>/studio/dashboard</c>). Verifies the
/// missing-binding surface and that an opened dashboard renders the editor with its panels and the
/// Vega-Lite-gated publish action. Drives the page through a fake
/// <see cref="IStudioDashboardPackageDataSource"/> rather than a mock server, in line with the form
/// builder render-test pattern; the live server binding ships its own opt-in Testcontainers suite.
/// </summary>
public sealed class StudioDashboardBuilderRenderTests
{
    [Fact]
    public void DashboardBuilder_WhenBindingMissing_RendersNotBoundSurface()
    {
        var data = new FakeDashboardDataSource
        {
            Workspace = new StudioDashboardWorkspace([], [MissingBinding])
        };
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioDashboardPackageDataSource>(data);

        var page = ctx.RenderComponent<StudioDashboardBuilderPage>();

        page.WaitForAssertion(
            () => Assert.Contains("Dashboard package lifecycle is not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("data-dashboard-builder", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardBuilder_OpenReadyDashboard_RendersPanelsAndEnablesPublish()
    {
        var data = new FakeDashboardDataSource
        {
            Workspace = new StudioDashboardWorkspace(
                [new StudioDashboardPackageListItem("dashboard-1", "Operations dashboard", 1, 7, 2, DateTimeOffset.UtcNow)],
                []),
            EditorLoad = new StudioDashboardEditorLoad(ReadyEditor(), [])
        };
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioDashboardPackageDataSource>(data);

        var page = ctx.RenderComponent<StudioDashboardBuilderPage>();
        page.WaitForAssertion(() => FindButton(page, "Operations dashboard"), TimeSpan.FromSeconds(5));
        FindButton(page, "Operations dashboard").Click();

        page.WaitForAssertion(
            () => Assert.Contains("data-dashboard-builder", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // Design handoff (StudioDashboardEditor): a three-region workbench — panel list rail,
        // layout canvas, and panel inspector — driven from the bound package.
        Assert.NotNull(page.Find("[data-dashboard-region=\"panels\"]"));
        Assert.NotNull(page.Find("[data-dashboard-region=\"canvas\"]"));
        Assert.NotNull(page.Find("[data-dashboard-region=\"inspector\"]"));

        // Responsive-preview toggle offers desktop/tablet/mobile, with desktop active by default.
        var breakpointToggle = page.Find(".studio-breakpoint-toggle");
        Assert.Contains("Desktop", breakpointToggle.TextContent, StringComparison.Ordinal);
        Assert.Contains("Tablet", breakpointToggle.TextContent, StringComparison.Ordinal);
        Assert.Contains("Mobile", breakpointToggle.TextContent, StringComparison.Ordinal);

        // The chart panel appears as a selectable layout slot tagged Vega-Lite, and the inspector
        // exposes the editable Vega-Lite spec.
        Assert.Contains("Requests by district", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Vega-Lite", page.Markup, StringComparison.Ordinal);
        Assert.NotEmpty(page.FindAll(".studio-canvas-panel"));
        Assert.NotNull(page.Find(".studio-vega-spec textarea"));

        Assert.False(FindButton(page, "Publish…").HasAttribute("disabled"));
    }

    [Fact]
    public void DashboardBuilder_SwitchBreakpoint_UpdatesCanvasPreview()
    {
        var data = new FakeDashboardDataSource
        {
            Workspace = new StudioDashboardWorkspace(
                [new StudioDashboardPackageListItem("dashboard-1", "Operations dashboard", 1, 7, 2, DateTimeOffset.UtcNow)],
                []),
            EditorLoad = new StudioDashboardEditorLoad(ReadyEditor(), [])
        };
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioDashboardPackageDataSource>(data);

        var page = ctx.RenderComponent<StudioDashboardBuilderPage>();
        page.WaitForAssertion(() => FindButton(page, "Operations dashboard"), TimeSpan.FromSeconds(5));
        FindButton(page, "Operations dashboard").Click();

        page.WaitForAssertion(
            () => Assert.Equal("desktop", page.Find("[data-dashboard-region=\"canvas\"]").GetAttribute("data-dashboard-breakpoint")),
            TimeSpan.FromSeconds(5));

        page.Find(".studio-breakpoint-option[data-breakpoint=\"mobile\"]").Click();

        Assert.Equal("mobile", page.Find("[data-dashboard-region=\"canvas\"]").GetAttribute("data-dashboard-breakpoint"));
    }

    [Fact]
    public void DashboardBuilder_ChartPanelWithoutVegaLiteSpec_GatesPublish()
    {
        var editor = ReadyEditor();
        editor.Panels[0].VegaLiteSpec = "{\"mark\":\"bar\"}"; // missing $schema
        var data = new FakeDashboardDataSource
        {
            Workspace = new StudioDashboardWorkspace(
                [new StudioDashboardPackageListItem("dashboard-2", "Incomplete", 1, 0, 1, DateTimeOffset.UtcNow)],
                []),
            EditorLoad = new StudioDashboardEditorLoad(editor, [])
        };
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioDashboardPackageDataSource>(data);

        var page = ctx.RenderComponent<StudioDashboardBuilderPage>();
        page.WaitForAssertion(() => FindButton(page, "Incomplete"), TimeSpan.FromSeconds(5));
        FindButton(page, "Incomplete").Click();

        page.WaitForAssertion(
            () => Assert.Contains("Resolve before publish", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Vega-Lite", page.Markup, StringComparison.Ordinal);
        Assert.True(FindButton(page, "Publish").HasAttribute("disabled"));
    }

    private static IElement FindButton(IRenderedComponent<StudioDashboardBuilderPage> page, string label) =>
        page.FindAll("button").First(button => button.TextContent.Contains(label, StringComparison.Ordinal));

    private static StudioDashboardEditorState ReadyEditor()
    {
        var state = new StudioDashboardEditorState
        {
            DashboardId = "dashboard-1",
            Version = 2,
            Title = "Operations dashboard",
            ETag = "etag-2"
        };
        state.Bindings.Add(new StudioDashboardBindingEditor
        {
            Alias = "requests",
            ContentRef = "content:service-requests",
            VersionPin = "v5"
        });
        state.Panels.Add(new StudioDashboardPanelEditor
        {
            Title = "Requests by district",
            Kind = StudioDashboardPanelKinds.Chart,
            BindingAlias = "requests",
            VegaLiteSpec = StudioDashboardChartSpec.DefaultBarChart("district", "request_count")
        });
        return state;
    }

    private static readonly StudioDashboardCapabilityState MissingBinding = new(
        "Dashboard builder",
        "Missing binding",
        "Honua:Server:BaseUrl",
        "Configure Honua:Server:BaseUrl so the dashboard builder can bind the server-owned dashboard package lifecycle.");

    private sealed class FakeDashboardDataSource : IStudioDashboardPackageDataSource
    {
        public StudioDashboardWorkspace Workspace { get; set; } = new([], []);

        public StudioDashboardEditorLoad EditorLoad { get; set; } = new(null, []);

        public Task<StudioDashboardWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Workspace);

        public Task<StudioDashboardEditorLoad> LoadAsync(string? dashboardId, CancellationToken cancellationToken = default) =>
            Task.FromResult(EditorLoad);

        public Task<StudioDashboardCommandResult> SaveDraftAsync(StudioDashboardEditorState state, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioDashboardCommandResult(true, "Saved.", state));

        public Task<StudioDashboardCommandResult> ValidateAsync(StudioDashboardEditorState state, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioDashboardCommandResult(true, "Valid.", state));

        public Task<StudioDashboardCommandResult> PublishAsync(StudioDashboardEditorState state, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioDashboardCommandResult(true, "Published.", state));

        public Task<StudioDashboardCommandResult> ReopenAsync(string dashboardId, int version, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioDashboardCommandResult(true, "Reopened.", new StudioDashboardEditorState { DashboardId = dashboardId, Version = version + 1 }));
    }
}
