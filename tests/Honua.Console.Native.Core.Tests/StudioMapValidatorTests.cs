using Honua.Console.Shell.Models;
using Honua.Console.Shell.Validation;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Docker-free unit coverage for <see cref="StudioMapValidator"/>, the Wave-2 client cross-field/bounds/format
/// validator for the Studio map builder. Each presence and the bbox parse/ordering rule from the catalog is
/// proven in its pass and fail state, keyed by <see cref="StudioMapFieldKeys"/>.
/// </summary>
public sealed class StudioMapValidatorTests
{
    private static IReadOnlyList<ConsoleFieldError> Evaluate(StudioMapEditorState state) =>
        StudioMapValidator.Instance.Evaluate(state);

    /// <summary>A fully valid baseline editor so a single failing rule can be asserted in isolation.</summary>
    private static StudioMapEditorState Valid()
    {
        var state = new StudioMapEditorState
        {
            Title = "Public works",
            Basemap = "basemap:streets",
            InitialExtent = "-158.3,21.2,-157.6,21.7",
        };
        state.Layers.Add(new StudioMapLayerEditor { SourceRef = "content:hydrants@v12", Title = "Hydrants" });
        return state;
    }

    [Fact]
    public void ValidMap_ProducesNoErrors() => Assert.Empty(Evaluate(Valid()));

    [Fact]
    public void MissingTitle_BlocksOnTitleKey()
    {
        var state = Valid();
        state.Title = "  ";

        var error = Assert.Single(Evaluate(state), e => e.FieldKey == StudioMapFieldKeys.Title);
        Assert.Equal(ConsoleValidationSeverity.Blocker, error.Severity);
    }

    [Fact]
    public void NoLayers_BlocksOnLayersKey()
    {
        var state = Valid();
        state.Layers.Clear();

        Assert.Contains(Evaluate(state), e =>
            e.FieldKey == StudioMapFieldKeys.Layers && e.Severity == ConsoleValidationSeverity.Blocker);
    }

    [Fact]
    public void LayerWithoutSourceRef_BlocksOnThatLayerKey()
    {
        var state = Valid();
        state.Layers.Add(new StudioMapLayerEditor { SourceRef = "  " });

        Assert.Contains(Evaluate(state), e => e.FieldKey == StudioMapFieldKeys.LayerSourceRef(1));
    }

    [Fact]
    public void MissingBasemap_BlocksOnBasemapKey()
    {
        var state = Valid();
        state.Basemap = string.Empty;

        Assert.Contains(Evaluate(state), e => e.FieldKey == StudioMapFieldKeys.Basemap);
    }

    [Fact]
    public void MissingInitialExtent_BlocksOnExtentKey()
    {
        var state = Valid();
        state.InitialExtent = string.Empty;

        var error = Assert.Single(Evaluate(state), e => e.FieldKey == StudioMapFieldKeys.InitialExtent);
        Assert.Equal("map.initialExtent.required", error.Code);
    }

    [Fact]
    public void OrderedBbox_Passes() => Assert.Empty(Evaluate(Valid()));

    [Fact]
    public void InvertedXBbox_FlagsOrderError()
    {
        var state = Valid();
        // minX (10) > maxX (1).
        state.InitialExtent = "10,1,1,5";

        var error = Assert.Single(Evaluate(state), e => e.FieldKey == StudioMapFieldKeys.InitialExtent);
        Assert.Equal("map.initialExtent.order", error.Code);
    }

    [Fact]
    public void InvertedYBbox_FlagsOrderError()
    {
        var state = Valid();
        // minY (9) > maxY (2).
        state.InitialExtent = "1,9,5,2";

        var error = Assert.Single(Evaluate(state), e => e.FieldKey == StudioMapFieldKeys.InitialExtent);
        Assert.Equal("map.initialExtent.order", error.Code);
    }

    [Theory]
    [InlineData("1,2,3")]
    [InlineData("1,2,3,4,5")]
    [InlineData("a,b,c,d")]
    [InlineData("1,2,three,4")]
    public void MalformedBbox_FlagsMalformed(string extent)
    {
        var state = Valid();
        state.InitialExtent = extent;

        var error = Assert.Single(Evaluate(state), e => e.FieldKey == StudioMapFieldKeys.InitialExtent);
        Assert.Equal("map.initialExtent.malformed", error.Code);
    }
}
