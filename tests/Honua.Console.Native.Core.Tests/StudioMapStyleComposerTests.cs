using System.Text.Json;
using System.Text.Json.Nodes;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

public sealed class StudioMapStyleComposerTests
{
    [Fact]
    public async Task Compose_UsesFetchedStylesAndPreservesBackground_RemovalAndVisibilityAreApplied()
    {
        using var original = JsonDocument.Parse("""
            {"version":8,"center":[1,2],"metadata":{"owner":"retained"},
             "sources":{"basemap":{"type":"raster","tiles":["https://tiles.example/base/{z}/{x}/{y}"]}},
             "layers":[{"id":"basemap","type":"raster","source":"basemap"}]}
            """);
        var state = new StudioMapEditorState { Basemap = "server-default", CanonicalMapStyle = original.RootElement.Clone() };
        state.Layers.Add(Layer(1, visible: false));
        state.Layers.Add(Layer(2, visible: true));
        var styles = new TestMapStyles();
        var result = await StudioMapStyleComposer.ComposeAsync(state, styles, CancellationToken.None);
        Assert.Null(result.Issue);
        var composed = result.Style!.Value;
        Assert.Equal(new[] { 2, 1 }, styles.RequestedLayers);
        Assert.Equal("retained", composed.GetProperty("metadata").GetProperty("owner").GetString());
        Assert.Equal(3, composed.GetProperty("sources").EnumerateObject().Count());
        var layers = composed.GetProperty("layers");
        Assert.Equal("basemap", layers[0].GetProperty("id").GetString());
        Assert.Equal("console-0-observations", layers[1].GetProperty("source").GetString());
        Assert.Equal("visible", layers[1].GetProperty("layout").GetProperty("visibility").GetString());
        Assert.Equal("none", layers[2].GetProperty("layout").GetProperty("visibility").GetString());
        Assert.Equal("#123456", layers[2].GetProperty("paint").GetProperty("circle-color").GetString());
        state.CanonicalMapStyle = composed;
        var repeated = await StudioMapStyleComposer.ComposeAsync(state, styles, CancellationToken.None);
        Assert.Null(repeated.Issue);
        foreach (var member in new[] { "sources", "layers" })
        {
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(composed.GetProperty(member).GetRawText()),
                JsonNode.Parse(repeated.Style!.Value.GetProperty(member).GetRawText())));
        }
        Assert.Equal(new[] { 2, 1 }, styles.RequestedLayers); // Real saved styles are retained, not replaced by another default fetch.
        state.Layers[0].Visible = true;
        var shown = await StudioMapStyleComposer.ComposeAsync(state, styles, CancellationToken.None);
        Assert.Null(shown.Issue);
        Assert.Equal("visible", shown.Style!.Value.GetProperty("layers")[2].GetProperty("layout").GetProperty("visibility").GetString());
        state.Layers.RemoveAt(0);
        var afterRemoval = await StudioMapStyleComposer.ComposeAsync(state, styles, CancellationToken.None);
        Assert.Null(afterRemoval.Issue);
        Assert.Equal(2, afterRemoval.Style!.Value.GetProperty("sources").EnumerateObject().Count());
        Assert.Equal(2, afterRemoval.Style.Value.GetProperty("layers").GetArrayLength());
    }

    [Fact]
    public async Task Compose_AdvertisedSelectedStyleUsesTargetDataAndSelectedPaint()
    {
        var state = new StudioMapEditorState { Basemap = "server-default" };
        var layer = Layer(7, true);
        layer.Style = "selected";
        state.Layers.Add(layer);
        var selected = JsonNode.Parse(TestMapStyles.Style)!;
        selected["sources"]!["observations"]!["data"] = "https://foreign.example/never-use-this-source.geojson";
        selected["layers"]![0]!["paint"]!["circle-color"] = "#abcdef";
        selected["layers"]![0]!["source-layer"] = "foreign-vector-layer";
        var styles = new TestMapStyles
        {
            Catalog = new StudioMapStyleCatalog([new StudioMapStyleOption("selected", "Selected style")], "selected", null),
            SelectedStyle = selected.ToJsonString()
        };
        var composed = await StudioMapStyleComposer.ComposeAsync(state, styles, CancellationToken.None);
        Assert.Null(composed.Issue);
        var body = composed.Style!.Value;
        Assert.Equal(JsonValueKind.Object, body.GetProperty("sources").EnumerateObject().Single().Value.GetProperty("data").ValueKind);
        Assert.Equal("#abcdef", body.GetProperty("layers")[0].GetProperty("paint").GetProperty("circle-color").GetString());
        Assert.False(body.GetProperty("layers")[0].TryGetProperty("source-layer", out _));
        Assert.DoesNotContain("foreign.example", body.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compose_OneStyleWithoutSpriteCanShareTheOtherStylesSprite()
    {
        var state = new StudioMapEditorState { Basemap = "server-default" };
        state.Layers.Add(Layer(1, true));
        state.Layers.Add(Layer(2, true));
        var styles = new TestMapStyles
        {
            ForLayer = id =>
            {
                var style = JsonNode.Parse(TestMapStyles.Style)!;
                if (id == 1) style["sprite"] = "https://assets.example/sprite";
                return style.ToJsonString();
            }
        };
        var composed = await StudioMapStyleComposer.ComposeAsync(state, styles, CancellationToken.None);
        Assert.Null(composed.Issue);
        Assert.Equal("https://assets.example/sprite", composed.Style!.Value.GetProperty("sprite").GetString());
    }

    [Theory]
    [InlineData("ref")]
    [InlineData("terrain")]
    [InlineData("sprite")]
    [InlineData("glyphs")]
    public async Task Compose_UnsupportedDependenciesAndConflictingResourcesAreExplicit(string unsupported)
    {
        var state = new StudioMapEditorState { Basemap = "server-default" };
        state.Layers.Add(Layer(1, true));
        state.Layers.Add(Layer(2, true));
        var styles = new TestMapStyles
        {
            ForLayer = layerId =>
            {
                var value = JsonNode.Parse(TestMapStyles.Style)!;
                if (unsupported == "ref") value["layers"]![0]!["ref"] = "other-layer";
                else if (unsupported == "terrain") value["terrain"] = new JsonObject { ["source"] = "elevation" };
                else value[unsupported] = $"https://assets.example/{layerId}";
                return value.ToJsonString();
            }
        };
        var result = await StudioMapStyleComposer.ComposeAsync(state, styles, CancellationToken.None);
        Assert.Null(result.Style);
        Assert.NotNull(result.Issue);
    }

    [Fact]
    public async Task Compose_UnsupportedFilterOrBasemapNeverPublishesStaleStyle()
    {
        var state = new StudioMapEditorState { Basemap = "basemap:streets" };
        state.Layers.Add(Layer(1, true));
        var styles = new TestMapStyles();
        Assert.NotNull((await StudioMapStyleComposer.ComposeAsync(state, styles, CancellationToken.None)).Issue);
        state.Basemap = "server-default";
        state.Layers[0].Filter = "status = 'active'";
        Assert.NotNull((await StudioMapStyleComposer.ComposeAsync(state, styles, CancellationToken.None)).Issue);
        Assert.Empty(styles.RequestedLayers);
    }

    private static StudioMapLayerEditor Layer(int id, bool visible) => new()
    {
        SourceRef = $"service:observations/{id}",
        BoundServiceId = "observations",
        BoundLayerId = id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Visible = visible
    };
}
