using AngleSharp.Dom;
using Bunit;
using Honua.Console.Shell.Components;
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
    public async Task Editor_RendersHeaderTabsAndRulesForBoundDraft(ConsolePresentationMode presentation)
    {
        var registry = new ConsoleAgentContextRegistry();
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(registry);
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = presentation == ConsolePresentationMode.V4 });
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<ICollectAutomationClient>(InMemoryCollectAutomationClient.CreateSeeded());

        ctx.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("/studio/automations/seed?secret=private");

        var page = ctx.Render<StudioAutomationEditorPage>(
            parameters => parameters.Add(p => p.DraftId, InMemoryCollectAutomationClient.SeedDraftId));

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("header.automation-editor-header")),
            TimeSpan.FromSeconds(5));

        var tabRow = page.Find("nav.automation-tab-row");
        var tabLabels = tabRow.QuerySelectorAll("button").Select(b => b.TextContent.Trim()).ToArray();
        Assert.Equal(["Rules", "Settings", "Contract · raw"], tabLabels);
        Assert.Equal(new string?[] { "true", "false", "false" }, tabRow.QuerySelectorAll("button").Select(b => b.GetAttribute("aria-pressed")).ToArray());

        // The Rules tab is active by default and renders the seeded rule cards.
        Assert.NotEmpty(page.FindAll("section.automation-rules article.automation-rule-card"));
        Assert.Equal(presentation == ConsolePresentationMode.V4, page.FindAll(".console-page-heading-v4").Count == 1);
        if (presentation == ConsolePresentationMode.V4)
        {
            Assert.Single(page.FindAll("h1"));
            Assert.Equal(page.FindAll(".automation-rule-card").Count, page.FindAll(".automation-rule-card > h2").Count);
            CatalogAdminPreview.Capture("automation-editor-seeded", $"<div class=\"console-shell\" data-console-v4=\"true\">{page.Markup}</div>");
        }
        if (presentation == ConsolePresentationMode.V4)
        {
            Assert.Equal("/studio/automations/seed", registry.Current.Route);
            Assert.Empty(registry.Current.Entities);
            Assert.Equal(InMemoryCollectAutomationClient.SeedDraftId, registry.Current.Filters["draftId"]);
            await page.Find("input[aria-label='Automation name']").InputAsync(new() { Value = "Changed automation" });
            Assert.Equal(1, registry.Current.Unsaved.ChangedFieldCount);
            page.FindAll("nav.automation-tab-row button").Single(button => button.TextContent.Trim() == "Settings").Click();
            Assert.Equal("settings", registry.Current.Filters["tab"]);
        }
        else Assert.Equal(0, registry.Current.Generation);
        await ctx.DisposeComponentsAsync();
        Assert.Empty(registry.Current.Filters);
        Assert.Equal(0, registry.Current.Unsaved.ChangedFieldCount);

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

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task EarlierDraftLoad_CannotOverwriteCurrentRoute(ConsolePresentationMode presentation)
    {
        var earlierLoad = new TaskCompletionSource<CollectAutomationEditorContext>();
        var client = new RecordingAutomationClient { PendingLoad = earlierLoad.Task };
        using var ctx = EditorContext(client, presentation);
        var page = ctx.Render<StudioAutomationEditorPage>(parameters => parameters.Add(p => p.DraftId, "earlier"));

        page.Render(parameters => parameters.Add(p => p.DraftId, "current"));
        page.WaitForAssertion(() => Assert.Equal("current", page.Find("input[aria-label='Automation name']").GetAttribute("value")));
        var renderCount = page.RenderCount;
        await page.InvokeAsync(() => earlierLoad.SetResult(new CollectAutomationEditorContext(null,
            new CollectAutomationDraft { DraftId = "earlier", Name = "Earlier automation" })));

        page.WaitForState(() => page.RenderCount > renderCount);
        Assert.Equal("current", page.Find("input[aria-label='Automation name']").GetAttribute("value"));
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task EarlierSave_CannotShowResultOnCurrentRoute(ConsolePresentationMode presentation)
    {
        var earlierSave = new TaskCompletionSource<CollectAutomationSaveResult>();
        var client = new RecordingAutomationClient { PendingSave = earlierSave.Task };
        using var ctx = EditorContext(client, presentation);
        var page = ctx.Render<StudioAutomationEditorPage>(parameters => parameters.Add(p => p.DraftId, "earlier"));
        var saving = page.InvokeAsync(() => SaveButton(page).ClickAsync(new()));
        page.WaitForAssertion(() => Assert.Equal(1, client.SaveCalls));

        page.Render(parameters => parameters.Add(p => p.DraftId, "current"));
        await page.Find("input[aria-label='Automation name']").InputAsync(new() { Value = "Edited current automation" });
        Assert.True(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
        await page.InvokeAsync(() => earlierSave.SetResult(new CollectAutomationSaveResult
        {
            ContentItemId = "automation-earlier",
            VersionId = "earlier-version",
            VersionNumber = 1
        }));
        await saving;

        Assert.Equal("Edited current automation", page.Find("input[aria-label='Automation name']").GetAttribute("value"));
        Assert.True(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
        Assert.Empty(page.FindAll("[data-automation-save]"));
        Assert.DoesNotContain("earlier-version", page.Markup, StringComparison.Ordinal);
        Assert.False(SaveButton(page).HasAttribute("disabled"));
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy, "saved-version")]
    [InlineData(ConsolePresentationMode.Legacy, null)]
    [InlineData(ConsolePresentationMode.V4, "saved-version")]
    [InlineData(ConsolePresentationMode.V4, null)]
    public async Task PendingSave_DisablesEditsUntilCompletion(ConsolePresentationMode presentation, string? versionId)
    {
        var completion = new TaskCompletionSource<CollectAutomationSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new RecordingAutomationClient { PendingSave = completion.Task };
        using var ctx = EditorContext(client, presentation);
        var page = ctx.Render<StudioAutomationEditorPage>(p => p.Add(x => x.DraftId, "current"));
        var saving = SaveButton(page).ClickAsync(new());
        try
        {
            page.WaitForAssertion(() => Assert.Equal(1, client.SaveCalls));
            AssertAutomationInputsDisabled(page);
            page.FindAll("nav.automation-tab-row button").Single(button => button.TextContent.Trim() == "Settings").Click();
            AssertAutomationInputsDisabled(page);
        }
        finally
        {
            completion.SetResult(new CollectAutomationSaveResult
            {
                ContentItemId = "automation-current",
                VersionId = versionId
            });
            await saving;
        }

        page.WaitForAssertion(() => Assert.False(page.Find("input[aria-label='Automation name']").HasAttribute("disabled")));
        await page.Find("input[aria-label='Automation name']").InputAsync(new() { Value = "Edited after completion" });
        page.WaitForAssertion(() => Assert.True(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty));
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task NewDraftSave_RemainsLockedWhenEarlierSaveCompletes(ConsolePresentationMode presentation)
    {
        var earlier = new TaskCompletionSource<CollectAutomationSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var current = new TaskCompletionSource<CollectAutomationSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new RecordingAutomationClient
        {
            SaveHandler = draft => draft.DraftId == "earlier" ? earlier.Task : current.Task
        };
        using var ctx = EditorContext(client, presentation);
        var page = ctx.Render<StudioAutomationEditorPage>(p => p.Add(x => x.DraftId, "earlier"));
        var earlierSave = SaveButton(page).ClickAsync(new());
        page.WaitForAssertion(() => Assert.Equal(1, client.SaveCalls));
        Task currentSave = Task.CompletedTask;
        try
        {
            page.Render(p => p.Add(x => x.DraftId, "current"));
            var name = page.Find("input[aria-label='Automation name']");
            Assert.Equal("current", name.GetAttribute("value"));
            Assert.False(name.HasAttribute("disabled"));
            Assert.False(SaveButton(page).HasAttribute("disabled"));
            name.Input("Edited current automation");
            currentSave = SaveButton(page).ClickAsync(new());
            page.WaitForAssertion(() => Assert.Equal(2, client.SaveCalls));
            AssertAutomationInputsDisabled(page);

            earlier.SetResult(new() { ContentItemId = "earlier", VersionId = "earlier-version" });
            await earlierSave;

            AssertAutomationInputsDisabled(page);
            Assert.True(SaveButton(page).HasAttribute("disabled"));
            Assert.Contains("Saving version", page.Find("[role=status]").TextContent, StringComparison.Ordinal);
            Assert.True(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
            Assert.Empty(page.FindAll("[data-automation-save]"));
            await SaveButton(page).ClickAsync(new());
            Assert.Equal(2, client.SaveCalls);
        }
        finally
        {
            earlier.TrySetResult(new());
            current.TrySetResult(new() { ContentItemId = "current", VersionId = "current-version", VersionNumber = 2 });
            await earlierSave;
            await currentSave;
        }

        Assert.False(SaveButton(page).HasAttribute("disabled"));
        Assert.False(page.Find("input[aria-label='Automation name']").HasAttribute("disabled"));
        Assert.False(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
        Assert.Contains("current-version", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("earlier-version", page.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task EarlierSaveFailure_CannotClearCurrentSaveLabelOrReportItsError(ConsolePresentationMode presentation)
    {
        var earlier = new TaskCompletionSource<CollectAutomationSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var current = new TaskCompletionSource<CollectAutomationSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new RecordingAutomationClient
        {
            SaveHandler = draft => draft.DraftId == "earlier" ? earlier.Task : current.Task
        };
        using var ctx = EditorContext(client, presentation);
        var page = ctx.Render<StudioAutomationEditorPage>(p => p.Add(x => x.DraftId, "earlier"));
        var earlierSave = SaveButton(page).ClickAsync(new());
        page.WaitForAssertion(() => Assert.Equal(1, client.SaveCalls));
        Task currentSave = Task.CompletedTask;
        try
        {
            page.Render(p => p.Add(x => x.DraftId, "current"));
            Assert.False(SaveButton(page).HasAttribute("disabled"));
            page.Find("input[aria-label='Automation name']").Input("Current automation");
            currentSave = SaveButton(page).ClickAsync(new());
            page.WaitForAssertion(() => Assert.Equal(2, client.SaveCalls));

            earlier.SetException(new HttpRequestException("Earlier request disconnected"));
            await earlierSave;

            AssertAutomationInputsDisabled(page);
            Assert.Contains("Saving version", page.Find("[role=status]").TextContent, StringComparison.Ordinal);
            Assert.DoesNotContain("Earlier request disconnected", page.Markup, StringComparison.Ordinal);
            Assert.True(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
        }
        finally
        {
            earlier.TrySetResult(new());
            current.TrySetResult(new() { ContentItemId = "current", VersionId = "current-version" });
            await earlierSave;
            await currentSave;
        }

        Assert.False(SaveButton(page).HasAttribute("disabled"));
        Assert.False(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
        Assert.DoesNotContain("Earlier request disconnected", page.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task CurrentSaveValidation_RemainsVisibleAfterEarlierSaveCompletes(ConsolePresentationMode presentation)
    {
        var earlier = new TaskCompletionSource<CollectAutomationSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var current = new TaskCompletionSource<CollectAutomationSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new RecordingAutomationClient
        {
            SaveHandler = draft => draft.DraftId == "earlier" ? earlier.Task : current.Task
        };
        using var ctx = EditorContext(client, presentation);
        var page = ctx.Render<StudioAutomationEditorPage>(p => p.Add(x => x.DraftId, "earlier"));
        var earlierSave = SaveButton(page).ClickAsync(new());
        page.WaitForAssertion(() => Assert.Equal(1, client.SaveCalls));
        Task currentSave = Task.CompletedTask;
        try
        {
            page.Render(p => p.Add(x => x.DraftId, "current"));
            Assert.False(SaveButton(page).HasAttribute("disabled"));
            page.Find("input[aria-label='Automation name']").Input("Current invalid automation");
            currentSave = SaveButton(page).ClickAsync(new());
            page.WaitForAssertion(() => Assert.Equal(2, client.SaveCalls));
            current.SetResult(new()
            {
                ValidationIssues = [new() { Severity = "error", Scope = "binding", Message = "Current validation rejected" }]
            });
            await currentSave;
            Assert.Contains("Current validation rejected", page.Markup, StringComparison.Ordinal);
            Assert.False(SaveButton(page).HasAttribute("disabled"));

            earlier.SetResult(new() { ContentItemId = "earlier", VersionId = "earlier-version" });
            await earlierSave;

            Assert.Contains("Current validation rejected", page.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("earlier-version", page.Markup, StringComparison.Ordinal);
            Assert.True(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
            Assert.Equal("Current invalid automation", page.Find("input[aria-label='Automation name']").GetAttribute("value"));
        }
        finally
        {
            earlier.TrySetResult(new());
            current.TrySetResult(new());
            await earlierSave;
            await currentSave;
        }
    }

    private static void AssertAutomationInputsDisabled(IRenderedComponent<StudioAutomationEditorPage> page)
    {
        var inputs = page.FindAll(".automation-editor-page input, .automation-editor-page textarea, .automation-editor-page select");
        Assert.NotEmpty(inputs);
        Assert.All(inputs, input => Assert.True(input.HasAttribute("disabled"), input.OuterHtml));
    }

    private static BunitContext EditorContext(RecordingAutomationClient client, ConsolePresentationMode presentation)
    {
        var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = presentation == ConsolePresentationMode.V4 });
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<ICollectAutomationClient>(client);
        return ctx;
    }

    private static IElement SaveButton(IRenderedComponent<StudioAutomationEditorPage> page) =>
        page.FindAll("button").Single(b => b.TextContent.Contains("Save version", StringComparison.Ordinal));

    // The recording client returns issues with no version id and no binding state because that is the live server's 400 shape.
    private sealed class RecordingAutomationClient : ICollectAutomationClient
    {
        public const string DraftId = "draft-recording";

        public Func<CollectAutomationDraft, Task<CollectAutomationSaveResult>>? SaveHandler { get; init; }
        public int SaveCalls { get; private set; }
        public Task<CollectAutomationEditorContext>? PendingLoad { get; init; }
        public Task<CollectAutomationSaveResult>? PendingSave { get; init; }

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

        public Task<CollectAutomationEditorContext> OpenEditorAsync(string? draftId, CancellationToken cancellationToken = default)
        {
            if (draftId == "earlier" && PendingLoad is not null) return PendingLoad;
            var draft = BoundDraft();
            draft.DraftId = draftId ?? DraftId;
            if (draftId is "earlier" or "current") draft.Name = draftId;
            return Task.FromResult(new CollectAutomationEditorContext(BindingState: null, draft));
        }

        public Task<CollectAutomationSaveResult> SaveVersionAsync(
            CollectAutomationDraft draft,
            string changeNote,
            CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            if (SaveHandler is not null) return SaveHandler(draft);
            if (PendingSave is not null) return PendingSave;
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
