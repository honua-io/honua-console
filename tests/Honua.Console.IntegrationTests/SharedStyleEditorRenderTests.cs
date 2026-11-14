using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class SharedStyleEditorRenderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void V4FlagChangesOnlyThePresentation(bool v4)
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.SetupModule("./_content/Honua.Console.Shell/unsaved-changes-guard.js").Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = v4 });
        context.Services.AddSingleton<IStudioMapStyleCatalogDataSource>(new Source());
        var page = context.Render<StudioStyleEditorPage>(parameters => parameters.Add(component => component.StyleId, "shared"));

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-style-consumers] li")));
        Assert.Equal(v4 ? "Styles" : "Shared catalog style editor", page.Find("h1").TextContent);
        Assert.Equal(v4 ? 1 : 0, page.FindAll(".console-page-heading-v4").Count);
        Assert.Single(page.FindAll("[data-shared-style-scope]"));
        Assert.Single(page.FindAll("[data-style-content]"));
        Assert.Single(page.FindAll("[data-style-save]"));
        Assert.Equal(2, page.FindAll(v4 ? "[data-style-impact] h3" : "[data-style-impact] h2").Count);
        Assert.Equal(v4 ? 1 : 2, page.FindAll(".console-panel").Count);
        if (v4)
        {
            Assert.Equal("Style", page.Find(".console-panel h2").TextContent);
            Assert.Empty(page.FindAll(".console-panel .console-panel"));
            CatalogAdminPreview.CaptureV4("studio-styles-v4", page.Markup);
        }
        else
        {
            Assert.Equal("console-page studio-style-editor", page.Find("[data-shared-style-editor]").GetAttribute("class"));
            Assert.Equal("Studio / Styles", page.Find(".console-page-heading .console-kicker").TextContent);
            Assert.Equal("note", page.Find("article[data-shared-style-scope]").GetAttribute("role"));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConflictRetainsDirtyContentAndUsesReviewedVersionAndImpact(bool v4)
    {
        using var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = v4 });
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
        Assert.True(page.Find("[data-style-result]").ClassList.Contains("console-state-danger"));
        if (v4) CatalogAdminPreview.CaptureV4("studio-styles-conflict-v4", page.Markup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AcceptedUpdateWithFailedReadbackRetainsCandidateAndNeverReplaysWrite(bool v4)
    {
        using var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = v4 });
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.SetupModule("./_content/Honua.Console.Shell/unsaved-changes-guard.js").Mode = JSRuntimeMode.Loose;
        var source = new Source { WriteSucceeds = true, FailReadback = true };
        context.Services.AddSingleton<IStudioMapStyleCatalogDataSource>(source);
        var page = context.Render<StudioStyleEditorPage>(parameters => parameters.Add(component => component.StyleId, "shared"));
        var acceptedEpoch = page.Find("[data-shared-style-editor]").GetAttribute("data-style-observation-epoch");
        page.Find("[data-style-content]").Input("human-candidate");
        page.Find("[data-style-save]").Click();
        page.WaitForAssertion(() =>
        {
            Assert.Single(page.FindAll("[data-style-readback-pending]"));
            Assert.Contains("Update accepted", page.Find("[data-style-result]").TextContent, StringComparison.Ordinal);
            Assert.Contains("human-candidate", page.Find("[data-style-content]").GetAttribute("value") ?? page.Find("[data-style-content]").TextContent, StringComparison.Ordinal);
            Assert.Equal("true", page.Find("[data-shared-style-editor]").GetAttribute("data-style-dirty"));
            Assert.Equal(acceptedEpoch, page.Find("[data-shared-style-editor]").GetAttribute("data-style-observation-epoch"));
            Assert.True(page.Find("[data-style-save]").HasAttribute("disabled"));
            Assert.Equal(1, source.Writes);
        });
        page.Find("[data-style-save]").Click();
        Assert.Equal(1, source.Writes);
        page.Find("[data-style-reload]").Click();
        page.WaitForAssertion(() => Assert.Equal(3, source.Reads));
        Assert.Contains("human-candidate", page.Find("[data-style-content]").GetAttribute("value") ?? page.Find("[data-style-content]").TextContent, StringComparison.Ordinal);
        source.FailReadback = false;
        page.Find("[data-style-reload]").Click();
        page.WaitForAssertion(() =>
        {
            Assert.Empty(page.FindAll("[data-style-readback-pending]"));
            Assert.Equal("false", page.Find("[data-shared-style-editor]").GetAttribute("data-style-dirty"));
            Assert.Equal("8", page.Find("[data-shared-style-editor]").GetAttribute("data-style-task-revision"));
            Assert.Contains("server-canonical", page.Find("[data-style-content]").GetAttribute("value") ?? page.Find("[data-style-content]").TextContent, StringComparison.Ordinal);
            Assert.NotEqual(acceptedEpoch, page.Find("[data-shared-style-editor]").GetAttribute("data-style-observation-epoch"));
            Assert.Equal(1, source.Writes);
            Assert.True(page.Find("[data-style-strict]").HasAttribute("checked"));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirtyEncodingSwitchCannotReplaceAcceptedBuffer(bool v4)
    {
        using var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = v4 });
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnwritableImpactMakesSharedEditorReadOnly(bool v4)
    {
        using var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = v4 });
        var source = new Source { CanEdit = false };
        context.Services.AddSingleton<IStudioMapStyleCatalogDataSource>(source);
        var page = context.Render<StudioStyleEditorPage>(parameters => parameters.Add(component => component.StyleId, "shared"));
        Assert.True(page.Find("[data-style-content]").HasAttribute("readonly"));
        Assert.True(page.Find("[data-style-save]").HasAttribute("disabled"));
        Assert.Contains("Visible resource", page.Find("[data-style-impact]").TextContent, StringComparison.Ordinal);
        Assert.Contains("cannot be edited", page.Find("[data-style-impact]").TextContent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncompleteImpactBlocksMutationEvenWhenCanEditIsTrue(bool v4)
    {
        using var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = v4 });
        context.Services.AddSingleton<IStudioMapStyleCatalogDataSource>(new Source { ImpactComplete = false });
        var page = context.Render<StudioStyleEditorPage>(parameters => parameters.Add(component => component.StyleId, "shared"));
        Assert.True(page.Find("[data-style-content]").HasAttribute("readonly"));
        Assert.True(page.Find("[data-style-save]").HasAttribute("disabled"));
        Assert.Single(page.FindAll("[data-style-impact-incomplete]"));
        Assert.Empty(page.FindAll("[data-style-consumers]"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangedAuthorityRetainsDirtyCandidateAndNeverWrites(bool v4)
    {
        using var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = v4 });
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.SetupModule("./_content/Honua.Console.Shell/unsaved-changes-guard.js").Mode = JSRuntimeMode.Loose;
        var source = new Source();
        context.Services.AddSingleton<IStudioMapStyleCatalogDataSource>(source);
        var page = context.Render<StudioStyleEditorPage>(parameters => parameters.Add(component => component.StyleId, "shared"));
        page.Find("[data-style-content]").Input("my-unsaved-style");
        source.AuthorityCurrent = false;
        page.Find("[data-style-save]").Click();
        page.WaitForAssertion(() => Assert.Contains("Connection changed", page.Find("[data-style-result]").TextContent, StringComparison.Ordinal));
        Assert.Equal(v4 ? "console-state" : "console-panel", page.Find("[data-style-result]").ClassList[0]);
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
        public Task<string?> CaptureAuthorityAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>("operator-binding");
        public Task<HonuaAdminEndpointResult<HonuaOgcStylesheet>> GetStylesheetBoundAsync(string styleId, HonuaOgcStyleEncoding encoding, string authorityBinding, CancellationToken cancellationToken = default) => GetStylesheetAsync(styleId, encoding, cancellationToken);
        public Task<bool> IsAuthorityCurrentAsync(string binding, CancellationToken cancellationToken = default) => Task.FromResult(binding == "operator-binding");
        public Task<HonuaAdminEndpointResult<HonuaSharedStyleImpact>> GetSharedStyleImpactBoundAsync(string styleId, string authorityBinding, CancellationToken cancellationToken = default) => GetSharedStyleImpactAsync(styleId, cancellationToken);
        public Task<HonuaOgcStyleSaveResult> SaveStylesheetAsync(string styleId, HonuaOgcStyleEncoding encoding, string content, bool strict, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    }

    private sealed class Source : IStudioMapStyleCatalogDataSource
    {
        public bool WriteSucceeds { get; init; }
        public bool FailReadback { get; set; }
        public bool Accepted { get; private set; }
        public int Writes { get; private set; }
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
            if (Accepted && FailReadback)
                return Task.FromResult(HonuaAdminEndpointResult<HonuaOgcStylesheet>.FromIssue(new("Unavailable", "GET /ogc/styles/{id}", "Temporarily unavailable")));
            return Task.FromResult(HonuaAdminEndpointResult<HonuaOgcStylesheet>.FromData(new(styleId, encoding, Accepted ? "{\"version\":8,\"name\":\"server-canonical\"}" : "{\"version\":8}") { EntityTag = Accepted ? "\"style-v8\"" : "\"style-v7\"", AuthorityBinding = "operator-binding" }));
        }
        public Task<HonuaAdminEndpointResult<HonuaSharedStyleImpact>> GetSharedStyleImpactAsync(string styleId, CancellationToken cancellationToken = default) =>
            Task.FromResult(HonuaAdminEndpointResult<HonuaSharedStyleImpact>.FromData(new(styleId, Accepted ? 8 : 7, Accepted ? "\"style-v8\"" : "\"style-v7\"", new string('a', 64), new string('b', 64), [new("resource", "Visible resource", null)], CanEdit) { ImpactComplete = ImpactComplete, Consumers = [new("map", "map-id", "map-package", new string('c', 64), "published", IsCurrent: true)] }));
        public Task<string?> CaptureAuthorityAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>("operator-binding");
        public Task<HonuaAdminEndpointResult<HonuaOgcStylesheet>> GetStylesheetBoundAsync(string styleId, HonuaOgcStyleEncoding encoding, string authorityBinding, CancellationToken cancellationToken = default) => GetStylesheetAsync(styleId, encoding, cancellationToken);
        public Task<bool> IsAuthorityCurrentAsync(string binding, CancellationToken cancellationToken = default) => Task.FromResult(AuthorityCurrent && binding == "operator-binding");
        public Task<HonuaAdminEndpointResult<HonuaSharedStyleImpact>> GetSharedStyleImpactBoundAsync(string styleId, string authorityBinding, CancellationToken cancellationToken = default) => GetSharedStyleImpactAsync(styleId, cancellationToken);
        public Task<HonuaOgcStyleSaveResult> SaveStylesheetAsync(string styleId, HonuaOgcStyleEncoding encoding, string content, bool strict, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The shared UI must not use the headerless legacy write.");
        public Task<HonuaOgcStyleSaveResult> SaveStylesheetBoundAsync(string styleId, HonuaOgcStyleEncoding encoding, string content, bool strict, string expectedEntityTag, string expectedImpactHash, string authorityBinding, CancellationToken cancellationToken = default) => SaveStylesheetConditionallyAsync(styleId, encoding, content, strict, expectedEntityTag, expectedImpactHash, cancellationToken);
        public Task<HonuaOgcStyleSaveResult> SaveStylesheetConditionallyAsync(string styleId, HonuaOgcStyleEncoding encoding, string content, bool strict, string expectedEntityTag, string expectedImpactHash, CancellationToken cancellationToken = default)
        {
            Writes++;
            SavedVersion = expectedEntityTag; SavedImpact = expectedImpactHash;
            if (WriteSucceeds)
            {
                Accepted = true;
                return Task.FromResult(HonuaOgcStyleSaveResult.Ok([]));
            }
            return Task.FromResult(HonuaOgcStyleSaveResult.Fail("Conflict", "Your edits are retained."));
        }
    }
}
