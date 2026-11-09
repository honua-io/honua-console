using AngleSharp.Dom;
using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class StudioAutomationEditorPageTests
{
    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public void UnboundRuntime_RendersMissingBindingState(ConsolePresentationMode presentation)
    {
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = presentation == ConsolePresentationMode.V4 });
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<ICollectAutomationClient>(new UnsupportedCollectAutomationClient());

        var page = ctx.Render<StudioAutomationEditorPage>(
            parameters => parameters.Add(p => p.DraftId, "new"));

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("section.console-state-error")),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Automation authoring is not bound", page.Markup, StringComparison.Ordinal);
        Assert.Equal(presentation == ConsolePresentationMode.V4, page.FindAll(".console-page-heading-v4").Count == 1);
        if (presentation == ConsolePresentationMode.V4)
        {
            Assert.Single(page.FindAll("h1"));
            CatalogAdminPreview.Capture("automation-editor-unbound", $"<div class=\"console-shell\" data-console-v4=\"true\">{page.Markup}</div>");
        }
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public void Editor_RendersHeaderTabsAndRulesForBoundDraft(ConsolePresentationMode presentation)
    {
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = presentation == ConsolePresentationMode.V4 });
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
        Assert.Equal(presentation == ConsolePresentationMode.V4, page.FindAll(".console-page-heading-v4").Count == 1);
        if (presentation == ConsolePresentationMode.V4)
        {
            Assert.Single(page.FindAll("h1"));
            Assert.Equal(page.FindAll(".automation-rule-card").Count, page.FindAll(".automation-rule-card > h2").Count);
            CatalogAdminPreview.Capture("automation-editor-seeded", $"<div class=\"console-shell\" data-console-v4=\"true\">{page.Markup}</div>");
        }
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public void SaveButton_DisabledWhileClientValidationBlocks(ConsolePresentationMode presentation)
    {
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = presentation == ConsolePresentationMode.V4 });
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

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public void Save_OfInvalidBody_CommitsNoVersionAndSurfacesIssues(ConsolePresentationMode presentation)
    {
        var client = new RecordingAutomationClient();
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = presentation == ConsolePresentationMode.V4 });
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

    // The recording client returns issues with no version id and no binding state because that is the live server's 400 shape.
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
