using AngleSharp.Dom;
using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

// A failed validation keeps the editor dirty so a later publish cannot reuse a stale version and drop the current edits.
public sealed class StudioWorkflowEditorPageTests
{
    [Fact]
    public void RetryEditor_ExplicitGlobalChange_UpdatesNodesWithoutChangingFailurePolicy()
    {
        var client = new RecordingWorkflowPackageClient();
        client.Draft.Nodes.Add(new StudioWorkflowNode { Id = "source", Type = "test.echo", FailurePolicy = WorkflowStepFailurePolicy.Skip });
        client.Draft.Nodes.Add(new StudioWorkflowNode { Id = "sink", Type = "test.echo" });
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioWorkflowPackageClient>(client);
        var page = ctx.Render<StudioWorkflowEditorPage>(parameters => parameters.Add(p => p.DraftId, RecordingWorkflowPackageClient.PackageId));
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("input[type='number'][max='10']")), TimeSpan.FromSeconds(5));
        page.Find("input[type='number'][max='10']").Change("4");
        page.WaitForAssertion(() => Assert.All(client.Draft.Nodes, node => Assert.Equal(4, node.RetryPolicy!.MaxAttempts)));
        Assert.Equal(WorkflowStepFailurePolicy.Skip, client.Draft.Nodes[0].FailurePolicy);
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy, 0)]
    [InlineData(ConsolePresentationMode.V4, 1)]
    public async Task Publish_AfterValidationFailedSave_DoesNotShipStalePriorVersion(ConsolePresentationMode presentation, int expectedV4Headings)
    {
        var client = new RecordingWorkflowPackageClient();
        using var ctx = new Bunit.BunitContext();
        var registry = OpenWorkflowEditorContext(ctx, presentation);
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        // The page renders <UnsavedChangesGuard/>, which imports a JS module on dirty transitions; allow the
        // bUnit JSInterop to no-op those calls (the guard is exercised directly in UnsavedChangesGuardTests).
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioWorkflowPackageClient>(client);

        var page = ctx.Render<StudioWorkflowEditorPage>(
            parameters => parameters.Add(p => p.DraftId, RecordingWorkflowPackageClient.PackageId));

        // The draft (with a prior valid version) has loaded once the package-metadata form renders.
        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("section[aria-label='Package metadata'] input.console-input")),
            TimeSpan.FromSeconds(5));

        Assert.Equal(expectedV4Headings, page.FindAll(".console-page-heading-v4").Count);
        Assert.Equal("#workflow-history",
            page.Find("nav[aria-label='Workflow editor sections'] a:nth-child(4)").GetAttribute("href"));
        Assert.Equal(presentation == ConsolePresentationMode.V4 ? "SECTION" : "MAIN", page.Find(".workflow-main").TagName);
        if (presentation == ConsolePresentationMode.V4)
        {
            Assert.Equal("Workflow editor", Assert.Single(page.FindAll("h1")).TextContent);
            CatalogAdminPreview.Capture("workflow-editor-seeded", $"<div class=\"console-shell\" data-console-v4=\"true\">{page.Markup}</div>");
        }

        // Edit the title so the draft diverges from the last saved version (now dirty).
        TitleInput(page).Input("Parcel normalizer (edited)");
        Assert.Equal(presentation == ConsolePresentationMode.V4 ? 1 : 0, registry.Current.Unsaved.ChangedFieldCount);

        // Save: the server rejects the graph as invalid, so no version is created and the draft stays dirty.
        FindButton(page, "Save draft").Click();
        page.WaitForAssertion(
            () => Assert.Contains(
                "Save blocked: server validation requires attention",
                page.Markup,
                StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Equal(1, client.SaveCalls);
        Assert.Equal(0, client.PublishCalls);

        // Publish must NOT proceed on the stale prior version: it re-attempts the save (which fails again) and
        // stops before ever calling PublishAsync.
        FindButton(page, "Publish").Click();
        page.WaitForAssertion(
            () => Assert.Equal(2, client.SaveCalls),
            TimeSpan.FromSeconds(5));
        Assert.Equal(0, client.PublishCalls);
        Assert.Contains(
            "Save blocked: server validation requires attention",
            page.Markup,
            StringComparison.Ordinal);
        Assert.Equal(presentation == ConsolePresentationMode.V4 ? 1 : 0, registry.Current.Unsaved.ChangedFieldCount);
        await ctx.DisposeComponentsAsync();
        Assert.Equal(0, registry.Current.Unsaved.ChangedFieldCount);
    }

    [Fact]
    public void Editor_RendersRunHistoryPanel_WithStateRejectedRowsAndProvenance()
    {
        var client = InMemoryStudioWorkflowPackageClient.CreateSeeded();
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        // The page renders <UnsavedChangesGuard/>, which imports a JS module on dirty transitions; allow the
        // bUnit JSInterop to no-op those calls (the guard is exercised directly in UnsavedChangesGuardTests).
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioWorkflowPackageClient>(client);

        var page = ctx.Render<StudioWorkflowEditorPage>(
            parameters => parameters.Add(p => p.DraftId, InMemoryStudioWorkflowPackageClient.SeedDraftId));

        // Before any run, the panel renders its empty prompt rather than fabricated history.
        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("section[aria-label='Workflow run history']")),
            TimeSpan.FromSeconds(5));
        Assert.Contains("No runs yet", page.Markup, StringComparison.Ordinal);

        // Dry-run, then publish: the run-history panel records both runs with their state and evidence.
        FindButton(page, "Run once").Click();
        FindButton(page, "Publish").Click();

        page.WaitForAssertion(
            () => Assert.NotEmpty(
                page.FindAll("section[aria-label='Workflow run history'] li.workflow-run-item")),
            TimeSpan.FromSeconds(5));

        var historyMarkup = page.Find("section[aria-label='Workflow run history']").InnerHtml;
        Assert.Contains("succeeded", historyMarkup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Rejected rows", historyMarkup, StringComparison.Ordinal);
        Assert.Contains("Content item history", historyMarkup, StringComparison.Ordinal);
    }

    [Fact]
    public void Editor_RendersDesignLandmarks_HeaderTabsPaletteAndDagCanvas()
    {
        var client = InMemoryStudioWorkflowPackageClient.CreateSeeded();
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        // The page renders <UnsavedChangesGuard/>, which imports a JS module on dirty transitions; allow the
        // bUnit JSInterop to no-op those calls (the guard is exercised directly in UnsavedChangesGuardTests).
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioWorkflowPackageClient>(client);

        var page = ctx.Render<StudioWorkflowEditorPage>(
            parameters => parameters.Add(p => p.DraftId, InMemoryStudioWorkflowPackageClient.SeedDraftId));

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("header.workflow-editor-header")),
            TimeSpan.FromSeconds(5));

        // Header bar: editable workflow name, the draft + DAG-valid status badges, and the action buttons
        // moved into the header to match the mockup's top bar.
        var header = page.Find("header.workflow-editor-header");
        Assert.NotEmpty(header.QuerySelectorAll("input.workflow-title-input"));
        Assert.Contains("draft", header.TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DAG", header.TextContent, StringComparison.Ordinal);
        Assert.Contains(header.QuerySelectorAll("button"), b => b.TextContent.Contains("Run once", StringComparison.Ordinal));
        Assert.Contains(header.QuerySelectorAll("button"), b => b.TextContent.Contains("Save draft", StringComparison.Ordinal));
        Assert.Contains(header.QuerySelectorAll("button"), b => b.TextContent.Contains("Publish", StringComparison.Ordinal));

        // Section tabs mirror the mockup IA and anchor the canvas/schedule/secrets/history/raw regions.
        var tabRow = page.Find("nav.workflow-tab-row");
        var tabLabels = tabRow.QuerySelectorAll("a").Select(a => a.TextContent.Trim()).ToArray();
        Assert.Equal(["Graph", "Schedule", "Secrets", "Run history", "Package · raw"], tabLabels);
        foreach (var anchor in new[] { "workflow-graph", "workflow-schedule", "workflow-secrets", "workflow-history", "workflow-package" })
        {
            Assert.NotEmpty(page.FindAll($"#{anchor}"));
        }

        // Step palette renders icon glyphs + category subtitle per node definition.
        var paletteItems = page.FindAll(".workflow-palette .workflow-palette-item");
        Assert.NotEmpty(paletteItems);
        Assert.All(paletteItems, item => Assert.NotEmpty(item.QuerySelectorAll(".workflow-step-glyph")));

        // The DAG canvas draws an SVG edge layer (arrow markers + dotgrid) with absolutely-positioned nodes.
        var canvas = page.Find("#workflow-graph .workflow-canvas");
        Assert.NotEmpty(canvas.QuerySelectorAll("svg.workflow-canvas-edges"));
        Assert.NotEmpty(canvas.QuerySelectorAll("marker#workflow-arrow"));
        Assert.NotEmpty(canvas.QuerySelectorAll(".workflow-canvas-nodes .workflow-node"));
        Assert.NotEmpty(canvas.QuerySelectorAll(".workflow-canvas-edge"));
    }

    private static ConsoleAgentContextRegistry OpenWorkflowEditorContext(Bunit.BunitContext ctx, ConsolePresentationMode presentation)
    {
        var registry = new ConsoleAgentContextRegistry();
        ctx.Services.AddSingleton(registry);
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = presentation == ConsolePresentationMode.V4 });
        var navigation = ctx.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        navigation.NavigateTo("http://localhost/console/studio/workflows/pkg-1?view=graph");
        return registry;
    }

    private static IElement TitleInput(IRenderedComponent<StudioWorkflowEditorPage> page) =>
        page.Find("section[aria-label='Package metadata'] input.console-input");

    private static IElement FindButton(IRenderedComponent<StudioWorkflowEditorPage> page, string label) =>
        page.FindAll("button").Single(button => button.TextContent.Contains(label, StringComparison.Ordinal));

    // This double pairs a prior valid version with a failed server validation so the test can prove a blocked save does not publish the stale version.
    private sealed class RecordingWorkflowPackageClient : IStudioWorkflowPackageClient
    {
        public const string PackageId = "pkg-1";

        public StudioWorkflowPackageDraft Draft { get; } = CreateBoundDraft();

        public int SaveCalls { get; private set; }

        public int PublishCalls { get; private set; }

        public int DryRunCalls { get; private set; }

        private static StudioWorkflowPackageDraft CreateBoundDraft() => new()
        {
            DraftId = PackageId,
            ContentItemId = PackageId,
            CurrentVersionId = $"{PackageId}:v1",
            VersionNumber = 1,
            Title = "Parcel normalizer",
            Summary = "Nightly parcel normalizer",
            SchemaVersion = StudioWorkflowContractValues.SchemaVersion,
            LastSavedAt = DateTimeOffset.UtcNow
        };

        public Task<StudioWorkflowEditorContext> OpenEditorAsync(
            string? draftId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioWorkflowEditorContext(BindingState: null, NodeDefinitions: [], Draft));

        public Task<IReadOnlyList<StudioWorkflowDraftSummary>> ListDraftsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StudioWorkflowDraftSummary>>([]);

        public Task<IReadOnlyList<StudioWorkflowNodeDefinition>> ListNodeDefinitionsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StudioWorkflowNodeDefinition>>([]);

        public Task<StudioWorkflowPackageDraft> CreateDraftAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Draft);

        public Task<StudioWorkflowPackageDraft?> GetDraftAsync(string draftId, CancellationToken cancellationToken = default) =>
            Task.FromResult<StudioWorkflowPackageDraft?>(Draft);

        // A failed graph validation: validation issues with no version id and no binding state - the same
        // shape ServerStudioWorkflowPackageClient returns on a 400 from POST .../versions.
        public Task<StudioWorkflowSaveResult> SaveVersionAsync(
            StudioWorkflowPackageDraft draft,
            string changeNote,
            CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return Task.FromResult(new StudioWorkflowSaveResult
            {
                ContentItemId = PackageId,
                ValidationIssues =
                [
                    new StudioWorkflowValidationIssue
                    {
                        Severity = "error",
                        Scope = "graph",
                        Message = "Workflow package requires at least one sink node."
                    }
                ]
            });
        }

        public Task<StudioWorkflowDryRunResult> DryRunAsync(
            StudioWorkflowPackageDraft draft,
            CancellationToken cancellationToken = default)
        {
            DryRunCalls++;
            return Task.FromResult(new StudioWorkflowDryRunResult { Status = "succeeded" });
        }

        // Correct gating never reaches this after a blocked save; returning a "queued" publish makes a
        // regression (a stale publish) fail loudly rather than pass silently.
        public Task<StudioWorkflowPublishResult> PublishAsync(
            StudioWorkflowPackageDraft draft,
            CancellationToken cancellationToken = default)
        {
            PublishCalls++;
            return Task.FromResult(new StudioWorkflowPublishResult
            {
                PublicationId = "pub-stale",
                Status = "queued",
                Mode = draft.PublicationIntent.Mode
            });
        }

        public Task<StudioWorkflowJobEvidence?> GetJobEvidenceAsync(string jobId, CancellationToken cancellationToken = default) =>
            Task.FromResult<StudioWorkflowJobEvidence?>(null);

        public Task<StudioWorkflowRunHistory> ListRunHistoryAsync(string contentItemId, CancellationToken cancellationToken = default) =>
            Task.FromResult(StudioWorkflowRunHistory.Empty);

    }
}
