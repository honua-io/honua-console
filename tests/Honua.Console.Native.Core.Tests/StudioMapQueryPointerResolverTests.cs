using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Validation;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Docker-free unit coverage for the Wave-2 JSON-Pointer → console field-key resolvers and the
/// GeoJSON/JsonPointer rule helpers they build on. Proves that a server validation diagnostic addressed by a
/// JSON Pointer (e.g. <c>/body/layers/2/sourceRef</c> or <c>/body/predicates/1/start</c>) is mapped onto the
/// same console field key the client validator uses, so a server finding surfaces inline next to the offending
/// layer/predicate.
/// </summary>
public sealed class StudioMapQueryPointerResolverTests
{
    [Theory]
    [InlineData("/body/title", "map.title")]
    [InlineData("/body/basemap", "map.basemap")]
    [InlineData("/body/initialExtent", "map.initialExtent")]
    [InlineData("/body/layers/2/sourceRef", "map.layer[2].sourceRef")]
    [InlineData("/layers/0/sourceRef", "map.layer[0].sourceRef")] // body-rooting optional
    [InlineData("/body/layers", "map.layers")]
    public void MapPointer_ResolvesToFieldKey(string pointer, string expected) =>
        Assert.Equal(expected, StudioMapPointerResolver.Resolve(pointer));

    [Fact]
    public void MapPointer_UnknownReturnsNull() =>
        Assert.Null(StudioMapPointerResolver.Resolve("/body/sharePolicy/tier"));

    [Theory]
    [InlineData("/body/serviceName", "query.serviceName")]
    [InlineData("/body/layerId", "query.layerId")]
    [InlineData("/body/outputSrid", "query.outputSrid")]
    [InlineData("/body/previewLimit", "query.previewLimit")]
    [InlineData("/body/predicates/1/start", "query.predicate[1].range")]
    [InlineData("/body/predicates/1/end", "query.predicate[1].range")]
    [InlineData("/body/predicates/3/distance", "query.predicate[3].distance")]
    [InlineData("/body/predicates/0/geometry", "query.predicate[0].geometry")]
    public void QueryPointer_ResolvesToFieldKey(string pointer, string expected) =>
        Assert.Equal(expected, StudioQueryPointerResolver.Resolve(pointer));

    [Fact]
    public void QueryPointer_UnknownReturnsNull() =>
        Assert.Null(StudioQueryPointerResolver.Resolve("/body/combinator"));

    [Fact]
    public void MapServerErrorBinder_MapsDiagnosticOntoLayerKey()
    {
        var diagnostics = new[]
        {
            new StudioValidationDiagnostic
            {
                Code = "studio.binding.ref.required",
                Severity = StudioPackageDiagnosticSeverity.Error,
                Path = "/body/layers/2/sourceRef",
                Message = "Layer 2 must bind a source.",
            },
        };

        var error = Assert.Single(StudioMapServerErrorBinder.Map(diagnostics));
        Assert.Equal(StudioMapFieldKeys.LayerSourceRef(2), error.FieldKey);
        Assert.Equal(ConsoleValidationSeverity.Error, error.Severity);
        Assert.Equal("/body/layers/2/sourceRef", error.Path);
    }

    [Fact]
    public void QueryServerErrorBinder_MapsDiagnosticOntoPredicateKey()
    {
        var diagnostics = new[]
        {
            new StudioValidationDiagnostic
            {
                Code = "query.predicate.temporal.order",
                Severity = StudioPackageDiagnosticSeverity.Blocker,
                Path = "/body/predicates/1/start",
                Message = "Start must be on or before End.",
            },
        };

        var error = Assert.Single(StudioQueryServerErrorBinder.Map(diagnostics));
        Assert.Equal(StudioQueryFieldKeys.PredicateRange(1), error.FieldKey);
        Assert.Equal(ConsoleValidationSeverity.Blocker, error.Severity);
    }

    [Fact]
    public void JsonPointer_SplitsAndUnescapes()
    {
        var segments = JsonPointer.Split("/body/layers/2/sourceRef");
        Assert.Equal(["body", "layers", "2", "sourceRef"], segments);

        // RFC-6901 escapes: ~1 -> /, ~0 -> ~.
        Assert.Equal(["a/b", "c~d"], JsonPointer.Split("/a~1b/c~0d"));

        Assert.Empty(JsonPointer.Split(null));
        Assert.Empty(JsonPointer.Split(""));
    }

    [Theory]
    [InlineData("{\"type\":\"Point\",\"coordinates\":[0,0]}", true)]
    [InlineData("{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[1,0],[1,1],[0,0]]]}", true)]
    [InlineData("{\"type\":\"GeometryCollection\",\"geometries\":[]}", true)]
    [InlineData("{\"type\":\"Point\"}", false)]
    [InlineData("{\"coordinates\":[0,0]}", false)]
    [InlineData("{\"type\":\"Banana\",\"coordinates\":[0,0]}", false)]
    [InlineData("not json", false)]
    [InlineData("", false)]
    public void GeoJsonRule_ValidatesGeometryShape(string value, bool expected) =>
        Assert.Equal(expected, GeoJsonRule.IsValidGeometry(value));
}
