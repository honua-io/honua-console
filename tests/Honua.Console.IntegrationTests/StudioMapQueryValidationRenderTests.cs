using AngleSharp.Dom;
using Bunit;
using Bunit.TestDoubles;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Honua.Console.Shell.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for the Wave-2 validation wiring on the map (<c>/studio/map</c>) and query
/// (<c>/studio/query</c>) builder pages: inline client errors with <c>aria-invalid</c>, server diagnostics
/// binding onto their fields, server findings not gating re-save, and the unsaved-changes dirty guard
/// (editing marks dirty → navigation is guarded; a successful save clears it). The pages now host an
/// <c>&lt;UnsavedChangesGuard/&gt;</c> so the contexts run Loose JSInterop.
/// </summary>
public sealed class StudioMapQueryValidationRenderTests
{
    // --- Map builder ---

    [Fact]
    public void Map_InvertedBbox_ShowsInlineErrorAndAriaInvalid()
    {
        using var ctx = NewContext();
        var editor = MapEditor();
        editor.InitialExtent = "10,1,1,5"; // minX > maxX
        var data = new FakeMapDataSource { EditorLoad = new StudioMapEditorLoad(editor, []) };
        ctx.Services.AddSingleton<IStudioMapPackageDataSource>(data);

        var page = OpenMap(ctx, data);

        page.WaitForAssertion(
            () => Assert.Contains("Initial extent is inverted", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("console-validation-inline", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_BlockingClientError_DisablesSaveAndShowsSummary()
    {
        using var ctx = NewContext();
        var editor = MapEditor();
        editor.Title = string.Empty; // title is a blocking rule
        var data = new FakeMapDataSource { EditorLoad = new StudioMapEditorLoad(editor, []) };
        ctx.Services.AddSingleton<IStudioMapPackageDataSource>(data);

        var page = OpenMap(ctx, data);

        page.WaitForAssertion(
            () => Assert.Contains("data-validation-summary=\"blocking\"", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.True(FindButton(page, "Save draft").HasAttribute("disabled"));
    }

    [Fact]
    public void Map_PresentButInvalidExtent_GatesPublishConsistentlyWithSave()
    {
        using var ctx = NewContext();
        var editor = MapEditor();
        // Present (passes the presence-only pre-publish gate) but inverted, so the client validator blocks.
        editor.InitialExtent = "10,1,1,5";
        var data = new FakeMapDataSource { EditorLoad = new StudioMapEditorLoad(editor, []) };
        ctx.Services.AddSingleton<IStudioMapPackageDataSource>(data);

        var page = OpenMap(ctx, data);

        page.WaitForAssertion(
            () => Assert.Contains("Initial extent is inverted", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        // Both Save AND Publish are gated; the header badge does not claim validation passed.
        Assert.True(FindButton(page, "Save draft").HasAttribute("disabled"));
        Assert.True(FindButton(page, "Publish").HasAttribute("disabled"));
        Assert.DoesNotContain("validation passed", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_ServerDiagnostic_SurfacesOnItsLayerInline_AndDoesNotGateSave()
    {
        using var ctx = NewContext();
        var data = new FakeMapDataSource
        {
            EditorLoad = new StudioMapEditorLoad(MapEditor(), []),
            // A save rejection carries a server diagnostic bound onto the layer's source-ref key.
            OnSave = _ => new StudioMapCommandResult(
                false,
                "Rejected.",
                FieldErrors:
                [
                    new ConsoleFieldError(
                        StudioMapFieldKeys.LayerSourceRef(0),
                        "studio.binding.ref.required",
                        ConsoleValidationSeverity.Blocker,
                        "Server says: bind a source.",
                        "/body/layers/0/sourceRef"),
                ]),
        };
        ctx.Services.AddSingleton<IStudioMapPackageDataSource>(data);

        var page = OpenMap(ctx, data);

        // Save is enabled (no client blocker) — server findings never gate re-save.
        Assert.False(FindButton(page, "Save draft").HasAttribute("disabled"));
        FindButton(page, "Save draft").Click();

        page.WaitForAssertion(
            () => Assert.Contains("Server says: bind a source.", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("console-field-state__error", page.Markup, StringComparison.Ordinal);
        // Save stays enabled after the server finding lands.
        Assert.False(FindButton(page, "Save draft").HasAttribute("disabled"));
    }

    [Fact]
    public void Map_Editing_MarksDirty_GuardsNavigation_AndSaveClearsIt()
    {
        using var ctx = NewContext();
        var data = new FakeMapDataSource { EditorLoad = new StudioMapEditorLoad(MapEditor(), []) };
        ctx.Services.AddSingleton<IStudioMapPackageDataSource>(data);
        ctx.JSInterop.Setup<bool>("confirm", _ => true).SetResult(false);

        var page = OpenMap(ctx, data);
        var nav = ctx.Services.GetRequiredService<FakeNavigationManager>();

        nav.NavigateTo("catalog");
        Assert.EndsWith("catalog", nav.Uri, StringComparison.Ordinal);
        nav.NavigateTo("studio/map");

        // Edit the title input -> dirty.
        page.Find("input[aria-label='Map title']").Change("Public works v2");

        nav.NavigateTo("catalog");
        page.WaitForAssertion(
            () => Assert.EndsWith("studio/map", nav.Uri, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        FindButton(page, "Save draft").Click();
        page.WaitForAssertion(
            () =>
            {
                nav.NavigateTo("catalog");
                Assert.EndsWith("catalog", nav.Uri, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
    }

    // --- Query builder ---

    [Fact]
    public void Query_InvalidGeoJson_ShowsInlineError()
    {
        using var ctx = NewContext();
        var query = QueryEditor();
        query.Predicates.Add(new StudioQueryPredicateEditor
        {
            Kind = StudioQueryPredicateKinds.Spatial,
            Operator = "intersects",
            Geometry = "{not json}",
        });
        var data = new FakeQueryDataSource { EditorLoad = new StudioQueryEditorLoad(query, []) };
        ctx.Services.AddSingleton<IStudioQueryPackageDataSource>(data);

        var page = OpenQuery(ctx, data);

        page.WaitForAssertion(
            () => Assert.Contains("Geometry must be a valid GeoJSON geometry", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("console-validation-inline", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Query_MissingServiceName_DisablesSaveAndShowsSummary()
    {
        using var ctx = NewContext();
        var query = QueryEditor();
        query.ServiceName = string.Empty;
        var data = new FakeQueryDataSource { EditorLoad = new StudioQueryEditorLoad(query, []) };
        ctx.Services.AddSingleton<IStudioQueryPackageDataSource>(data);

        var page = OpenQuery(ctx, data);

        page.WaitForAssertion(
            () => Assert.Contains("data-validation-summary=\"blocking\"", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.True(FindButton(page, "Save query").HasAttribute("disabled"));
    }

    [Fact]
    public void Query_ServiceNameRow_ShowsAriaInvalid()
    {
        using var ctx = NewContext();
        var query = QueryEditor();
        query.ServiceName = string.Empty;
        var data = new FakeQueryDataSource { EditorLoad = new StudioQueryEditorLoad(query, []) };
        ctx.Services.AddSingleton<IStudioQueryPackageDataSource>(data);

        var page = OpenQuery(ctx, data);

        page.WaitForAssertion(
            () => Assert.Contains("aria-invalid=\"true\"", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("console-field-state--invalid", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Query_Editing_MarksDirty_GuardsNavigation_AndSaveClearsIt()
    {
        using var ctx = NewContext();
        var data = new FakeQueryDataSource { EditorLoad = new StudioQueryEditorLoad(QueryEditor(), []) };
        ctx.Services.AddSingleton<IStudioQueryPackageDataSource>(data);
        ctx.JSInterop.Setup<bool>("confirm", _ => true).SetResult(false);

        var page = OpenQuery(ctx, data);
        var nav = ctx.Services.GetRequiredService<FakeNavigationManager>();

        nav.NavigateTo("catalog");
        Assert.EndsWith("catalog", nav.Uri, StringComparison.Ordinal);
        nav.NavigateTo("studio/query");

        // Edit the service-name input -> dirty.
        page.Find("input[placeholder='permits']").Change("permits-v2");

        nav.NavigateTo("catalog");
        page.WaitForAssertion(
            () => Assert.EndsWith("studio/query", nav.Uri, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        FindButton(page, "Save query").Click();
        page.WaitForAssertion(
            () =>
            {
                nav.NavigateTo("catalog");
                Assert.EndsWith("catalog", nav.Uri, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
    }

    private static Bunit.TestContext NewContext()
    {
        var ctx = new Bunit.TestContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        // The map builder injects the style-picker catalog source (#161). Register the unsupported (no-server)
        // implementation so the inspector degrades to the legacy free-form style input under render tests.
        ctx.Services.AddSingleton<IStudioMapStyleCatalogDataSource, UnsupportedStudioMapStyleCatalogDataSource>();
        return ctx;
    }

    private static IRenderedComponent<StudioMapBuilderPage> OpenMap(Bunit.TestContext ctx, FakeMapDataSource data)
    {
        var page = ctx.RenderComponent<StudioMapBuilderPage>();
        page.WaitForAssertion(() => FindButtonGeneric(page, FakeMapDataSource.OpenLabel), TimeSpan.FromSeconds(5));
        FindButton(page, FakeMapDataSource.OpenLabel).Click();
        page.WaitForAssertion(
            () => Assert.Contains("data-map-builder", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        return page;
    }

    private static IRenderedComponent<StudioQueryBuilderPage> OpenQuery(Bunit.TestContext ctx, FakeQueryDataSource data)
    {
        var page = ctx.RenderComponent<StudioQueryBuilderPage>();
        page.WaitForAssertion(() => FindButtonGeneric(page, FakeQueryDataSource.OpenLabel), TimeSpan.FromSeconds(5));
        FindButton(page, FakeQueryDataSource.OpenLabel).Click();
        page.WaitForAssertion(
            () => Assert.Contains("data-query-editor", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        return page;
    }

    private static IElement FindButton<T>(IRenderedComponent<T> page, string label)
        where T : Microsoft.AspNetCore.Components.IComponent =>
        page.FindAll("button").First(button => button.TextContent.Contains(label, StringComparison.Ordinal));

    private static IElement FindButtonGeneric<T>(IRenderedComponent<T> page, string label)
        where T : Microsoft.AspNetCore.Components.IComponent =>
        FindButton(page, label);

    private static StudioMapEditorState MapEditor()
    {
        var state = new StudioMapEditorState
        {
            MapId = "map-1",
            Version = 3,
            Title = "Public works",
            Basemap = "basemap:streets",
            InitialExtent = "-158.3,21.2,-157.6,21.7",
        };
        state.Layers.Add(new StudioMapLayerEditor { SourceRef = "content:hydrants@v12", Title = "Hydrants" });
        return state;
    }

    private static StudioQueryEditor QueryEditor() => new()
    {
        QueryId = "query-1",
        Version = 2,
        Title = "Flood-zone permits",
        ServiceName = "permits",
        LayerId = 0,
        PreviewLimit = 25,
    };

    private sealed class FakeMapDataSource : IStudioMapPackageDataSource
    {
        public const string OpenLabel = "Open map";

        public StudioMapEditorLoad EditorLoad { get; set; } = new(null, []);

        public Func<StudioMapEditorState, StudioMapCommandResult>? OnSave { get; set; }

        public Task<StudioMapWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default)
        {
            var state = EditorLoad.State;
            var packages = state is null
                ? Array.Empty<StudioMapPackageListItem>()
                : new[]
                {
                    new StudioMapPackageListItem(state.MapId ?? "map-1", OpenLabel, state.Layers.Count, state.Version, null, DateTimeOffset.UtcNow),
                };
            return Task.FromResult(new StudioMapWorkspace(packages, []));
        }

        public Task<StudioMapEditorLoad> LoadAsync(string? mapId, CancellationToken cancellationToken = default) =>
            Task.FromResult(EditorLoad);

        public Task<StudioMapCommandResult> SaveDraftAsync(StudioMapEditorState state, CancellationToken cancellationToken = default) =>
            Task.FromResult(OnSave?.Invoke(state) ?? new StudioMapCommandResult(true, "Saved.", state));

        public Task<StudioMapCommandResult> PublishAsync(StudioMapEditorState state, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioMapCommandResult(true, "Published.", state));

        public Task<StudioMapCommandResult> ReopenAsync(StudioMapEditorState state, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioMapCommandResult(true, "Reopened.", state));
    }

    private sealed class FakeQueryDataSource : IStudioQueryPackageDataSource
    {
        public const string OpenLabel = "Open query";

        public StudioQueryEditorLoad EditorLoad { get; set; } = new(null, []);

        public Task<StudioQueryWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default)
        {
            var query = EditorLoad.Query;
            var packages = query is null
                ? Array.Empty<StudioQueryPackageListItem>()
                : new[]
                {
                    new StudioQueryPackageListItem(query.QueryId ?? "query-1", OpenLabel, query.ServiceName, query.Version, null, DateTimeOffset.UtcNow),
                };
            return Task.FromResult(new StudioQueryWorkspace(packages, []));
        }

        public Task<StudioQueryEditorLoad> LoadAsync(string? queryId, CancellationToken cancellationToken = default) =>
            Task.FromResult(EditorLoad);

        public Task<StudioQueryCommandResult> SaveAsync(StudioQueryEditor query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioQueryCommandResult(true, "Saved.", query));

        public Task<StudioQueryCommandResult> PreviewAsync(StudioQueryEditor query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioQueryCommandResult(true, "Previewed.", query));
    }
}
