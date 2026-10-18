using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class SharedStyleEditorLifecycleTests
{
    private static BunitContext Context(RaceSource source)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.SetupModule("./_content/Honua.Console.Shell/unsaved-changes-guard.js").Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IStudioMapStyleCatalogDataSource>(source);
        return context;
    }

    [Theory]
    [InlineData("validation")]
    [InlineData("write")]
    [InlineData("readback")]
    public async Task LaterInputNeverChangesSubmittedBodyOrGetsOverwrittenByReadback(string phase)
    {
        var source = new RaceSource();
        using var context = Context(source);
        var page = context.Render<StudioStyleEditorPage>(p => p.Add(c => c.StyleId, "shared"));
        var pause = source.PauseAt(phase);
        page.Find("[data-style-content]").Input("submitted-body");
        var save = page.Find("[data-style-save]").ClickAsync(new());
        await pause.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        page.Find("[data-style-content]").Input("later-human-edit");
        pause.Release.TrySetResult();
        await save;
        Assert.Equal("submitted-body", source.Submitted);
        Assert.Contains("later-human-edit", page.Find("[data-style-content]").GetAttribute("value") ?? page.Find("[data-style-content]").TextContent, StringComparison.Ordinal);
        Assert.True(page.Find("[data-style-save]").HasAttribute("disabled"));
        Assert.Contains("Update accepted", page.Markup, StringComparison.Ordinal);
        Assert.Single(source.Writes);
    }

    [Fact]
    public async Task NewInputAfterExplicitReloadConsentSurvivesThatReload()
    {
        var source = new RaceSource();
        using var context = Context(source);
        var page = context.Render<StudioStyleEditorPage>(p => p.Add(c => c.StyleId, "shared"));
        page.Find("[data-style-content]").Input("old-candidate");
        var pause = source.PauseAt("read");
        var reload = page.Find("[data-style-reload]").ClickAsync(new());
        await pause.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        page.Find("[data-style-content]").Input("new-candidate");
        pause.Release.TrySetResult();
        await reload;
        Assert.Contains("new-candidate", page.Find("[data-style-content]").GetAttribute("value") ?? page.Find("[data-style-content]").TextContent, StringComparison.Ordinal);
        Assert.True(page.Find("[data-style-save]").HasAttribute("disabled"));
        Assert.Empty(source.Writes);
    }

    [Theory]
    [InlineData("read")]
    [InlineData("write")]
    public async Task DisposalStopsFollowupReadsAndImpactProjection(string phase)
    {
        var source = new RaceSource();
        using var context = Context(source);
        var page = context.Render<StudioStyleEditorPage>(p => p.Add(c => c.StyleId, "shared"));
        var pause = source.PauseAt(phase);
        if (phase == "write") page.Find("[data-style-content]").Input("submitted-body");
        var pending = page.Find(phase == "write" ? "[data-style-save]" : "[data-style-reload]").ClickAsync(new());
        await pause.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var reads = source.Reads;
        var impacts = source.Impacts;
        await context.DisposeComponentsAsync();
        pause.Release.TrySetResult();
        await pending;
        Assert.Equal(reads, source.Reads);
        Assert.Equal(impacts, source.Impacts);
    }

    [Fact]
    public async Task DelayedOldAuthorityFailureCannotPaintNewStyle()
    {
        var source = new RaceSource();
        using var context = Context(source);
        var pause = source.PauseAt("validation");
        var page = context.Render<StudioStyleEditorPage>(p => p.Add(c => c.StyleId, "A"));
        await pause.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        page.Render(p => p.Add(c => c.StyleId, "B"));
        page.WaitForAssertion(() => Assert.Equal("B", page.Find("[data-shared-style-editor]").GetAttribute("data-style-task-id")));
        source.DelayedValidationResult = false;
        var renderCount = page.RenderCount;
        pause.Release.TrySetResult();
        await pause.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        page.WaitForState(() => page.RenderCount > renderCount);
        page.WaitForAssertion(() =>
        {
            Assert.Equal("B", page.Find("[data-shared-style-editor]").GetAttribute("data-style-task-id"));
            Assert.DoesNotContain("Connection changed", page.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void RetryRemainsPinnedToOriginalAuthorityAndNeverReadsReplacementProfile()
    {
        var source = new RaceSource();
        using var context = Context(source);
        var page = context.Render<StudioStyleEditorPage>(p => p.Add(c => c.StyleId, "shared"));
        page.Find("[data-style-content]").Input("retained-candidate");
        source.ActiveAuthority = "other-profile";
        page.Find("[data-style-reload]").Click();
        page.WaitForAssertion(() => Assert.True(page.Find("[data-style-save]").HasAttribute("disabled")));
        Assert.Equal(1, source.ForwardedReads);
        Assert.Equal(1, source.Captures);
        Assert.All(source.RequestedBindings, binding => Assert.Equal("original-profile", binding));
        Assert.Contains("retained-candidate", page.Find("[data-style-content]").GetAttribute("value") ?? page.Find("[data-style-content]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownWriteResultRequiresReadOnlyReviewBeforeAnotherWrite()
    {
        var source = new RaceSource { WriteResult = HonuaOgcStyleSaveResult.Fail("Unavailable", "Lost response") };
        using var context = Context(source);
        var page = context.Render<StudioStyleEditorPage>(p => p.Add(c => c.StyleId, "shared"));
        page.Find("[data-style-content]").Input("candidate");
        page.Find("[data-style-save]").Click();
        page.WaitForAssertion(() => Assert.Contains("Update not confirmed", page.Markup, StringComparison.Ordinal));
        Assert.True(page.Find("[data-style-save]").HasAttribute("disabled"));
        page.Find("[data-style-save]").Click();
        Assert.Single(source.Writes);
        page.Find("[data-style-reload]").Click();
        page.WaitForAssertion(() => Assert.Equal("false", page.Find("[data-shared-style-editor]").GetAttribute("data-style-dirty")));
        Assert.Single(source.Writes);
        Assert.Equal(2, source.Reads);
    }

    private sealed class Pause
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class RaceSource : IStudioMapStyleCatalogDataSource
    {
        private string? _phase;
        private Pause? _pause;
        public HonuaOgcStyleSaveResult WriteResult { get; init; } = HonuaOgcStyleSaveResult.Ok([]);
        public string ActiveAuthority { get; set; } = "original-profile";
        public bool DelayedValidationResult { get; set; } = true;
        public List<string> Writes { get; } = [];
        public List<string> RequestedBindings { get; } = [];
        public string? Submitted { get; private set; }
        public int Reads { get; private set; }
        public int ForwardedReads { get; private set; }
        public int Impacts { get; private set; }
        public int Captures { get; private set; }
        public Pause PauseAt(string phase) { _phase = phase; return _pause = new(); }
        private async Task<bool> Wait(string phase)
        {
            if (_phase != phase || _pause is not { } pause) return false;
            _phase = null;
            pause.Started.TrySetResult();
            await pause.Release.Task;
            pause.Completed.TrySetResult();
            return true;
        }
        public Task<StudioMapStyleCatalog> GetStyleCatalogAsync(CancellationToken cancellationToken = default) => Task.FromResult(StudioMapStyleCatalog.Empty);
        public Task<string?> CaptureAuthorityAsync(CancellationToken cancellationToken = default) { Captures++; return Task.FromResult<string?>(ActiveAuthority); }
        public Task<HonuaAdminEndpointResult<HonuaOgcStylesheet>> GetStylesheetAsync(string styleId, HonuaOgcStyleEncoding encoding, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Must use pinned reads");
        public async Task<HonuaAdminEndpointResult<HonuaOgcStylesheet>> GetStylesheetBoundAsync(string styleId, HonuaOgcStyleEncoding encoding, string authorityBinding, CancellationToken cancellationToken = default)
        {
            Reads++;
            RequestedBindings.Add(authorityBinding);
            if (authorityBinding != ActiveAuthority) return HonuaAdminEndpointResult<HonuaOgcStylesheet>.FromIssue(new("Connection changed", "GET /ogc/styles/{id}", "Restore the original connection."));
            ForwardedReads++;
            await Wait("read");
            if (Writes.Count > 0) await Wait("readback");
            return HonuaAdminEndpointResult<HonuaOgcStylesheet>.FromData(new(styleId, encoding, "server-" + styleId) { EntityTag = "\"style-v7\"", AuthorityBinding = authorityBinding });
        }
        public async Task<bool> IsAuthorityCurrentAsync(string binding, CancellationToken cancellationToken = default)
        {
            var delayed = await Wait("validation");
            return binding == ActiveAuthority && (!delayed || DelayedValidationResult);
        }
        public Task<HonuaAdminEndpointResult<HonuaSharedStyleImpact>> GetSharedStyleImpactBoundAsync(string styleId, string authorityBinding, CancellationToken cancellationToken = default)
        {
            Impacts++;
            return Task.FromResult(HonuaAdminEndpointResult<HonuaSharedStyleImpact>.FromData(new(styleId, 7, "\"style-v7\"", new string('a', 64), new string('b', 64), [], true) { ImpactComplete = true }));
        }
        public Task<HonuaOgcStyleSaveResult> SaveStylesheetAsync(string styleId, HonuaOgcStyleEncoding encoding, string content, bool strict, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Must use pinned writes");
        public async Task<HonuaOgcStyleSaveResult> SaveStylesheetBoundAsync(string styleId, HonuaOgcStyleEncoding encoding, string content, bool strict, string expectedEntityTag, string expectedImpactHash, string authorityBinding, CancellationToken cancellationToken = default)
        {
            Submitted = content;
            Writes.Add(styleId);
            await Wait("write");
            return WriteResult;
        }
    }
}
