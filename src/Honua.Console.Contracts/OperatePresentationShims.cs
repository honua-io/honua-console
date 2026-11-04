using System.Net.Http.Json;
using System.Text.Json;

namespace Honua.Console.Contracts;

// Document is the server's raw template, and null means nothing is stored.
public sealed record HonuaAdminLayerAuthoringDocument
{
    public int LayerId { get; init; }

    public JsonElement? Document { get; init; }
}

public sealed partial class HonuaAdminOperateHttpClient
{
    public Task<HonuaAdminEndpointResult<HonuaAdminLayerAuthoringDocument>> GetLayerPopupInfoAsync(
        int layerId,
        CancellationToken cancellationToken = default) =>
        GetEnvelopeAsync<HonuaAdminLayerAuthoringDocument>(
            $"/api/v1/admin/metadata/layers/{layerId}/popup-info",
            "GET /api/v1/admin/metadata/layers/{layerId}/popup-info",
            cancellationToken);

    public Task<HonuaAdminEndpointResult<HonuaAdminLayerAuthoringDocument>> UpdateLayerPopupInfoAsync(
        int layerId,
        JsonElement? document,
        CancellationToken cancellationToken = default) =>
        SendEnvelopeAsync<HonuaAdminLayerAuthoringDocument>(
            () => JsonRequest(HttpMethod.Put, $"/api/v1/admin/metadata/layers/{layerId}/popup-info", document),
            "PUT /api/v1/admin/metadata/layers/{layerId}/popup-info",
            cancellationToken);

    public Task<HonuaAdminEndpointResult<HonuaAdminLayerAuthoringDocument>> GetLayerDrawingInfoAsync(
        int layerId,
        CancellationToken cancellationToken = default) =>
        GetEnvelopeAsync<HonuaAdminLayerAuthoringDocument>(
            $"/api/v1/admin/metadata/layers/{layerId}/drawing-info",
            "GET /api/v1/admin/metadata/layers/{layerId}/drawing-info",
            cancellationToken);

    public Task<HonuaAdminEndpointResult<HonuaAdminLayerAuthoringDocument>> UpdateLayerDrawingInfoAsync(
        int layerId,
        JsonElement? document,
        CancellationToken cancellationToken = default) =>
        SendEnvelopeAsync<HonuaAdminLayerAuthoringDocument>(
            () => JsonRequest(HttpMethod.Put, $"/api/v1/admin/metadata/layers/{layerId}/drawing-info", document),
            "PUT /api/v1/admin/metadata/layers/{layerId}/drawing-info",
            cancellationToken);
}
