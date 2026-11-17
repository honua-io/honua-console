using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class StudioFormNavigationTests
{
    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task ChangedQueryOpensAnotherForm(ConsolePresentationMode presentation)
    {
        using var view = new FormView(presentation);
        await view.Complete("a");
        view.Navigate("b");
        await view.Complete("b");
        view.AssertEditor("b");
        Assert.Equal(new[] { "a", "b" }, view.Data.Requests);
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task PendingQueryClearsPreviousEditor(ConsolePresentationMode presentation)
    {
        using var view = new FormView(presentation);
        await view.Complete("a");
        view.Navigate("b");
        view.Page.WaitForAssertion(() => Assert.Empty(view.Page.FindAll("[data-form-builder]")));
        await view.Complete("b");
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task OldReadCannotReplaceCurrentForm(ConsolePresentationMode presentation)
    {
        using var view = new FormView(presentation);
        view.Navigate("b");
        await view.Complete("b");
        await view.Complete("a");
        view.AssertEditor("b");
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task OldReadCannotReleaseCurrentBusyState(ConsolePresentationMode presentation)
    {
        using var view = new FormView(presentation);
        view.Navigate("b");
        await view.Complete("a");
        Assert.Empty(view.Page.FindAll("[data-form-builder]"));
        Assert.True(view.Button("New form").HasAttribute("disabled"));
        await view.Complete("b");
        view.AssertEditor("b");
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task ClearingQueryReturnsToListAndInvalidatesRead(ConsolePresentationMode presentation)
    {
        using var view = new FormView(presentation);
        view.Navigate(null);
        await view.Complete("a");
        Assert.Empty(view.Page.FindAll("[data-form-builder]"));
        Assert.False(view.Button("New form").HasAttribute("disabled"));
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task ReturningToFormUsesNewVisit(ConsolePresentationMode presentation)
    {
        using var view = new FormView(presentation);
        view.Navigate("b");
        view.Navigate("a");
        await view.Complete("a", "Return visit", 1);
        await view.Complete("b");
        await view.Complete("a", "Original visit");
        Assert.Equal("Return visit", view.Title.GetAttribute("value"));
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task SameQueryPreservesDraft(ConsolePresentationMode presentation)
    {
        using var view = new FormView(presentation);
        await view.Complete("a");
        view.Title.Change("Local draft");
        view.ConfirmNavigation();
        view.Navigate("a", "unrelated");
        Assert.Contains("marker=unrelated", view.Location, StringComparison.Ordinal);
        Assert.Equal("Local draft", view.Title.GetAttribute("value"));
        Assert.Single(view.Data.Requests);
        Assert.True(view.Page.FindComponent<Honua.Console.Shell.Components.UnsavedChangesGuard>().Instance.IsDirty);
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public void SameQueryDoesNotDuplicatePendingRead(ConsolePresentationMode presentation)
    {
        using var view = new FormView(presentation);
        view.Navigate("a", "unrelated");
        Assert.Single(view.Data.Requests);
        Assert.Equal(1, view.Data.WorkspaceReads);
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task UnknownQueryFallsBackToList(ConsolePresentationMode presentation)
    {
        using var view = new FormView(presentation);
        await view.Complete("a");
        view.Navigate("unknown");
        Assert.Empty(view.Page.FindAll("[data-form-builder]"));
        Assert.False(view.Button("New form").HasAttribute("disabled"));
        Assert.Single(view.Data.Requests);
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task OldWorkspaceCannotOpenAFormOrReleaseNewLoading(ConsolePresentationMode presentation)
    {
        using var view = new FormView(presentation, WorkspaceMode.Delayed);
        view.Navigate("b");
        await view.CompleteWorkspace(0);
        Assert.Contains("Loading forms", view.Page.Markup, StringComparison.Ordinal);
        Assert.Empty(view.Data.Requests);
        await view.Page.InvokeAsync(() => view.Data.Workspaces[1].SetResult(DelayedFormData.Workspace));
        view.Page.WaitForAssertion(() => Assert.Equal(new[] { "b" }, view.Data.Requests));
        await view.Complete("b");
        view.AssertEditor("b");
        Assert.Equal(new[] { "b" }, view.Data.Requests);
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy, CommandOrder.CommandFirst)]
    [InlineData(ConsolePresentationMode.Legacy, CommandOrder.ReadFirst)]
    [InlineData(ConsolePresentationMode.V4, CommandOrder.CommandFirst)]
    [InlineData(ConsolePresentationMode.V4, CommandOrder.ReadFirst)]
    public async Task OldSaveCannotChangeCurrentVisit(ConsolePresentationMode presentation, CommandOrder order)
    {
        using var view = new FormView(presentation);
        await view.Complete("a");
        var command = view.Button("Save draft").ClickAsync(new MouseEventArgs());
        Assert.Equal("a", view.Data.SaveTarget);
        view.Navigate("b");
        if (order == CommandOrder.ReadFirst)
        {
            await view.Complete("b");
        }
        view.Data.Command.SetResult(new StudioFormCommandResult(true, "Old save finished", Editor("a")));
        await command;
        if (order == CommandOrder.CommandFirst)
        {
            Assert.True(view.Button("New form").HasAttribute("disabled"));
            await view.Complete("b");
        }
        view.AssertEditor("b");
        Assert.DoesNotContain("Old save finished", view.Page.Markup, StringComparison.Ordinal);
        Assert.False(view.Button("Save draft").HasAttribute("disabled"));
    }

    public enum CommandOrder { CommandFirst, ReadFirst }
    public enum WorkspaceMode { Immediate, Delayed }

    private static StudioFormEditorState Editor(string id, string? title = null)
    {
        var editor = new StudioFormEditorState { FormId = id, Title = title ?? $"Form {id}", ServiceId = "inspections", RequiresGeometry = false };
        editor.Fields.Add(new StudioFormFieldEditor { FieldId = "name", Label = "Name", TargetField = "name" });
        editor.SavedSignature = StudioFormPackageMapper.ComputeContentSignature(editor);
        return editor;
    }

    private sealed class FormView : IDisposable
    {
        private readonly BunitContext _context = new();
        public DelayedFormData Data { get; }
        public IRenderedComponent<StudioFormBuilderPage> Page { get; }
        public string Location => _context.Services.GetRequiredService<NavigationManager>().Uri;
        public void ConfirmNavigation() => _context.JSInterop.Setup<bool>("confirm", _ => true).SetResult(true);
        public AngleSharp.Dom.IElement Title => Page.Find("input[placeholder='Field inspection form']");

        public FormView(ConsolePresentationMode presentation, WorkspaceMode mode = WorkspaceMode.Immediate)
        {
            Data = new DelayedFormData(mode);
            _context.JSInterop.Mode = JSRuntimeMode.Loose;
            _context.Services.AddSingleton(new ConsoleV4Options { Enabled = presentation == ConsolePresentationMode.V4 });
            _context.Services.AddSingleton<IStudioFormPackageDataSource>(Data);
            Navigate("a");
            Page = _context.Render<StudioFormBuilderPage>();
        }

        public void Navigate(string? id, string? marker = null)
        {
            var navigation = _context.Services.GetRequiredService<NavigationManager>();
            var uri = navigation.GetUriWithQueryParameter("formId", id);
            navigation.NavigateTo(marker is null ? uri : $"{uri}&marker={marker}");
        }

        public AngleSharp.Dom.IElement Button(string text) => Page.FindAll("button").Single(button => button.TextContent == text);

        public Task Complete(string id, string? title = null, int visit = 0) =>
            CompleteResult(Data.Reads[id][visit], new StudioFormEditorLoad(Editor(id, title), []));

        public Task CompleteWorkspace(int visit) => CompleteResult(Data.Workspaces[visit], DelayedFormData.Workspace);

        private async Task CompleteResult<T>(TaskCompletionSource<T> completion, T result)
        {
            var renders = Page.RenderCount;
            await Page.InvokeAsync(() => completion.SetResult(result));
            Page.WaitForState(() => Page.RenderCount > renders);
        }

        public void AssertEditor(string id) => Page.WaitForAssertion(() => Assert.Equal($"Form {id}", Title.GetAttribute("value")));
        public void Dispose() => _context.Dispose();
    }

    private sealed class DelayedFormData(WorkspaceMode mode) : IStudioFormPackageDataSource
    {
        public static StudioFormWorkspace Workspace { get; } = new(
            new[] { "a", "b" }.Select(id => new StudioFormPackageListItem(id, $"Form {id}", "inspections", 0, 1, null, DateTimeOffset.UtcNow)).ToArray(), []);
        public Dictionary<string, List<TaskCompletionSource<StudioFormEditorLoad>>> Reads { get; } = [];
        public List<TaskCompletionSource<StudioFormWorkspace>> Workspaces { get; } = [];
        public List<string?> Requests { get; } = [];
        public int WorkspaceReads { get; private set; }
        public string? SaveTarget { get; private set; }
        public TaskCompletionSource<StudioFormCommandResult> Command { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<StudioFormWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default)
        {
            WorkspaceReads++;
            if (mode == WorkspaceMode.Immediate)
            {
                return Task.FromResult(Workspace);
            }
            var completion = new TaskCompletionSource<StudioFormWorkspace>(TaskCreationOptions.RunContinuationsAsynchronously);
            Workspaces.Add(completion);
            return completion.Task;
        }

        public Task<StudioFormEditorLoad> LoadAsync(string? formId, CancellationToken cancellationToken = default)
        {
            Requests.Add(formId);
            var completion = new TaskCompletionSource<StudioFormEditorLoad>(TaskCreationOptions.RunContinuationsAsynchronously);
            var key = formId ?? string.Empty;
            if (!Reads.TryGetValue(key, out var visits))
            {
                visits = [];
                Reads[key] = visits;
            }
            visits.Add(completion);
            return completion.Task;
        }

        public Task<StudioFormCommandResult> SaveDraftAsync(StudioFormEditorState state, CancellationToken cancellationToken = default)
        {
            SaveTarget = state.FormId;
            return Command.Task;
        }
        public Task<StudioFormCommandResult> ValidateAsync(StudioFormEditorState state, CancellationToken cancellationToken = default) => Command.Task;
        public Task<StudioFormCommandResult> PublishAsync(StudioFormEditorState state, CancellationToken cancellationToken = default) => Command.Task;
        public Task<StudioFormCommandResult> ReopenAsync(string formId, int version, CancellationToken cancellationToken = default) => Command.Task;
        public Task<StudioFormOfflinePolicyView> GetOfflinePolicyAsync(string formId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioFormOfflinePolicyView(false, []));
    }
}
