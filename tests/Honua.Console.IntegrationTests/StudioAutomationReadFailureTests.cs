using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class StudioAutomationReadFailureTests
{
    [Theory]
    [InlineData(false, "open")]
    [InlineData(true, "open")]
    [InlineData(false, "list")]
    [InlineData(true, "list")]
    public void ListReadFailureRendersUnavailableInsteadOfEmpty(bool v4, string failure)
    {
        using var context = CreateContext(v4, new FailingReadClient(failure));
        var page = context.Render<StudioAutomationsPage>();

        page.WaitForAssertion(() => Assert.Contains("Could not load automations", page.Markup));
        Assert.Single(page.FindAll("section.console-state-error"));
        Assert.Empty(page.FindAll("section.console-state-empty"));
        Assert.Empty(page.FindAll("section.console-state-loading"));
        Assert.Empty(page.FindAll(".automation-card, .automation-table"));
        CapturePreview($"list-{failure}", v4, page.Markup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EditorReadFailureRendersUnavailableInsteadOfMissingDraft(bool v4)
    {
        using var context = CreateContext(v4, new FailingReadClient("open"));
        var page = context.Render<StudioAutomationEditorPage>(p => p.Add(v => v.DraftId, "failed-draft"));

        page.WaitForAssertion(() => Assert.Contains("Could not load automation draft", page.Markup));
        Assert.Single(page.FindAll("section.console-state-error"));
        Assert.Empty(page.FindAll("section.console-state-empty"));
        Assert.Empty(page.FindAll("section.console-state-loading"));
        Assert.Empty(page.FindAll(".automation-editor-header"));
        CapturePreview("editor-open", v4, page.Markup);
    }

    [Theory]
    [InlineData(false, "open")]
    [InlineData(true, "open")]
    [InlineData(false, "history")]
    [InlineData(true, "history")]
    public void HistoryReadFailureRendersUnavailableInsteadOfNoVersions(bool v4, string failure)
    {
        using var context = CreateContext(v4, new FailingReadClient(failure));
        var page = context.Render<StudioAutomationVersionsPage>(p => p.Add(v => v.DraftId, "failed-draft"));

        page.WaitForAssertion(() => Assert.Contains("Could not load automation versions", page.Markup));
        Assert.Single(page.FindAll("section.console-state-error"));
        Assert.Empty(page.FindAll("section.console-state-empty"));
        Assert.Empty(page.FindAll("section.console-state-loading"));
        Assert.Empty(page.FindAll(".automation-version-item"));
        CapturePreview($"history-{failure}", v4, page.Markup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EditorFailureFromPreviousDraftDoesNotReplaceNewDraft(bool v4)
    {
        var client = new FailingReadClient("delayed-open");
        using var context = CreateContext(v4, client);
        var page = context.Render<StudioAutomationEditorPage>(p => p.Add(v => v.DraftId, "failed-draft"));
        await client.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        page.Render(p => p.Add(v => v.DraftId, InMemoryCollectAutomationClient.SeedDraftId));
        page.WaitForState(() => page.FindAll(".automation-editor-header").Count == 1);
        var renderCount = page.RenderCount;
        await page.InvokeAsync(client.FailPendingRead);
        page.WaitForState(() => page.RenderCount > renderCount);

        page.WaitForAssertion(() =>
        {
            Assert.Empty(page.FindAll("section.console-state-error"));
            Assert.Single(page.FindAll(".automation-editor-header"));
            Assert.DoesNotContain("Could not load automation draft", page.Markup);
        });
    }

    [Theory]
    [InlineData(false, "delayed-open")]
    [InlineData(true, "delayed-open")]
    [InlineData(false, "delayed-history")]
    [InlineData(true, "delayed-history")]
    public async Task HistoryFailureFromPreviousDraftDoesNotReplaceNewHistory(bool v4, string failure)
    {
        var client = new FailingReadClient(failure);
        using var context = CreateContext(v4, client);
        var page = context.Render<StudioAutomationVersionsPage>(p => p.Add(v => v.DraftId, "failed-draft"));
        await client.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        page.Render(p => p.Add(v => v.DraftId, InMemoryCollectAutomationClient.SeedDraftId));
        page.WaitForState(() => page.FindAll(".automation-version-item").Count == 1);
        var renderCount = page.RenderCount;
        await page.InvokeAsync(client.FailPendingRead);
        page.WaitForState(() => page.RenderCount > renderCount);

        page.WaitForAssertion(() =>
        {
            Assert.Empty(page.FindAll("section.console-state-error"));
            Assert.Single(page.FindAll(".automation-version-item"));
            Assert.DoesNotContain("Could not load automation versions", page.Markup);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EditorCanOpenAnotherDraftAfterReadFailure(bool v4)
    {
        using var context = CreateContext(v4, new FailingReadClient("open"));
        var page = context.Render<StudioAutomationEditorPage>(p => p.Add(v => v.DraftId, "failed-draft"));
        page.WaitForState(() => page.FindAll("section.console-state-error").Count == 1);

        page.Render(p => p.Add(v => v.DraftId, InMemoryCollectAutomationClient.SeedDraftId));

        page.WaitForAssertion(() =>
        {
            Assert.Empty(page.FindAll("section.console-state-error"));
            Assert.Single(page.FindAll(".automation-editor-header"));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HistoryCanOpenAnotherDraftAfterReadFailure(bool v4)
    {
        using var context = CreateContext(v4, new FailingReadClient("history"));
        var page = context.Render<StudioAutomationVersionsPage>(p => p.Add(v => v.DraftId, "failed-draft"));
        page.WaitForState(() => page.FindAll("section.console-state-error").Count == 1);

        page.Render(p => p.Add(v => v.DraftId, InMemoryCollectAutomationClient.SeedDraftId));

        page.WaitForAssertion(() =>
        {
            Assert.Empty(page.FindAll("section.console-state-error"));
            Assert.Single(page.FindAll(".automation-version-item"));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfulRestoreRemainsCommittedWhenHistoryRefreshFails(bool v4)
    {
        var client = new FailingReadClient("after-restore");
        var draft = (await client.OpenEditorAsync(InMemoryCollectAutomationClient.SeedDraftId)).Draft!;
        await client.SaveVersionAsync(draft, "Second version");
        using var context = CreateContext(v4, client);
        context.JSInterop.Setup<bool>("confirm", _ => true).SetResult(true);
        var page = context.Render<StudioAutomationVersionsPage>(p => p.Add(v => v.DraftId, InMemoryCollectAutomationClient.SeedDraftId));
        var restore = page.FindAll("button").First(b => b.TextContent.Trim() == "Restore");

        await restore.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        page.WaitForAssertion(() =>
        {
            Assert.Single(page.FindAll("[data-automation-restore='committed']"));
            Assert.Contains("Could not load automation versions", page.Markup);
            Assert.Empty(page.FindAll("[data-automation-error], .automation-version-item"));
            Assert.DoesNotContain("Restore failed", page.Markup);
        });
        Assert.Equal(1, client.RestoreCount);
        Assert.Equal(3, (await client.ReadSavedHistoryAsync(draft.ContentItemId)).Versions.Count);
        CapturePreview("restore-refresh", v4, page.Markup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedDraftNavigationDiscardsPreviousDraftDirtyState(bool v4)
    {
        using var context = CreateContext(v4, new FailingReadClient("open"));
        var page = context.Render<StudioAutomationEditorPage>(p => p.Add(v => v.DraftId, InMemoryCollectAutomationClient.SeedDraftId));
        page.Find("input[aria-label='Automation name']").Input("Edited previous draft");
        Assert.True(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);

        page.Render(p => p.Add(v => v.DraftId, "failed-draft"));

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Could not load automation draft", page.Markup);
            Assert.False(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
            Assert.Empty(page.FindAll("input[aria-label='Automation name']"));
        });
    }

    private static void CapturePreview(string name, bool v4, string markup) =>
        CatalogAdminPreview.Capture($"automation-read-{name}-{(v4 ? "v4" : "legacy")}",
            $"<div class=\"console-shell\" data-console-v4=\"{v4.ToString().ToLowerInvariant()}\">{markup}</div>");

    private static BunitContext CreateContext(bool v4, ICollectAutomationClient client)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = v4 });
        context.Services.AddSingleton(client);
        return context;
    }

    private sealed class FailingReadClient(string failure) : ICollectAutomationClient
    {
        private readonly InMemoryCollectAutomationClient _inner = InMemoryCollectAutomationClient.CreateSeeded();
        private readonly TaskCompletionSource _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int RestoreCount { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void FailPendingRead() => _pending.SetException(new HttpRequestException("Read unavailable."));

        public async Task<CollectAutomationEditorContext> OpenEditorAsync(string? draftId, CancellationToken cancellationToken = default)
        {
            if (draftId is "failed-draft" or "new")
            {
                if (failure == "open") throw new HttpRequestException("Read unavailable.");
                if (failure == "delayed-open") await WaitForFailureAsync();
                return new CollectAutomationEditorContext(null, new CollectAutomationDraft { ContentItemId = "failed-content" });
            }

            return await _inner.OpenEditorAsync(draftId, cancellationToken);
        }

        public Task<IReadOnlyList<CollectAutomationSummary>> ListAutomationsAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<IReadOnlyList<CollectAutomationSummary>>(new HttpRequestException("Read unavailable."));

        public async Task<CollectAutomationVersionHistory> ListVersionsAsync(string contentItemId, CancellationToken cancellationToken = default)
        {
            if (failure == "after-restore" && RestoreCount > 0) throw new HttpRequestException("History unavailable.");
            if (contentItemId == "failed-content")
            {
                if (failure == "history") throw new HttpRequestException("Read unavailable.");
                if (failure == "delayed-history") await WaitForFailureAsync();
            }

            return await _inner.ListVersionsAsync(contentItemId, cancellationToken);
        }

        private Task WaitForFailureAsync()
        {
            Started.SetResult();
            return _pending.Task;
        }

        public Task<CollectAutomationSaveResult> SaveVersionAsync(CollectAutomationDraft draft, string changeNote, CancellationToken cancellationToken = default) =>
            _inner.SaveVersionAsync(draft, changeNote, cancellationToken);
        public Task<CollectAutomationDraft?> GetVersionAsync(string contentItemId, string versionId, CancellationToken cancellationToken = default) =>
            _inner.GetVersionAsync(contentItemId, versionId, cancellationToken);
        public Task<CollectAutomationVersionHistory> ReadSavedHistoryAsync(string contentItemId) =>
            _inner.ListVersionsAsync(contentItemId);

        public async Task<CollectAutomationRestoreResult> RestoreVersionAsync(string contentItemId, string versionId, CancellationToken cancellationToken = default)
        {
            var result = await _inner.RestoreVersionAsync(contentItemId, versionId, cancellationToken);
            RestoreCount++;
            return result;
        }
    }
}
