using Honua.Console.Shell.Models;
using Honua.Console.Shell.Validation;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Docker-free unit coverage for the Wave-6 Esri content-import intake validator and the new absolute-http
/// URL rule. Proves the catalog's intake rules in pass and fail: a source must be provided, pasted JSON must
/// parse to a JSON object, and a typed URL / item id must be an absolute http(s) URL or a plausible item id.
/// Keyed by <see cref="EsriImportFieldKeys"/>.
/// </summary>
public sealed class Wave6ValidatorTests
{
    private static EsriImportIntakeState PasteIntake(string? json) =>
        new(EsriContentKind.WebMap, EsriIntakeMode.Paste, PastedJson: json);

    private static EsriImportIntakeState UrlIntake(string? url) =>
        new(EsriContentKind.WebMap, EsriIntakeMode.Url, Url: url);

    private static IReadOnlyList<ConsoleFieldError> Evaluate(EsriImportIntakeState state) =>
        EsriImportValidator.Instance.Evaluate(state);

    // --- source presence ---

    [Fact]
    public void EmptyIntake_BlocksOnSource()
    {
        var error = Assert.Single(Evaluate(new EsriImportIntakeState(EsriContentKind.WebMap, EsriIntakeMode.Paste)));
        Assert.Equal(EsriImportFieldKeys.Source, error.FieldKey);
        Assert.Equal(ConsoleValidationSeverity.Blocker, error.Severity);
        Assert.Equal("esri.import.source.required", error.Code);
    }

    [Fact]
    public void ConnectedArcGisMode_CountsAsASource_NoSourceError()
    {
        var state = new EsriImportIntakeState(EsriContentKind.WebMap, EsriIntakeMode.ConnectedArcGis);
        Assert.DoesNotContain(Evaluate(state), e => e.FieldKey == EsriImportFieldKeys.Source);
    }

    [Fact]
    public void UploadFileName_CountsAsASource_NoSourceError()
    {
        var state = new EsriImportIntakeState(EsriContentKind.Dashboard, EsriIntakeMode.Upload, UploadFileName: "dash.json");
        Assert.Empty(Evaluate(state));
    }

    // --- JSON shape ---

    [Fact]
    public void PastedValidJsonObject_NoErrors() =>
        Assert.Empty(Evaluate(PasteIntake("""{ "operationalLayers": [] }""")));

    [Fact]
    public void PastedInvalidJson_ErrorsOnJson()
    {
        var error = Assert.Single(Evaluate(PasteIntake("{ not json")), e => e.FieldKey == EsriImportFieldKeys.Json);
        Assert.Equal("esri.import.json.invalid", error.Code);
        Assert.Equal(ConsoleValidationSeverity.Error, error.Severity);
    }

    [Fact]
    public void PastedJsonArray_ErrorsOnJson_NotAnObject()
    {
        // Valid JSON, but not the object shape every Esri export uses.
        var error = Assert.Single(Evaluate(PasteIntake("[1, 2, 3]")), e => e.FieldKey == EsriImportFieldKeys.Json);
        Assert.Equal("esri.import.json.invalid", error.Code);
        Assert.Contains("JSON object", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PastedJson_ToleratesTrailingCommas()
    {
        // The parser allows trailing commas; the validator's shape gate must match so a paste that the
        // parser would accept never trips the inline error first.
        Assert.Empty(Evaluate(PasteIntake("""{ "title": "x", }""")));
    }

    // --- URL / item id ---

    [Fact]
    public void UrlIntake_AbsoluteHttpsUrl_NoErrors() =>
        Assert.Empty(Evaluate(UrlIntake("https://org.maps.arcgis.com/home/item.html?id=abc")));

    [Fact]
    public void UrlIntake_AbsoluteHttpUrl_NoErrors() =>
        Assert.Empty(Evaluate(UrlIntake("http://gis.example.gov/rest")));

    [Fact]
    public void UrlIntake_BareItemId_NoErrors() =>
        Assert.Empty(Evaluate(UrlIntake("ab12cd34ef56")));

    [Fact]
    public void UrlIntake_GarbageWithSpaces_ErrorsOnUrl()
    {
        var error = Assert.Single(Evaluate(UrlIntake("not a url")), e => e.FieldKey == EsriImportFieldKeys.Url);
        Assert.Equal("esri.import.url.invalid", error.Code);
    }

    // --- UrlRule.IsAbsoluteHttp ---

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("http://example.com", true)]
    [InlineData("HTTP://EXAMPLE.COM", true)]
    [InlineData("ftp://example.com", false)]
    [InlineData("example.com", false)]
    [InlineData("/relative/path", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void UrlRule_IsAbsoluteHttp(string? value, bool expected) =>
        Assert.Equal(expected, UrlRule.IsAbsoluteHttp(value));
}
