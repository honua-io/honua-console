using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class SharedStyleEditorRenderTests
{
    [Fact]
    public void ConflictRetainsDirtyContentAndUsesReviewedVersionAndImpact()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.SetupModule("./_content/Honua.Console.Shell/unsaved-changes-guard.js").Mode = JSRuntimeMode.Loose;
        var source = new Source();
        context.Services.AddSingleton<IStudioMapStyleCatalogDataSource>(source);
        var page = context.Render<StudioStyleEditorPage>(parameters => parameters.Add(component => component.StyleId, "shared"));
        page.Find("[data-style-content]").Input("{\"version\":8,\"name\":\"human-edit\"}");
        page.Find("[data-style-save]").Click();
        page.WaitForAssertion(() =>
        {
            Assert.Contains("Conflict", page.Find("[data-style-result]").TextContent, StringComparison.Ordinal);
            Assert.Contains("human-edit", page.Find("[data-style-content]").GetAttribute("value") ?? page.Find("[data-style-content]").TextContent, StringComparison.Ordinal);
            Assert.Equal("\"style-v7\"", source.SavedVersion);
            Assert.Equal(new string('b', 64), source.SavedImpact);
            Assert.Equal(1, source.Reads);
            Assert.Contains("map-package", page.Find("[data-style-consumers]").TextContent, StringComparison.Ordinal);
            Assert.Contains("published, current", page.Find("[data-style-consumers]").TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void DirtyEncodingSwitchCannotReplaceAcceptedBuffer()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.SetupModule("./_content/Honua.Console.Shell/unsaved-changes-guard.js").Mode = JSRuntimeMode.Loose;
        var source = new Source();
        context.Services.AddSingleton<IStudioMapStyleCatalogDataSource>(source);
        var page = context.Render<StudioStyleEditorPage>(parameters => parameters.Add(component => component.StyleId, "shared"));
        page.Find("[data-style-content]").Input("human-edit");
        page.Find("[data-style-mode]").Change("Esri");
        Assert.Equal(1, source.Reads);
        Assert.Contains("Unsaved edits", page.Markup, StringComparison.Ordinal);
        Assert.Contains("human-edit", page.Find("[data-style-content]").GetAttribute("value") ?? page.Find("[data-style-content]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void UnwritableImpactMakesSharedEditorReadOnly()
    {
        using var context = new BunitContext();
        var source = new Source { CanEdit = false };
        context.Services.AddSingleton<IStudioMapStyleCatalogDataSource>(source);
        var page = context.Render<StudioStyleEditorPage>(parameters => parameters.Add(component => component.StyleId, "shared"));
        Assert.True(page.Find("[data-style-content]").HasAttribute("readonly"));
        Assert.True(page.Find("[data-style-save]").HasAttribute("disabled"));
        Assert.Contains("Visible resource", page.Find("[data-style-impact]").TextContent, StringComparison.Ordinal);
        Assert.Contains("cannot be edited", page.Find("[data-style-impact]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void IncompleteImpactBlocksMutationEvenWhenCanEditIsTrue()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<IStudioMapStyleCatalogDataSource>(new Source { ImpactComplete = false });
        var page = context.Render<StudioStyleEditorPage>(parameters => parameters.Add(component => component.StyleId, "shared"));
        Assert.True(page.Find("[data-style-content]").HasAttribute("readonly"));
        Assert.True(page.Find("[data-style-save]").HasAttribute("disabled"));
        Assert.Single(page.FindAll("[data-style-impact-incomplete]"));
        Assert.Empty(page.FindAll("[data-style-consumers]"));
    }

    [Fact]
    public void ChangedAuthorityRetainsDirtyCandidateAndNeverWrites()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.SetupModule("./_content/Honua.Console.Shell/unsaved-changes-guard.js").Mode = JSRuntimeMode.Loose;
        var source = new Source();
        context.Services.AddSingleton<IStudioMapStyleCatalogDataSource>(source);
        var page = context.Render<StudioStyleEditorPage>(parameters => parameters.Add(component => component.StyleId, "shared"));
        page.Find("[data-style-content]").Input("my-unsaved-style");
        source.AuthorityCurrent = false;
        page.Find("[data-style-save]").Click();
        page.WaitForAssertion(() => Assert.Contains("Connection changed", page.Find("[data-style-result]").TextContent, StringComparison.Ordinal));
        Assert.Null(source.SavedVersion);
        Assert.Equal("true", page.Find("[data-shared-style-editor]").GetAttribute("data-style-dirty"));
        Assert.Contains("my-unsaved-style", page.Find("[data-style-content]").GetAttribute("value") ?? page.Find("[data-style-content]").TextContent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SlowPriorStyleCannotReplaceNewSelectionOrBorrowItsImpact(bool delayImpact)
    {
        using var context = new BunitContext();
        var source = new DelayedSource(delayImpact);
        context.Services.AddSingleton<IStudioMapStyleCatalogDataSource>(source);
        var page = context.Render<StudioStyleEditorPage>(parameters => parameters.Add(component => component.StyleId, "A"));
        await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        page.Render(parameters => parameters.Add(component => component.StyleId, "B"));
        page.WaitForAssertion(() => Assert.Equal("B", page.Find("[data-shared-style-editor]").GetAttribute("data-style-task-id")));
        var renderCount = page.RenderCount;
        source.Release.SetResult();
        page.WaitForState(() => page.RenderCount > renderCount);
        page.WaitForAssertion(() =>
        {
            Assert.Equal("B", page.Find("[data-shared-style-editor]").GetAttribute("data-style-task-id"));
            Assert.Contains("style-B", page.Find("[data-style-content]").GetAttribute("value") ?? page.Find("[data-style-content]").TextContent, StringComparison.Ordinal);
            Assert.DoesNotContain("style-A", page.Markup, StringComparison.Ordinal);
        });
    }

    private sealed class DelayedSource(bool delayImpact) : IStudioMapStyleCatalogDataSource
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<StudioMapStyleCatalog> GetStyleCatalogAsync(CancellationToken cancellationToken = default) => Task.FromResult(StudioMapStyleCatalog.Empty);
        private async Task Pause(string styleId, bool impact)
        {
            if (styleId == "A" && delayImpact == impact) { Started.TrySetResult(); await Release.Task; }
        }
        public async Task<HonuaAdminEndpointResult<HonuaOgcStylesheet>> GetStylesheetAsync(string styleId, HonuaOgcStyleEncoding encoding, CancellationToken cancellationToken = default)
        {
            await Pause(styleId, false);
            return HonuaAdminEndpointResult<HonuaOgcStylesheet>.FromData(new(styleId, encoding, "{\"version\":8,\"name\":\"style-" + styleId + "\"}") { EntityTag = "\"style-v7\"", AuthorityBinding = "operator-binding" });
        }
        public async Task<HonuaAdminEndpointResult<HonuaSharedStyleImpact>> GetSharedStyleImpactAsync(string styleId, CancellationToken cancellationToken = default)
        {
            await Pause(styleId, true);
            return HonuaAdminEndpointResult<HonuaSharedStyleImpact>.FromData(new(styleId, 7, "\"style-v7\"", new string('a', 64), new string('b', 64), [], true) { ImpactComplete = true });
        }
        public Task<bool> IsAuthorityCurrentAsync(string binding, CancellationToken cancellationToken = default) => Task.FromResult(binding == "operator-binding");
        public Task<HonuaAdminEndpointResult<HonuaSharedStyleImpact>> GetSharedStyleImpactBoundAsync(string styleId, string authorityBinding, CancellationToken cancellationToken = default) => GetSharedStyleImpactAsync(styleId, cancellationToken);
        public Task<HonuaOgcStyleSaveResult> SaveStylesheetAsync(string styleId, HonuaOgcStyleEncoding encoding, string content, bool strict, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    }

    private sealed class Source : IStudioMapStyleCatalogDataSource
    {
        public bool AuthorityCurrent { get; set; } = true;
        public bool CanEdit { get; init; } = true;
        public bool ImpactComplete { get; init; } = true;
        public int Reads { get; private set; }
        public string? SavedVersion { get; private set; }
        public string? SavedImpact { get; private set; }
        public Task<StudioMapStyleCatalog> GetStyleCatalogAsync(CancellationToken cancellationToken = default) => Task.FromResult(StudioMapStyleCatalog.Empty);
        public Task<HonuaAdminEndpointResult<HonuaOgcStylesheet>> GetStylesheetAsync(string styleId, HonuaOgcStyleEncoding encoding, CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(HonuaAdminEndpointResult<HonuaOgcStylesheet>.FromData(new(styleId, encoding, "{\"version\":8}") { EntityTag = "\"style-v7\"", AuthorityBinding = "operator-binding" }));
        }
        public Task<HonuaAdminEndpointResult<HonuaSharedStyleImpact>> GetSharedStyleImpactAsync(string styleId, CancellationToken cancellationToken = default) =>
            Task.FromResult(HonuaAdminEndpointResult<HonuaSharedStyleImpact>.FromData(new(styleId, 7, "\"style-v7\"", new string('a', 64), new string('b', 64), [new("resource", "Visible resource", null)], CanEdit) { ImpactComplete = ImpactComplete, Consumers = [new("map", "map-id", "map-package", new string('c', 64), "published", IsCurrent: true)] }));
        public Task<bool> IsAuthorityCurrentAsync(string binding, CancellationToken cancellationToken = default) => Task.FromResult(AuthorityCurrent && binding == "operator-binding");
        public Task<HonuaAdminEndpointResult<HonuaSharedStyleImpact>> GetSharedStyleImpactBoundAsync(string styleId, string authorityBinding, CancellationToken cancellationToken = default) => GetSharedStyleImpactAsync(styleId, cancellationToken);
        public Task<HonuaOgcStyleSaveResult> SaveStylesheetAsync(string styleId, HonuaOgcStyleEncoding encoding, string content, bool strict, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The shared UI must not use the headerless legacy write.");
        public Task<HonuaOgcStyleSaveResult> SaveStylesheetBoundAsync(string styleId, HonuaOgcStyleEncoding encoding, string content, bool strict, string expectedEntityTag, string expectedImpactHash, string authorityBinding, CancellationToken cancellationToken = default) => SaveStylesheetConditionallyAsync(styleId, encoding, content, strict, expectedEntityTag, expectedImpactHash, cancellationToken);
        public Task<HonuaOgcStyleSaveResult> SaveStylesheetConditionallyAsync(string styleId, HonuaOgcStyleEncoding encoding, string content, bool strict, string expectedEntityTag, string expectedImpactHash, CancellationToken cancellationToken = default)
        {
            SavedVersion = expectedEntityTag; SavedImpact = expectedImpactHash;
            return Task.FromResult(HonuaOgcStyleSaveResult.Fail("Conflict", "Your edits are retained."));
        }
    }
}
