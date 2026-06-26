using AngleSharp.Dom;
using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render regression for the Collect automation editor (<see cref="StudioAutomationEditorPage"/>,
/// honua-console#219). Covers the missing-binding surface, disabled-until-valid save gating, inline client
/// validation, and the invariant that a validation-failing save commits no version and surfaces its issues
/// (mirrors the workflow editor's "no stale version on a blocked save" guard).
/// </summary>
public sealed class StudioAutomationEditorPageTests
{
    [Fact]
    public void UnboundRuntime_RendersMissingBindingState()
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<ICollectAutomationClient>(new UnsupportedCollectAutomationClient());

        var page = ctx.Render<StudioAutomationEditorPage>(
            parameters => parameters.Add(p => p.DraftId, "new"));

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("section.console-state-error")),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Automation authoring is not bound", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Editor_RendersHeaderTabsAndRulesForBoundDraft()
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<ICollectAutomationClient>(InMemoryCollectAutomationClient.CreateSeeded());

        var page = ctx.Render<StudioAutomationEditorPage>(
            parameters => parameters.Add(p => p.DraftId, InMemoryCollectAutomationClient.SeedDraftId));

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("header.automation-editor-header")),
            TimeSpan.FromSeconds(5));

        var tabRow = page.Find("nav.automation-tab-row");
        var tabLabels = tabRow.QuerySelectorAll("button").Select(b => b.TextContent.Trim()).ToArray();
        Assert.Equal(["Rules", "Settings", "Package · raw"], tabLabels);

        // The Rules tab is active by default and renders the seeded rule cards.
        Assert.NotEmpty(page.FindAll("section.automation-rules article.automation-rule-card"));
    }

    [Fact]
    public void SaveButton_DisabledWhileClientValidationBlocks()
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<ICollectAutomationClient>(InMemoryCollectAutomationClient.CreateSeeded());

        // A brand-new draft has no bound form, so the client validator blocks; save must be disabled.
        var page = ctx.Render<StudioAutomationEditorPage>(
            parameters => parameters.Add(p => p.DraftId, "new"));

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("header.automation-editor-header")),
            TimeSpan.FromSeconds(5));

        var save = SaveButton(page);
        Assert.True(save.HasAttribute("disabled"));
        Assert.Contains("Resolve these before saving a version", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Save_OfInvalidBody_CommitsNoVersionAndSurfacesIssues()
    {
        var client = new RecordingAutomationClient();
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<ICollectAutomationClient>(client);

        var page = ctx.Render<StudioAutomationEditorPage>(
            parameters => parameters.Add(p => p.DraftId, RecordingAutomationClient.DraftId));

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("header.automation-editor-header")),
            TimeSpan.FromSeconds(5));

        // The recording client returns a bound, client-valid draft, so save is enabled; clicking it triggers a
        // server-side validation failure (no version id), which must surface as a blocked save.
        SaveButton(page).Click();

        page.WaitForAssertion(
            () => Assert.Contains("Save blocked", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Equal(1, client.SaveCalls);
        Assert.DoesNotContain("Saved version", page.Markup, StringComparison.Ordinal);
    }

    private static IElement SaveButton(IRenderedComponent<StudioAutomationEditorPage> page) =>
        page.FindAll("button").Single(b => b.TextContent.Contains("Save version", StringComparison.Ordinal));

    /// <summary>
    /// Returns a bound, client-valid draft but a save that fails server validation (issues with no version id
    /// and no binding state) — the shape a live server returns on a 400 from the versions endpoint.
    /// </summary>
    private sealed class RecordingAutomationClient : ICollectAutomationClient
    {
        public const string DraftId = "draft-recording";

        public int SaveCalls { get; private set; }

        private static CollectAutomationDraft BoundDraft() => new()
        {
            DraftId = DraftId,
            ContentItemId = "automation-recording",
            Name = "Recording automation",
            FormId = "form-recording",
            MaxCascadeDepth = 8,
            Rules =
            [
                new CollectAutomationRule
                {
                    Id = "rule-1",
                    Name = "Rule 1",
                    Trigger = CollectAutomationContractValues.TriggerBeforeSubmit,
                    Actions =
                    [
                        new CollectAutomationAction
                        {
                            Id = "action-1",
                            Kind = CollectAutomationContractValues.ActionValidate,
                            Expression = "always"
                        }
                    ]
                }
            ]
        };

        public Task<IReadOnlyList<CollectAutomationSummary>> ListAutomationsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CollectAutomationSummary>>([]);

        public Task<CollectAutomationEditorContext> OpenEditorAsync(string? draftId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CollectAutomationEditorContext(BindingState: null, BoundDraft()));

        public Task<CollectAutomationSaveResult> SaveVersionAsync(
            CollectAutomationDraft draft,
            string changeNote,
            CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return Task.FromResult(new CollectAutomationSaveResult
            {
                ContentItemId = draft.ContentItemId,
                ValidationIssues =
                [
                    new CollectAutomationValidationIssue
                    {
                        Severity = "error",
                        Scope = "binding",
                        Message = "Server rejected the automation body."
                    }
                ]
            });
        }

        public Task<CollectAutomationVersionHistory> ListVersionsAsync(string contentItemId, CancellationToken cancellationToken = default) =>
            Task.FromResult(CollectAutomationVersionHistory.Empty);

        public Task<CollectAutomationDraft?> GetVersionAsync(string contentItemId, string versionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<CollectAutomationDraft?>(null);

        public Task<CollectAutomationRestoreResult> RestoreVersionAsync(string contentItemId, string versionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CollectAutomationRestoreResult { ContentItemId = contentItemId });
    }
}
