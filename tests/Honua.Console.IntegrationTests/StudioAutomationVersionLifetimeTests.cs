using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class StudioAutomationVersionLifetimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposedConfirmationCannotDispatchRestore(bool v4)
    {
        var client = new DeferredVersionsClient();
        using var context = CreateContext(client, v4);
        var confirmation = context.JSInterop.Setup<bool>("confirm", _ => true);
        var page = context.Render<StudioAutomationVersionsPage>(p => p.Add(v => v.DraftId, "alpha"));
        var click = RestoreAsync(page);
        Assert.Single(context.JSInterop.Invocations["confirm"]);

        await context.DisposeComponentsAsync();
        confirmation.SetResult(true);
        await click.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(client.Restores);
        Assert.Equal(0, context.Services.GetRequiredService<ConsoleAgentContextRegistry>().Current.Unsaved.PendingOperationCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedDraftInvalidatesPendingConfirmation(bool v4)
    {
        var client = new DeferredVersionsClient();
        using var context = CreateContext(client, v4);
        var confirmation = context.JSInterop.Setup<bool>("confirm", _ => true);
        var page = context.Render<StudioAutomationVersionsPage>(p => p.Add(v => v.DraftId, "alpha"));
        var click = RestoreAsync(page);

        page.Render(p => p.Add(v => v.DraftId, "beta"));
        confirmation.SetResult(true);
        await click.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(client.Restores);
        Assert.Contains("beta history", page.Markup);
        Assert.DoesNotContain("Restored v", page.Markup);
        Assert.Empty(page.FindAll("button[disabled]"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposedOpenDoesNotReadVersionHistory(bool v4)
    {
        var client = new DeferredVersionsClient { DelayOpen = true };
        using var context = CreateContext(client, v4);
        context.Render<StudioAutomationVersionsPage>(p => p.Add(v => v.DraftId, "alpha"));
        Assert.Single(client.OpenTokens);

        await context.DisposeComponentsAsync();
        await context.Renderer.Dispatcher.InvokeAsync(() => client.OpenCompletion.SetResult());
        await context.Renderer.Dispatcher.InvokeAsync(() => Task.CompletedTask);

        Assert.True(client.OpenTokens[0].IsCancellationRequested);
        Assert.Empty(client.HistoryReads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DraftChangeCancelsOldHistoryAndKeepsCurrentRows(bool v4)
    {
        var client = new DeferredVersionsClient { DelayAlphaHistory = true };
        using var context = CreateContext(client, v4);
        var page = context.Render<StudioAutomationVersionsPage>(p => p.Add(v => v.DraftId, "alpha"));
        Assert.Single(client.HistoryReads);

        page.Render(p => p.Add(v => v.DraftId, "beta"));
        await page.InvokeAsync(() => client.HistoryCompletion.SetResult());

        page.WaitForAssertion(() =>
        {
            Assert.True(client.HistoryReads[0].Token.IsCancellationRequested);
            Assert.Contains("beta history", page.Markup);
            Assert.DoesNotContain("alpha history", page.Markup);
            Assert.Empty(page.FindAll(".console-state-loading"));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposedPreviewCancelsReadAndDoesNotRepublishContext(bool v4)
    {
        var client = new DeferredVersionsClient();
        using var context = CreateContext(client, v4);
        var registry = context.Services.GetRequiredService<ConsoleAgentContextRegistry>();
        var page = context.Render<StudioAutomationVersionsPage>(p => p.Add(v => v.DraftId, "alpha"));
        var click = page.FindAll("button").First(b => b.TextContent.Trim() == "View body")
            .ClickAsync(new MouseEventArgs());
        Assert.Single(client.PreviewTokens);

        await context.DisposeComponentsAsync();
        client.PreviewCompletion.SetResult();
        await click;

        Assert.True(client.PreviewTokens[0].IsCancellationRequested);
        Assert.Empty(registry.Current.Filters);
        Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DraftChangeCancelsPreviewAndKeepsCurrentCommandsAvailable(bool v4)
    {
        var client = new DeferredVersionsClient();
        using var context = CreateContext(client, v4);
        var page = context.Render<StudioAutomationVersionsPage>(p => p.Add(v => v.DraftId, "alpha"));
        var preview = page.FindAll("button").First(b => b.TextContent.Trim() == "View body")
            .ClickAsync(new MouseEventArgs());

        page.Render(p => p.Add(v => v.DraftId, "beta"));
        Assert.Empty(page.FindAll("button[disabled]"));
        client.PreviewCompletion.SetResult();
        await preview;

        Assert.True(client.PreviewTokens.Single().IsCancellationRequested);
        Assert.Contains("beta history", page.Markup);
        Assert.DoesNotContain("alpha history", page.Markup);
        Assert.Empty(page.FindAll("button[disabled]"));
        Assert.DoesNotContain("Hide body", page.Markup);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task RetiredRestoreDoesNotUnlockOrReplaceCurrentRestore(bool v4, bool failOldRestore)
    {
        var client = new DeferredVersionsClient();
        using var context = CreateContext(client, v4);
        context.JSInterop.Setup<bool>("confirm", _ => true).SetResult(true);
        var registry = context.Services.GetRequiredService<ConsoleAgentContextRegistry>();
        var page = context.Render<StudioAutomationVersionsPage>(p => p.Add(v => v.DraftId, "alpha"));
        var alpha = RestoreAsync(page);
        Assert.Single(client.Restores);

        page.Render(p => p.Add(v => v.DraftId, "beta"));
        Assert.Empty(page.FindAll("button[disabled]"));
        var beta = RestoreAsync(page);
        Assert.Equal(2, client.Restores.Count);
        Assert.All(page.FindAll("button"), button => Assert.True(button.HasAttribute("disabled")));
        if (v4) Assert.Equal(1, registry.Current.Unsaved.PendingOperationCount);

        client.CompleteRestore("alpha", failOldRestore);
        await alpha;

        Assert.All(page.FindAll("button"), button => Assert.True(button.HasAttribute("disabled")));
        Assert.DoesNotContain("Restored v", page.Markup);
        Assert.Empty(page.FindAll("[data-automation-error]"));
        if (v4) Assert.Equal(1, registry.Current.Unsaved.PendingOperationCount);
        Assert.Equal(2, client.HistoryReads.Count);

        client.CompleteRestore("beta");
        await beta;

        page.WaitForAssertion(() =>
        {
            Assert.Contains("beta-restored", page.Markup);
            Assert.DoesNotContain("alpha-restored", page.Markup);
            Assert.Empty(page.FindAll("button[disabled]"));
            Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount);
            Assert.Equal("beta", client.HistoryReads.Last().ContentId);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposedRestoreDoesNotReloadHistory(bool v4)
    {
        var client = new DeferredVersionsClient();
        using var context = CreateContext(client, v4);
        context.JSInterop.Setup<bool>("confirm", _ => true).SetResult(true);
        var page = context.Render<StudioAutomationVersionsPage>(p => p.Add(v => v.DraftId, "alpha"));
        var click = RestoreAsync(page);
        Assert.Single(client.Restores);
        Assert.Single(client.HistoryReads);

        await context.DisposeComponentsAsync();
        client.CompleteRestore("alpha");
        await click;

        Assert.Single(client.Restores);
        Assert.Single(client.HistoryReads);
        Assert.Equal(0, context.Services.GetRequiredService<ConsoleAgentContextRegistry>().Current.Unsaved.PendingOperationCount);
    }

    private static Task RestoreAsync(IRenderedComponent<StudioAutomationVersionsPage> page) =>
        page.FindAll("button").First(b => b.TextContent.Trim() == "Restore").ClickAsync(new MouseEventArgs());

    private static BunitContext CreateContext(ICollectAutomationClient client, bool v4)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(client);
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = v4 });
        context.Services.AddSingleton<ConsoleAgentContextRegistry>();
        return context;
    }

    private sealed class DeferredVersionsClient : ICollectAutomationClient
    {
        public bool DelayOpen { get; init; }
        public bool DelayAlphaHistory { get; init; }
        public TaskCompletionSource OpenCompletion { get; } = new();
        public TaskCompletionSource HistoryCompletion { get; } = new();
        public TaskCompletionSource PreviewCompletion { get; } = new();
        public List<CancellationToken> OpenTokens { get; } = [];
        public List<CancellationToken> PreviewTokens { get; } = [];
        public List<(string ContentId, CancellationToken Token)> HistoryReads { get; } = [];
        public Dictionary<string, TaskCompletionSource<CollectAutomationRestoreResult>> Restores { get; } = [];

        public Task<IReadOnlyList<CollectAutomationSummary>> ListAutomationsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CollectAutomationSummary>>([]);

        public async Task<CollectAutomationEditorContext> OpenEditorAsync(string? draftId, CancellationToken cancellationToken = default)
        {
            OpenTokens.Add(cancellationToken);
            if (DelayOpen) await OpenCompletion.Task;
            return new(null, new CollectAutomationDraft { ContentItemId = draftId! });
        }

        public async Task<CollectAutomationVersionHistory> ListVersionsAsync(string contentItemId, CancellationToken cancellationToken = default)
        {
            HistoryReads.Add((contentItemId, cancellationToken));
            if (DelayAlphaHistory && contentItemId == "alpha") await HistoryCompletion.Task;
            return new([new CollectAutomationVersion
            {
                ContentItemId = contentItemId,
                VersionId = $"{contentItemId}-v1",
                VersionNumber = 1,
                ChangeNote = $"{contentItemId} history",
                IsCurrent = false
            }]);
        }

        public async Task<CollectAutomationDraft?> GetVersionAsync(string contentItemId, string versionId, CancellationToken cancellationToken = default)
        {
            PreviewTokens.Add(cancellationToken);
            await PreviewCompletion.Task;
            return new CollectAutomationDraft { ContentItemId = contentItemId, FormId = contentItemId };
        }

        public Task<CollectAutomationSaveResult> SaveVersionAsync(CollectAutomationDraft draft, string changeNote, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CollectAutomationRestoreResult> RestoreVersionAsync(string contentItemId, string versionId, CancellationToken cancellationToken = default)
        {
            var completion = new TaskCompletionSource<CollectAutomationRestoreResult>();
            Restores.Add(contentItemId, completion);
            return completion.Task;
        }

        public void CompleteRestore(string contentItemId, bool fail = false)
        {
            if (fail) Restores[contentItemId].SetException(new HttpRequestException("Retired restore failed."));
            else Restores[contentItemId].SetResult(new CollectAutomationRestoreResult
            {
                ContentItemId = contentItemId,
                NewVersionId = $"{contentItemId}-restored",
                NewVersionNumber = 3
            });
        }
    }
}
