using AngleSharp.Dom;
using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render regression for the server-backed Studio GP/ETL workflow editor
/// (<see cref="StudioWorkflowEditorPage"/>). A save that fails honua-server (#1185) graph validation creates
/// no immutable version; the editor must keep the draft dirty so a follow-on publish cannot reuse a stale
/// prior version and silently drop the current edits. Regression for the honua-console#62 review finding
/// "failed version saves can still mark stale drafts as publishable".
/// </summary>
public sealed class StudioWorkflowEditorPageTests
{
    [Fact]
    public void Publish_AfterValidationFailedSave_DoesNotShipStalePriorVersion()
    {
        var client = new RecordingWorkflowPackageClient();
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioWorkflowPackageClient>(client);

        var page = ctx.RenderComponent<StudioWorkflowEditorPage>(
            parameters => parameters.Add(p => p.DraftId, RecordingWorkflowPackageClient.PackageId));

        // The draft (with a prior valid version) has loaded once the package-metadata form renders.
        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("section[aria-label='Package metadata'] input.console-input")),
            TimeSpan.FromSeconds(5));

        // Edit the title so the draft diverges from the last saved version (now dirty).
        TitleInput(page).Input("Parcel normalizer (edited)");

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
    }

    [Fact]
    public void Editor_RendersRunHistoryPanel_WithStateRejectedRowsAndProvenance()
    {
        var client = InMemoryStudioWorkflowPackageClient.CreateSeeded();
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioWorkflowPackageClient>(client);

        var page = ctx.RenderComponent<StudioWorkflowEditorPage>(
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
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioWorkflowPackageClient>(client);

        var page = ctx.RenderComponent<StudioWorkflowEditorPage>(
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

    private static IElement TitleInput(IRenderedComponent<StudioWorkflowEditorPage> page) =>
        page.Find("section[aria-label='Package metadata'] input.console-input");

    private static IElement FindButton(IRenderedComponent<StudioWorkflowEditorPage> page, string label) =>
        page.FindAll("button").Single(button => button.TextContent.Contains(label, StringComparison.Ordinal));

    /// <summary>
    /// Test double that always returns a bound draft carrying a prior valid version and a save that fails
    /// server validation (no version id, no binding state). Records call counts so the test can assert that a
    /// blocked save never escalates into a stale publish.
    /// </summary>
    private sealed class RecordingWorkflowPackageClient : IStudioWorkflowPackageClient
    {
        public const string PackageId = "pkg-1";

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
            Task.FromResult(new StudioWorkflowEditorContext(BindingState: null, NodeDefinitions: [], CreateBoundDraft()));

        public Task<IReadOnlyList<StudioWorkflowDraftSummary>> ListDraftsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StudioWorkflowDraftSummary>>([]);

        public Task<IReadOnlyList<StudioWorkflowNodeDefinition>> ListNodeDefinitionsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StudioWorkflowNodeDefinition>>([]);

        public Task<StudioWorkflowPackageDraft> CreateDraftAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateBoundDraft());

        public Task<StudioWorkflowPackageDraft?> GetDraftAsync(string draftId, CancellationToken cancellationToken = default) =>
            Task.FromResult<StudioWorkflowPackageDraft?>(CreateBoundDraft());

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
