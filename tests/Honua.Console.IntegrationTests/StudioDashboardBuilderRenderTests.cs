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
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton<IStudioDashboardPackageDataSource>(data);

        var page = ctx.Render<StudioDashboardBuilderPage>();

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
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton<IStudioDashboardPackageDataSource>(data);

        var page = ctx.Render<StudioDashboardBuilderPage>();
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
        using var ctx = new Bunit.BunitContext();
        // Switching the breakpoint marks the editor dirty, arming the <UnsavedChangesGuard/> (a JS module
        // import); run Loose JSInterop so bUnit auto-handles that import.
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioDashboardPackageDataSource>(data);

        var page = ctx.Render<StudioDashboardBuilderPage>();
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
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton<IStudioDashboardPackageDataSource>(data);

        var page = ctx.Render<StudioDashboardBuilderPage>();
        page.WaitForAssertion(() => FindButton(page, "Incomplete"), TimeSpan.FromSeconds(5));
        FindButton(page, "Incomplete").Click();

        page.WaitForAssertion(
            () => Assert.Contains("Resolve before publish", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Vega-Lite", page.Markup, StringComparison.Ordinal);
        Assert.True(FindButton(page, "Publish").HasAttribute("disabled"));
    }

    [Fact]
    public void DashboardBuilder_NewFromPrompt_RendersConversationAndLivePreview()
    {
        var data = new FakeDashboardDataSource
        {
            Workspace = new StudioDashboardWorkspace([], []),
            EditorLoad = new StudioDashboardEditorLoad(ReadyEditor(), [])
        };
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton<IStudioDashboardPackageDataSource>(data);

        var page = ctx.Render<StudioDashboardBuilderPage>();
        page.WaitForAssertion(() => FindButton(page, "New from prompt"), TimeSpan.FromSeconds(5));
        FindButton(page, "New from prompt").Click();

        // Design handoff (StudioDashboardAI): a 380px conversation column + live dashboard package preview.
        page.WaitForAssertion(
            () => Assert.Equal("ai", page.Find("[data-dashboard-builder]").GetAttribute("data-dashboard-view")),
            TimeSpan.FromSeconds(5));

        Assert.NotNull(page.Find("[data-studio-ai-pane]"));
        Assert.Contains("Dashboard from prompt", page.Markup, StringComparison.Ordinal);

        // The conversation seeds a neutral intro turn inviting the author to describe the dashboard; the
        // proposal is server-grounded and validated, never fabricated (it does not describe the draft).
        Assert.Contains("Describe the dashboard you want", page.Markup, StringComparison.Ordinal);

        // The live preview reflects the real draft's panels (the seeded chart panel) tagged Vega-Lite.
        var preview = page.Find("[data-dashboard-preview]");
        Assert.Contains("Requests by district", preview.TextContent, StringComparison.Ordinal);
        Assert.NotEmpty(page.FindAll("[data-preview-kind=\"chart\"]"));
        Assert.Contains("Vega-Lite", preview.TextContent, StringComparison.Ordinal);

        // The preview titlebar offers "Open editor" and a Vega-Lite-gated "Publish…".
        Assert.False(FindButton(page, "Publish…").HasAttribute("disabled"));
        FindButton(page, "Open editor →").Click();
        Assert.Equal("editor", page.Find("[data-dashboard-builder]").GetAttribute("data-dashboard-view"));
    }

    [Fact]
    public void DashboardBuilder_RefinePrompt_ForwardsToServerGenerationAndRendersResult()
    {
        StudioDashboardGenerationRequest? sent = null;
        var data = new FakeDashboardDataSource
        {
            Workspace = new StudioDashboardWorkspace([], []),
            EditorLoad = new StudioDashboardEditorLoad(ReadyEditor(), []),
            // A refine prompt drives the real server generation contract (not SaveDraftAsync). Return a
            // generated outcome whose proposed document hydrates the editor and surfaces "view evidence".
            OnGenerate = state => new StudioDashboardGenerationOutcome
            {
                Status = StudioDashboardGenerationStatuses.Generated,
                State = state,
                Rationale = "Added a county filter panel to the dashboard."
            }
        };
        using var ctx = new Bunit.BunitContext();
        // A generated outcome marks the editor dirty, so the UnsavedChangesGuard syncs its beforeunload
        // handler over JS interop; loose mode lets that no-op in the renderer harness.
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioDashboardPackageDataSource>(data);

        var page = ctx.Render<StudioDashboardBuilderPage>();
        page.WaitForAssertion(() => FindButton(page, "New from prompt"), TimeSpan.FromSeconds(5));
        FindButton(page, "New from prompt").Click();
        page.WaitForAssertion(() => page.Find(".studio-ai-refine-input"), TimeSpan.FromSeconds(5));

        page.Find(".studio-ai-refine-input").Input("Add a filter for county");
        page.Find(".studio-ai-send").Click();

        // The prompt is echoed and the server-produced result renders (rationale + a "view evidence" turn);
        // nothing is saved to a draft on a generate turn (SaveDraftAsync is the explicit save action).
        page.WaitForAssertion(
            () => Assert.Contains("view evidence", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Add a filter for county", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Added a county filter panel to the dashboard.", page.Markup, StringComparison.Ordinal);
        Assert.Equal(0, data.SaveCount);
    }

    [Fact]
    public void DashboardBuilder_Publish_RendersSixStepWizardWithPublishingAsSummary()
    {
        var data = new FakeDashboardDataSource
        {
            Workspace = new StudioDashboardWorkspace(
                [new StudioDashboardPackageListItem("dashboard-1", "Operations dashboard", 1, 7, 2, DateTimeOffset.UtcNow)],
                []),
            EditorLoad = new StudioDashboardEditorLoad(ReadyEditor(), [])
        };
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton<IStudioDashboardPackageDataSource>(data);

        var page = ctx.Render<StudioDashboardBuilderPage>();
        page.WaitForAssertion(() => FindButton(page, "Operations dashboard"), TimeSpan.FromSeconds(5));
        FindButton(page, "Operations dashboard").Click();
        page.WaitForAssertion(() => FindButton(page, "Publish…"), TimeSpan.FromSeconds(5));
        FindButton(page, "Publish…").Click();

        page.WaitForAssertion(
            () => Assert.Equal("publish", page.Find("[data-dashboard-builder]").GetAttribute("data-dashboard-view")),
            TimeSpan.FromSeconds(5));

        // Design handoff (StudioDashboardPublish): the 6-step stepper.
        Assert.NotNull(page.Find(".publish-wizard"));
        var steps = page.FindAll(".publish-step");
        Assert.Equal(6, steps.Count);
        foreach (var label in new[] { "Validate", "Dependencies", "Visibility", "Embed", "Rollback", "Confirm" })
        {
            Assert.Contains(steps, step => step.TextContent.Contains(label, StringComparison.Ordinal));
        }

        // First step shows the validation gate; "all checks passed" appears in the publish bar.
        Assert.Contains("all checks passed", page.Markup, StringComparison.Ordinal);
        Assert.NotNull(page.Find("[data-publish-step=\"validate\"]"));

        // Step through to Confirm, asserting each step body renders along the way.
        foreach (var step in new[] { "dependencies", "visibility", "embed", "rollback" })
        {
            page.Find(".publish-wizard-next").Click();
            Assert.NotNull(page.Find($"[data-publish-step=\"{step}\"]"));
        }

        page.Find(".publish-wizard-next").Click();

        // Confirm step: "Publishing as" summary from the real editor state + a finish action (no next).
        var summary = page.Find("[data-publish-summary=\"publishing-as\"]");
        Assert.Contains("Publishing as", summary.TextContent, StringComparison.Ordinal);
        Assert.Contains("Dashboard", summary.TextContent, StringComparison.Ordinal);
        Assert.Contains("Operations dashboard", summary.TextContent, StringComparison.Ordinal);
        Assert.Empty(page.FindAll(".publish-wizard-next"));

        // The finish action publishes through the real data source.
        page.Find(".publish-wizard-finish").Click();
        page.WaitForAssertion(
            () => Assert.True(data.PublishCount >= 1, "expected the wizard finish to call PublishAsync"),
            TimeSpan.FromSeconds(5));
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

        public int SaveCount { get; private set; }

        public int PublishCount { get; private set; }

        public Task<StudioDashboardWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Workspace);

        public Task<StudioDashboardEditorLoad> LoadAsync(string? dashboardId, CancellationToken cancellationToken = default) =>
            Task.FromResult(EditorLoad);

        public Task<StudioDashboardCommandResult> SaveDraftAsync(StudioDashboardEditorState state, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.FromResult(new StudioDashboardCommandResult(true, "Saved.", state));
        }

        public Task<StudioDashboardCommandResult> ValidateAsync(StudioDashboardEditorState state, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioDashboardCommandResult(true, "Valid.", state));

        public Task<StudioDashboardCommandResult> PublishAsync(StudioDashboardEditorState state, CancellationToken cancellationToken = default)
        {
            PublishCount++;
            state.Status = StudioDashboardStatuses.Published;
            state.PublishedVersion = state.Version;
            return Task.FromResult(new StudioDashboardCommandResult(true, "Published.", state));
        }

        public Task<StudioDashboardCommandResult> ReopenAsync(string dashboardId, int version, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioDashboardCommandResult(true, "Reopened.", new StudioDashboardEditorState { DashboardId = dashboardId, Version = version + 1 }));

        public Func<StudioDashboardEditorState, StudioDashboardGenerationOutcome>? OnGenerate { get; set; }

        public Task<StudioDashboardGenerationOutcome> GenerateAsync(StudioDashboardEditorState currentState, StudioDashboardGenerationRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(OnGenerate?.Invoke(currentState) ?? new StudioDashboardGenerationOutcome { Status = StudioDashboardGenerationStatuses.Unsupported, Rationale = "Generation not configured for this test." });
    }
}
