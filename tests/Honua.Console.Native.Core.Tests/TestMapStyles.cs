using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

internal sealed class TestMapStyles : IStudioMapStyleCatalogDataSource
{
    public const string Style = """
        {"version":8,"sources":{"observations":{"type":"geojson","data":{"type":"FeatureCollection","features":[]}}},
         "layers":[{"id":"observations","type":"circle","source":"observations","paint":{"circle-color":"#123456"}}]}
        """;

    public StudioMapStyleCatalog Catalog { get; init; } = StudioMapStyleCatalog.Empty;
    public string SelectedStyle { get; init; } = Style;
    public List<int> RequestedLayers { get; } = [];
    public Func<int, string> ForLayer { get; init; } = _ => Style;

    public static void MarkSaved(StudioMapEditorState state)
    {
        using var document = JsonDocument.Parse(Style);
        state.CanonicalMapStyle = document.RootElement.Clone();
        state.SavedStyleSignature = StudioMapStyleComposer.Signature(state);
    }

    public Task<HonuaAdminEndpointResult<HonuaOgcStylesheet>> GetLayerStylesheetAsync(int layerId, CancellationToken cancellationToken = default)
    {
        RequestedLayers.Add(layerId);
        return Task.FromResult(HonuaAdminEndpointResult<HonuaOgcStylesheet>.FromData(new HonuaOgcStylesheet(
            layerId.ToString(System.Globalization.CultureInfo.InvariantCulture), HonuaOgcStyleEncoding.MapLibre, ForLayer(layerId))));
    }

    public Task<HonuaAdminEndpointResult<HonuaOgcStylesheet>> GetStylesheetAsync(string styleId, HonuaOgcStyleEncoding encoding, CancellationToken cancellationToken = default) =>
        Task.FromResult(HonuaAdminEndpointResult<HonuaOgcStylesheet>.FromData(new HonuaOgcStylesheet(styleId, encoding, SelectedStyle)));

    public Task<StudioMapStyleCatalog> GetStyleCatalogAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Catalog);

    public Task<HonuaOgcStyleSaveResult> SaveStylesheetAsync(string styleId, HonuaOgcStyleEncoding encoding, string content, bool strict, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Read-only style fixture.");
}
