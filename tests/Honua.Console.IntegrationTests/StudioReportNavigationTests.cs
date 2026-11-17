using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class StudioReportNavigationTests
{
    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task ChangedQueryLoadsAnotherPublication(ConsolePresentationMode presentation)
    {
        using var view = new ReportView(presentation);
        await view.Complete("a");
        await view.Navigate("b");
        await view.Complete("b");
        view.AssertPublication("b");
        Assert.Equal(new[] { "a", "b" }, view.Data.Requests);
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task PendingNavigationRemovesPreviousReport(ConsolePresentationMode presentation)
    {
        using var view = new ReportView(presentation);
        await view.Complete("a");
        await view.Navigate("b");
        Assert.Empty(view.Page.FindAll("[data-report-publication]"));
        Assert.Contains("Loading report publication", view.Page.Markup, StringComparison.Ordinal);
        await view.Complete("b");
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task OldReadCannotReplaceNewPublication(ConsolePresentationMode presentation)
    {
        using var view = new ReportView(presentation);
        await view.Navigate("b");
        await view.Complete("b");
        await view.Complete("a");
        view.AssertPublication("b");
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task OldReadCannotReleaseNewLoadingState(ConsolePresentationMode presentation)
    {
        using var view = new ReportView(presentation);
        await view.Navigate("b");
        await view.Complete("a");
        Assert.Contains("Loading report publication", view.Page.Markup, StringComparison.Ordinal);
        Assert.True(view.Page.Find("button").HasAttribute("disabled"));
        await view.Complete("b");
        view.AssertPublication("b");
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task ClearingQueryInvalidatesPendingRead(ConsolePresentationMode presentation)
    {
        using var view = new ReportView(presentation);
        await view.Navigate(null);
        await view.Complete("a");
        Assert.Empty(view.Page.FindAll("[data-report-publication], [data-report-builder]"));
        Assert.DoesNotContain("Loading report publication", view.Page.Markup, StringComparison.Ordinal);
        Assert.False(view.Page.Find("button").HasAttribute("disabled"));
        Assert.Equal(string.Empty, view.Page.Find("input[placeholder='pub-report-...']").GetAttribute("value") ?? string.Empty);
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task ReturningToPublicationUsesNewVisit(ConsolePresentationMode presentation)
    {
        using var view = new ReportView(presentation);
        await view.Navigate("b");
        await view.Navigate("a");
        await view.Complete("a", "Return visit", 1);
        await view.Complete("b");
        await view.Complete("a", "Original visit");
        view.AssertPublication("a");
        Assert.Contains("Return visit", view.Page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Original visit", view.Page.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task OldMissingBindingCannotHideCurrentPublication(ConsolePresentationMode presentation)
    {
        using var view = new ReportView(presentation);
        await view.Navigate("b");
        await view.Complete("b");
        await view.CompleteMissing("a");
        view.AssertPublication("b");
        Assert.DoesNotContain("Report publication registry is not bound", view.Page.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task QueryChangeRecoversAfterMissingBinding(ConsolePresentationMode presentation)
    {
        using var view = new ReportView(presentation);
        await view.CompleteMissing("a");
        Assert.Contains("Report publication registry is not bound", view.Page.Markup, StringComparison.Ordinal);
        await view.Navigate("b");
        await view.Complete("b");
        view.AssertPublication("b");
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task SameQueryPreservesLocalDraft(ConsolePresentationMode presentation)
    {
        using var view = new ReportView(presentation);
        await view.Complete("a");
        view.Button("New report").Click();
        view.Page.Find(".studio-report-name").Input("Local draft");
        await view.Navigate("a", "unrelated");
        Assert.Equal("Local draft", view.Page.Find(".studio-report-name").GetAttribute("value"));
        Assert.Single(view.Data.Requests);
        Assert.True(view.Page.FindComponent<Honua.Console.Shell.Components.UnsavedChangesGuard>().Instance.IsDirty);
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task SameQueryDoesNotDuplicatePendingRead(ConsolePresentationMode presentation)
    {
        using var view = new ReportView(presentation);
        await view.Navigate("a", "unrelated");
        Assert.Single(view.Data.Requests);
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy, CommandOrder.CommandFirst)]
    [InlineData(ConsolePresentationMode.Legacy, CommandOrder.ReadFirst)]
    [InlineData(ConsolePresentationMode.V4, CommandOrder.CommandFirst)]
    [InlineData(ConsolePresentationMode.V4, CommandOrder.ReadFirst)]
    public async Task OldPolicyCommandCannotChangeNewVisit(ConsolePresentationMode presentation, CommandOrder order)
    {
        using var view = new ReportView(presentation);
        await view.Complete("a");
        view.Button("Edit / republish").Click();
        var command = view.Button("Update policy").ClickAsync(new MouseEventArgs());
        Assert.Equal("a", view.Data.PolicyTarget);
        await view.Navigate("b");
        if (order == CommandOrder.ReadFirst)
        {
            await view.Complete("b");
        }

        view.Data.Command.SetResult(new StudioReportCommandResult(true, "Old command finished", Publication("a")));
        await command;
        if (order == CommandOrder.CommandFirst)
        {
            Assert.True(view.Page.Find("button").HasAttribute("disabled"));
            Assert.Contains("Loading report publication", view.Page.Markup, StringComparison.Ordinal);
            await view.Complete("b");
        }

        view.AssertPublication("b");
        Assert.DoesNotContain("Old command finished", view.Page.Markup, StringComparison.Ordinal);
        Assert.Empty(view.Page.FindAll("[data-report-builder]"));
        Assert.False(view.Page.Find("button").HasAttribute("disabled"));
    }

    public enum CommandOrder { CommandFirst, ReadFirst }

    private static StudioReportPublicationView Publication(string id, string? title = null) => new(
        id, $"report-{id}", $"/published/report-{id}", "report", "active", "organization", true,
        title ?? $"Report {id}", "v1", 1, null, null, DateTimeOffset.UtcNow, []);

    private sealed class ReportView : IDisposable
    {
        private readonly BunitContext _context = new();
        public DelayedReportData Data { get; } = new();
        public IRenderedComponent<StudioReportBuilderPage> Page { get; }

        public ReportView(ConsolePresentationMode presentation)
        {
            _context.JSInterop.Mode = JSRuntimeMode.Loose;
            _context.Services.AddSingleton(new ConsoleV4Options { Enabled = presentation == ConsolePresentationMode.V4 });
            _context.Services.AddSingleton<IStudioReportPublicationDataSource>(Data);
            NavigateCore("a", null);
            Page = _context.Render<StudioReportBuilderPage>();
        }

        public Task Navigate(string? id, string? marker = null) =>
            Page.InvokeAsync(() => NavigateCore(id, marker));

        private void NavigateCore(string? id, string? marker)
        {
            var navigation = _context.Services.GetRequiredService<NavigationManager>();
            var uri = navigation.GetUriWithQueryParameter("publicationId", id);
            navigation.NavigateTo(marker is null ? uri : $"{uri}&marker={marker}");
        }

        public AngleSharp.Dom.IElement Button(string text) => Page.FindAll("button").Single(button => button.TextContent == text);

        public Task Complete(string id, string? title = null, int visit = 0) =>
            CompleteRead(Data.Read(id, visit), new StudioReportPublicationLoad(Publication(id, title), []));

        public Task CompleteMissing(string id) => CompleteRead(Data.Read(id), new StudioReportPublicationLoad(null,
            [new StudioReportCapabilityState("Report builder", "Missing binding", "report-registry", "Registry unavailable")]));

        private async Task CompleteRead(TaskCompletionSource<StudioReportPublicationLoad> completion, StudioReportPublicationLoad result)
        {
            var renderCount = Page.RenderCount;
            await Page.InvokeAsync(() => completion.SetResult(result));
            Page.WaitForState(() => Page.RenderCount > renderCount);
        }

        public void AssertPublication(string id) => Page.WaitForAssertion(() =>
            Assert.Equal(id, Assert.Single(Page.FindAll("[data-report-publication]")).GetAttribute("data-report-publication")));

        public void Dispose() => _context.Dispose();
    }

    private sealed class DelayedReportData : IStudioReportPublicationDataSource
    {
        private readonly Dictionary<string, List<TaskCompletionSource<StudioReportPublicationLoad>>> _reads = [];
        public List<string> Requests { get; } = [];
        public TaskCompletionSource<StudioReportCommandResult> Command { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? PolicyTarget { get; private set; }

        public TaskCompletionSource<StudioReportPublicationLoad> Read(string id, int visit = 0)
        {
            Assert.True(_reads.ContainsKey(id), $"No report read for {id}");
            Assert.True(_reads[id].Count > visit, $"No report visit {visit} for {id}");
            return _reads[id][visit];
        }

        public Task<StudioReportPublicationLoad> LoadAsync(string publicationId, CancellationToken cancellationToken = default)
        {
            Requests.Add(publicationId);
            var completion = new TaskCompletionSource<StudioReportPublicationLoad>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_reads.TryGetValue(publicationId, out var visits))
            {
                visits = [];
                _reads[publicationId] = visits;
            }
            visits.Add(completion);
            return completion.Task;
        }

        public Task<StudioReportCommandResult> PublishAsync(StudioReportEditorState state, CancellationToken cancellationToken = default) => Command.Task;

        public Task<StudioReportCommandResult> RollbackAsync(string publicationId, string targetVersionId, string? expectedEtag = null, CancellationToken cancellationToken = default) => Command.Task;

        public Task<StudioReportCommandResult> UpdatePolicyAsync(string publicationId, string visibility, bool embeddable, string? expectedEtag = null, CancellationToken cancellationToken = default)
        {
            PolicyTarget = publicationId;
            return Command.Task;
        }
    }
}
