using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class SharedStyleNavigationTests
{
    public enum Presentation { Legacy, V4 }
    public enum ReadStage { Stylesheet, Impact }

    [Theory]
    [InlineData(Presentation.Legacy, null)]
    [InlineData(Presentation.V4, null)]
    [InlineData(Presentation.Legacy, "")]
    [InlineData(Presentation.V4, "")]
    [InlineData(Presentation.Legacy, " ")]
    [InlineData(Presentation.V4, " ")]
    public void RemovingRouteStyleClearsCleanEditor(Presentation presentation, string? emptyStyle)
    {
        var source = new Source();
        using var context = Context(source, presentation);
        var page = Render(context, "A");
        AssertReviewed(page, "A");
        page.Render(p => p.Add(c => c.StyleId, emptyStyle));
        AssertEmpty(page);
        Assert.Single(source.Reads);
        Assert.Empty(source.Writes);
    }

    [Theory]
    [InlineData(Presentation.Legacy, ReadStage.Stylesheet)]
    [InlineData(Presentation.V4, ReadStage.Stylesheet)]
    [InlineData(Presentation.Legacy, ReadStage.Impact)]
    [InlineData(Presentation.V4, ReadStage.Impact)]
    public async Task RemovedRouteCannotBeRepopulatedByPendingRead(Presentation presentation, ReadStage stage)
    {
        var source = new Source();
        var pause = source.DelayNext(stage);
        using var context = Context(source, presentation);
        var page = Render(context, "A");
        await pause.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        page.Render(p => p.Add(c => c.StyleId, (string?)null));
        AssertEmpty(page);
        await Complete(page, pause);
        AssertEmpty(page);
        Assert.Empty(source.Writes);
        Assert.Equal(stage == ReadStage.Stylesheet ? 0 : 1, source.Impacts.Count);
    }

    [Theory]
    [InlineData(Presentation.Legacy)]
    [InlineData(Presentation.V4)]
    public void UnchangedRouteKeepsPickerSelection(Presentation presentation)
    {
        var source = new Source();
        using var context = Context(source, presentation);
        var page = Render(context, "A");
        page.Find("select[aria-label='Layer style reference']").Change("B");
        AssertReviewed(page, "B");
        page.Render(p => p.Add(c => c.StyleId, "A"));
        AssertReviewed(page, "B");
        Assert.Equal(new[] { "A", "B" }, source.Reads);
    }

    [Theory]
    [InlineData(Presentation.Legacy)]
    [InlineData(Presentation.V4)]
    public async Task UnchangedRouteKeepsPendingPickerSelection(Presentation presentation)
    {
        var source = new Source();
        using var context = Context(source, presentation);
        var page = Render(context, "A");
        var pause = source.DelayNext(ReadStage.Stylesheet);
        var selecting = page.Find("select[aria-label='Layer style reference']").ChangeAsync(new() { Value = "B" });
        await pause.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        page.Render(p => p.Add(c => c.StyleId, "A"));
        Assert.Equal("B", page.Find("select[aria-label='Layer style reference']").GetAttribute("value"));
        Assert.Equal("true", page.Find("[data-shared-style-editor]").GetAttribute("data-style-pending"));
        pause.Release.TrySetResult();
        await selecting;
        page.WaitForAssertion(() => AssertReviewed(page, "B"));
        Assert.Equal(new[] { "A", "B" }, source.Reads);
    }

    [Theory]
    [InlineData(Presentation.Legacy)]
    [InlineData(Presentation.V4)]
    public async Task NewStyleDoesNotDisplayPreviousCleanContentWhileLoading(Presentation presentation)
    {
        var source = new Source();
        using var context = Context(source, presentation);
        var page = Render(context, "A");
        var pause = source.DelayNext(ReadStage.Stylesheet);
        page.Render(p => p.Add(c => c.StyleId, "B"));
        await pause.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        AssertLoadingWithoutContent(page);
        await Complete(page, pause);
        AssertReviewed(page, "B");
    }

    [Theory]
    [InlineData(Presentation.Legacy)]
    [InlineData(Presentation.V4)]
    public async Task NewEncodingDoesNotDisplayPreviousCleanContentWhileLoading(Presentation presentation)
    {
        var source = new Source();
        using var context = Context(source, presentation);
        var page = Render(context, "A");
        var pause = source.DelayNext(ReadStage.Stylesheet);
        var changing = page.Find("[data-style-mode]").ChangeAsync(new() { Value = "Esri" });
        await pause.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        AssertLoadingWithoutContent(page);
        pause.Release.TrySetResult();
        await changing;
        page.WaitForAssertion(() => Assert.Equal("Esri-A", Content(page)));
        Assert.Equal("A", page.Find("[data-shared-style-editor]").GetAttribute("data-style-task-id"));
        Assert.False(page.Find("[data-style-save]").HasAttribute("disabled"));
    }

    [Theory]
    [InlineData(Presentation.Legacy)]
    [InlineData(Presentation.V4)]
    public void RouteRemovalRetainsDirtyCandidateUntilExplicitDiscard(Presentation presentation)
    {
        var source = new Source();
        using var context = Context(source, presentation);
        var page = Render(context, "A");
        page.Find("[data-style-content]").Input("human-candidate");
        page.Render(p => p.Add(c => c.StyleId, (string?)null));
        Assert.Equal("human-candidate", Content(page));
        Assert.Equal("A", page.Find("select[aria-label='Layer style reference']").GetAttribute("value"));
        Assert.Equal("true", page.Find("[data-shared-style-editor]").GetAttribute("data-style-dirty"));
        Assert.Single(source.Reads);
        Assert.Empty(source.Writes);
        page.Find("[data-style-reload]").Click();
        AssertReviewed(page, "A");
    }

    [Theory]
    [InlineData(Presentation.Legacy)]
    [InlineData(Presentation.V4)]
    public async Task ReturningToOriginalRouteRejectsItsEarlierRead(Presentation presentation)
    {
        var source = new Source();
        var pause = source.DelayNext(ReadStage.Stylesheet);
        using var context = Context(source, presentation);
        var page = Render(context, "A");
        await pause.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        page.Render(p => p.Add(c => c.StyleId, "B"));
        page.Render(p => p.Add(c => c.StyleId, "A"));
        AssertReviewed(page, "A");
        var epoch = page.Find("[data-shared-style-editor]").GetAttribute("data-style-observation-epoch");
        await Complete(page, pause);
        AssertReviewed(page, "A");
        Assert.Equal(epoch, page.Find("[data-shared-style-editor]").GetAttribute("data-style-observation-epoch"));
        Assert.Equal(new[] { "B", "A" }, source.Impacts);
    }

    [Theory]
    [InlineData(Presentation.Legacy)]
    [InlineData(Presentation.V4)]
    public void UnchangedEmptyRoutePreservesPickerDraft(Presentation presentation)
    {
        var source = new Source();
        using var context = Context(source, presentation);
        var page = Render(context, null);
        AssertEmpty(page);
        page.Find("select[aria-label='Layer style reference']").Change("B");
        page.Find("[data-style-content]").Input("human-candidate");
        page.Render(p => p.Add(c => c.StyleId, (string?)null));
        Assert.Equal("human-candidate", Content(page));
        Assert.Single(source.Reads);
        Assert.Empty(source.Writes);
    }

    [Theory]
    [InlineData(Presentation.Legacy)]
    [InlineData(Presentation.V4)]
    public void UnchangedRouteDoesNotReportNavigationAgainstPickerDraft(Presentation presentation)
    {
        var source = new Source();
        using var context = Context(source, presentation);
        var page = Render(context, "A");
        page.Find("select[aria-label='Layer style reference']").Change("B");
        page.Find("[data-style-content]").Input("human-candidate");
        page.Render(p => p.Add(c => c.StyleId, "A"));
        Assert.Equal("human-candidate", Content(page));
        Assert.Equal("B", page.Find("select[aria-label='Layer style reference']").GetAttribute("value"));
        Assert.Empty(page.FindAll("[data-style-result]"));
        Assert.Equal(new[] { "A", "B" }, source.Reads);
        Assert.Empty(source.Writes);
    }

    private static BunitContext Context(Source source, Presentation presentation)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.SetupModule("./_content/Honua.Console.Shell/unsaved-changes-guard.js").Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = presentation == Presentation.V4 });
        context.Services.AddSingleton<IStudioMapStyleCatalogDataSource>(source);
        return context;
    }

    private static IRenderedComponent<StudioStyleEditorPage> Render(BunitContext context, string? styleId) =>
        context.Render<StudioStyleEditorPage>(p => p.Add(c => c.StyleId, styleId));

    private static string Content(IRenderedComponent<StudioStyleEditorPage> page) =>
        page.Find("[data-style-content]").GetAttribute("value") ?? page.Find("[data-style-content]").TextContent;

    private static void AssertEmpty(IRenderedComponent<StudioStyleEditorPage> page)
    {
        Assert.Contains("Select a style to edit.", page.Markup, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("[data-style-content], [data-style-impact], [data-style-save], [data-style-result], [data-style-load-issue]"));
        Assert.Equal("false", page.Find("[data-shared-style-editor]").GetAttribute("data-style-pending"));
        Assert.Equal("", page.Find("select[aria-label='Layer style reference']").GetAttribute("value"));
    }

    private static void AssertReviewed(IRenderedComponent<StudioStyleEditorPage> page, string styleId)
    {
        Assert.Equal("MapLibre-" + styleId, Content(page));
        Assert.Equal(styleId, page.Find("[data-shared-style-editor]").GetAttribute("data-style-task-id"));
        Assert.Equal("false", page.Find("[data-shared-style-editor]").GetAttribute("data-style-dirty"));
        Assert.Equal("false", page.Find("[data-shared-style-editor]").GetAttribute("data-style-pending"));
        Assert.False(page.Find("[data-style-save]").HasAttribute("disabled"));
    }

    private static void AssertLoadingWithoutContent(IRenderedComponent<StudioStyleEditorPage> page)
    {
        Assert.Equal("", Content(page));
        Assert.True(page.Find("[data-style-content]").HasAttribute("disabled"));
        Assert.True(page.Find("[data-style-save]").HasAttribute("disabled"));
        Assert.Empty(page.FindAll("[data-style-impact]"));
    }

    private static async Task Complete(IRenderedComponent<StudioStyleEditorPage> page, Pause pause)
    {
        var renders = page.RenderCount;
        pause.Release.TrySetResult();
        await pause.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        page.WaitForState(() => page.RenderCount > renders);
    }

    private sealed class Pause
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class Source : IStudioMapStyleCatalogDataSource
    {
        private ReadStage? _stage;
        private Pause? _pause;
        public List<string> Reads { get; } = [];
        public List<string> Impacts { get; } = [];
        public List<string> Writes { get; } = [];
        public Pause DelayNext(ReadStage stage) { _stage = stage; return _pause = new(); }

        private async Task Wait(ReadStage stage)
        {
            if (_stage != stage || _pause is not { } pause) return;
            _stage = null;
            pause.Started.TrySetResult();
            await pause.Release.Task;
            pause.Completed.TrySetResult();
        }

        public Task<StudioMapStyleCatalog> GetStyleCatalogAsync(CancellationToken cancellationToken = default) => Task.FromResult(StudioMapStyleCatalog.Empty);
        public Task<string?> CaptureAuthorityAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>("operator");
        public Task<bool> IsAuthorityCurrentAsync(string authorityBinding, CancellationToken cancellationToken = default) => Task.FromResult(authorityBinding == "operator");
        public Task<HonuaAdminEndpointResult<HonuaOgcStylesheet>> GetStylesheetAsync(string styleId, HonuaOgcStyleEncoding encoding, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Use pinned reads");
        public async Task<HonuaAdminEndpointResult<HonuaOgcStylesheet>> GetStylesheetBoundAsync(string styleId, HonuaOgcStyleEncoding encoding, string authorityBinding, CancellationToken cancellationToken = default)
        {
            Reads.Add(styleId);
            await Wait(ReadStage.Stylesheet);
            return HonuaAdminEndpointResult<HonuaOgcStylesheet>.FromData(new(styleId, encoding, encoding + "-" + styleId) { EntityTag = "v7", AuthorityBinding = authorityBinding });
        }
        public async Task<HonuaAdminEndpointResult<HonuaSharedStyleImpact>> GetSharedStyleImpactBoundAsync(string styleId, string authorityBinding, CancellationToken cancellationToken = default)
        {
            Impacts.Add(styleId);
            await Wait(ReadStage.Impact);
            return HonuaAdminEndpointResult<HonuaSharedStyleImpact>.FromData(new(styleId, 7, "v7", new string('a', 64), new string('b', 64), [], true) { ImpactComplete = true });
        }
        public Task<HonuaOgcStyleSaveResult> SaveStylesheetAsync(string styleId, HonuaOgcStyleEncoding encoding, string content, bool strict, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Use pinned writes");
        public Task<HonuaOgcStyleSaveResult> SaveStylesheetBoundAsync(string styleId, HonuaOgcStyleEncoding encoding, string content, bool strict, string expectedEntityTag, string expectedImpactHash, string authorityBinding, CancellationToken cancellationToken = default)
        {
            Writes.Add(styleId);
            return Task.FromResult(HonuaOgcStyleSaveResult.Ok([]));
        }
    }
}
