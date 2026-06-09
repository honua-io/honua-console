using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for the layer-metadata authoring page (<c>/operate/layers/{id}/metadata</c>):
/// the three sections (Display / Editing / CRS) each GET-load their part and PUT-save it. Drives the page
/// through a fake operation (never a mock server). The merged-build Unsupported* source is exercised through
/// real DI to prove the honest missing-binding state across all three sections.
/// </summary>
public sealed class LayerMetadataPageRenderTests
{
    private const string ResourceId = "conn-1-layer-1";

    [Fact]
    public void Page_WhenBound_RendersDisplayEditingSpatialFromGet()
    {
        var fake = new FakeMetadata
        {
            Display = new ConsoleLayerDisplay { Bound = true, LayerId = 1, DisplayField = "name", MinScale = 100000 },
            Editing = new ConsoleLayerEditing { Bound = true, LayerId = 1, GlobalIdField = "globalid", CanModify = true },
            Spatial = new ConsoleLayerSpatial
            {
                Bound = true,
                LayerId = 1,
                Srid = 4326,
                GeometryType = "polygon",
                SupportedCrs = ["http://www.opengis.net/def/crs/EPSG/0/4326"],
                StorageCrs = "http://www.opengis.net/def/crs/EPSG/0/4326",
            },
        };
        var page = RenderPage(fake);

        page.WaitForAssertion(
            () => Assert.Contains("data-display-save", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        // Display field, editing global-id field, and the supported-CRS value all surfaced from the GET load.
        Assert.Contains("value=\"name\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("value=\"globalid\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-editing-save", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-spatial-save", page.Markup, StringComparison.Ordinal);
        Assert.Contains("EPSG/0/4326", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Page_SaveDisplay_IssuesSetDisplayAndSurfacesResult()
    {
        var fake = new FakeMetadata
        {
            Display = new ConsoleLayerDisplay { Bound = true, LayerId = 1, DisplayField = "name" },
            Editing = new ConsoleLayerEditing { Bound = true, LayerId = 1 },
            Spatial = new ConsoleLayerSpatial { Bound = true, LayerId = 1 },
            DisplaySaveResult = new ConsoleSetLayerMetadataResult { Succeeded = true, State = "Updated", Detail = "Saved the layer's display hints on honua-server." },
        };
        var page = RenderPage(fake);

        page.WaitForAssertion(
            () => Assert.Contains("data-display-save", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        page.Find("[data-display-save]").Click();

        page.WaitForAssertion(
            () => Assert.Contains("data-display-result", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.True(fake.DisplaySaved);
        Assert.Contains("display hints", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Page_SaveEditing_IssuesSetEditingAndSurfacesResult()
    {
        var fake = new FakeMetadata
        {
            Display = new ConsoleLayerDisplay { Bound = true, LayerId = 1 },
            Editing = new ConsoleLayerEditing { Bound = true, LayerId = 1, GlobalIdField = "globalid" },
            Spatial = new ConsoleLayerSpatial { Bound = true, LayerId = 1 },
            EditingSaveResult = new ConsoleSetLayerMetadataResult { Succeeded = true, State = "Updated", Detail = "Saved the layer's editing metadata on honua-server." },
        };
        var page = RenderPage(fake);

        page.WaitForAssertion(
            () => Assert.Contains("data-editing-save", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        page.Find("[data-editing-save]").Click();

        page.WaitForAssertion(
            () => Assert.Contains("data-editing-result", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.True(fake.EditingSaved);
        Assert.Contains("editing metadata", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Page_SaveSpatial_IssuesSetSpatialWithParsedCrsList()
    {
        var fake = new FakeMetadata
        {
            Display = new ConsoleLayerDisplay { Bound = true, LayerId = 1 },
            Editing = new ConsoleLayerEditing { Bound = true, LayerId = 1 },
            Spatial = new ConsoleLayerSpatial
            {
                Bound = true,
                LayerId = 1,
                SupportedCrs = ["http://www.opengis.net/def/crs/EPSG/0/4326"],
            },
            SpatialSaveResult = new ConsoleSetLayerMetadataResult { Succeeded = true, State = "Updated", Detail = "Saved the layer's CRS / spatial metadata on honua-server." },
        };
        var page = RenderPage(fake);

        page.WaitForAssertion(
            () => Assert.Contains("data-spatial-save", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        page.Find("[data-spatial-save]").Click();

        page.WaitForAssertion(
            () => Assert.Contains("data-spatial-result", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.NotNull(fake.SpatialSavedCrs);
        Assert.Single(fake.SpatialSavedCrs!);
        Assert.Contains("CRS / spatial metadata", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Page_MergedBuild_RendersMissingBindingThroughRealDi()
    {
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new FakeTransition());
        ctx.Services.AddSingleton<IConsoleLayerMetadataOperation, UnsupportedConsoleLayerMetadataOperation>();

        var page = ctx.RenderComponent<OperateLayerMetadataPage>(p => p.Add(x => x.ResourceId, ResourceId));

        page.WaitForAssertion(
            () => Assert.Contains("data-display-unbound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("data-editing-unbound", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-spatial-unbound", page.Markup, StringComparison.Ordinal);
        Assert.Contains("HONUA_SERVER_BASE_URL", page.Markup, StringComparison.Ordinal);
    }

    private static IRenderedComponent<OperateLayerMetadataPage> RenderPage(FakeMetadata metadata)
    {
        var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new FakeTransition());
        ctx.Services.AddSingleton<IConsoleLayerMetadataOperation>(metadata);
        return ctx.RenderComponent<OperateLayerMetadataPage>(p => p.Add(x => x.ResourceId, ResourceId));
    }

    private sealed class FakeTransition : IOperateTransitionDataSource
    {
        public Task<OperateTransitionWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new OperateTransitionWorkspace(
                [],
                [],
                [
                    new OperateServiceDetail(
                        "svc", "Service", "FeatureServer", "running", "server",
                        [new OperateServiceLayerProjection(1, "Parcels", "polygon", ResourceId, "parcels")],
                        [],
                        [])
                ],
                [],
                []));

        public Task<OperateConnectionSummary?> FindConnectionAsync(string connectionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateConnectionSummary?>(null);

        public Task<OperateResourceEditPreview?> FindResourceEditAsync(string resourceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateResourceEditPreview?>(null);

        public Task<OperateServiceDetail?> FindServiceAsync(string serviceName, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateServiceDetail?>(null);
    }

    private sealed class FakeMetadata : IConsoleLayerMetadataOperation
    {
        public ConsoleLayerDisplay Display { get; set; } = ConsoleLayerDisplay.Unbound("test");
        public ConsoleLayerEditing Editing { get; set; } = ConsoleLayerEditing.Unbound("test");
        public ConsoleLayerSpatial Spatial { get; set; } = ConsoleLayerSpatial.Unbound("test");

        public ConsoleSetLayerMetadataResult? DisplaySaveResult { get; set; }
        public ConsoleSetLayerMetadataResult? EditingSaveResult { get; set; }
        public ConsoleSetLayerMetadataResult? SpatialSaveResult { get; set; }

        public bool DisplaySaved { get; private set; }
        public bool EditingSaved { get; private set; }
        public IReadOnlyList<string>? SpatialSavedCrs { get; private set; }
        public bool SpatialSaveInvoked { get; private set; }

        public Task<ConsoleLayerDisplay> GetDisplayAsync(int layerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Display);

        public Task<ConsoleSetLayerMetadataResult> SetDisplayAsync(int layerId, ConsoleLayerDisplay display, CancellationToken cancellationToken = default)
        {
            DisplaySaved = true;
            return Task.FromResult(DisplaySaveResult ?? new ConsoleSetLayerMetadataResult { Succeeded = true, State = "Updated" });
        }

        public Task<ConsoleLayerEditing> GetEditingAsync(int layerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Editing);

        public Task<ConsoleSetLayerMetadataResult> SetEditingAsync(int layerId, ConsoleLayerEditing editing, CancellationToken cancellationToken = default)
        {
            EditingSaved = true;
            return Task.FromResult(EditingSaveResult ?? new ConsoleSetLayerMetadataResult { Succeeded = true, State = "Updated" });
        }

        public Task<ConsoleLayerSpatial> GetSpatialAsync(int layerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Spatial);

        public Task<ConsoleSetLayerMetadataResult> SetSpatialAsync(
            int layerId,
            IReadOnlyList<string>? supportedCrs,
            string? storageCrs,
            double? storageCrsCoordinateEpoch,
            bool clearStorageCrs,
            bool clearStorageCrsCoordinateEpoch,
            CancellationToken cancellationToken = default)
        {
            SpatialSaveInvoked = true;
            SpatialSavedCrs = supportedCrs;
            return Task.FromResult(SpatialSaveResult ?? new ConsoleSetLayerMetadataResult { Succeeded = true, State = "Updated" });
        }
    }
}
