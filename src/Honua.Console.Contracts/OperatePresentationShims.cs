using System.Net.Http.Json;
using System.Text.Json;

namespace Honua.Console.Contracts;

// SHIM(honua-sdk-dotnet#166): per-layer presentation authoring (popup-info + drawing-info / renderer)
// over the shipped honua-server admin authoring endpoints. Lives in the Console contracts boundary
// alongside the rest of the IHonuaAdminOperateClient surface (OperateAdminShims.cs) until honua-sdk-dotnet
// publishes a consumable stable Admin package. See SDK_SHIM_POLICY "Active Shims".
//
// Endpoints (AdminLayerAuthoringEndpoints.cs:38-41):
//   GET  /api/v1/admin/metadata/layers/{id}/popup-info   -> ApiResponse<{ layerId, document }>
//   PUT  /api/v1/admin/metadata/layers/{id}/popup-info   body: the raw document JSON object (or null to clear)
//   GET  /api/v1/admin/metadata/layers/{id}/drawing-info -> ApiResponse<{ layerId, document }>
//   PUT  /api/v1/admin/metadata/layers/{id}/drawing-info body: the raw document JSON object (or null to clear)
//
// The server stores each document verbatim in resource.Extensions and emits it back on the FeatureServer/
// MapServer layer metadata (popupInfo / drawingInfo). The console therefore passes the document through as
// raw JSON so the exact server shapes round-trip without lossy re-modeling (the live e2e sends a popup-info
// body of {title,fieldInfos:[...]} and a drawing-info body of {renderer:{type,field1,uniqueValueInfos:[...]}}).

/// <summary>
/// A layer's stored presentation document (popup-info OR drawing-info), echoed by the GET/PUT admin
/// authoring endpoints. <see cref="Document"/> is the raw GeoServices template object verbatim, or null
/// when nothing is authored / it was cleared.
/// </summary>
public sealed record HonuaAdminLayerAuthoringDocument
{
    public int LayerId { get; init; }

    public JsonElement? Document { get; init; }
}

public partial interface IHonuaAdminOperateClient
{
    /// <summary>
    /// Reads a layer's authored GeoServices popupInfo template
    /// (<c>GET /api/v1/admin/metadata/layers/{layerId}/popup-info</c>). The document is the raw stored
    /// template ({title, fieldInfos:[...]}) or null when none is authored.
    /// </summary>
    Task<HonuaAdminEndpointResult<HonuaAdminLayerAuthoringDocument>> GetLayerPopupInfoAsync(
        int layerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes (or clears, with a null document) a layer's GeoServices popupInfo template
    /// (<c>PUT /api/v1/admin/metadata/layers/{layerId}/popup-info</c>). The raw document object is sent as
    /// the request body verbatim so the exact server shape round-trips.
    /// </summary>
    Task<HonuaAdminEndpointResult<HonuaAdminLayerAuthoringDocument>> UpdateLayerPopupInfoAsync(
        int layerId,
        JsonElement? document,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a layer's authored drawingInfo renderer
    /// (<c>GET /api/v1/admin/metadata/layers/{layerId}/drawing-info</c>). The document is the raw stored
    /// template ({renderer:{...}}) or null when none is authored.
    /// </summary>
    Task<HonuaAdminEndpointResult<HonuaAdminLayerAuthoringDocument>> GetLayerDrawingInfoAsync(
        int layerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes (or clears, with a null document) a layer's drawingInfo renderer
    /// (<c>PUT /api/v1/admin/metadata/layers/{layerId}/drawing-info</c>). The raw document object is sent as
    /// the request body verbatim so the exact server shape round-trips.
    /// </summary>
    Task<HonuaAdminEndpointResult<HonuaAdminLayerAuthoringDocument>> UpdateLayerDrawingInfoAsync(
        int layerId,
        JsonElement? document,
        CancellationToken cancellationToken = default);
}

public sealed partial class HonuaAdminOperateHttpClient
{
    public Task<HonuaAdminEndpointResult<HonuaAdminLayerAuthoringDocument>> GetLayerPopupInfoAsync(
        int layerId,
        CancellationToken cancellationToken = default) =>
        GetAuthoringDocumentAsync(
            $"/api/v1/admin/metadata/layers/{layerId}/popup-info",
            "GET /api/v1/admin/metadata/layers/{layerId}/popup-info",
            cancellationToken);

    public Task<HonuaAdminEndpointResult<HonuaAdminLayerAuthoringDocument>> UpdateLayerPopupInfoAsync(
        int layerId,
        JsonElement? document,
        CancellationToken cancellationToken = default) =>
        PutAuthoringDocumentAsync(
            $"/api/v1/admin/metadata/layers/{layerId}/popup-info",
            document,
            "PUT /api/v1/admin/metadata/layers/{layerId}/popup-info",
            cancellationToken);

    public Task<HonuaAdminEndpointResult<HonuaAdminLayerAuthoringDocument>> GetLayerDrawingInfoAsync(
        int layerId,
        CancellationToken cancellationToken = default) =>
        GetAuthoringDocumentAsync(
            $"/api/v1/admin/metadata/layers/{layerId}/drawing-info",
            "GET /api/v1/admin/metadata/layers/{layerId}/drawing-info",
            cancellationToken);

    public Task<HonuaAdminEndpointResult<HonuaAdminLayerAuthoringDocument>> UpdateLayerDrawingInfoAsync(
        int layerId,
        JsonElement? document,
        CancellationToken cancellationToken = default) =>
        PutAuthoringDocumentAsync(
            $"/api/v1/admin/metadata/layers/{layerId}/drawing-info",
            document,
            "PUT /api/v1/admin/metadata/layers/{layerId}/drawing-info",
            cancellationToken);

    private Task<HonuaAdminEndpointResult<HonuaAdminLayerAuthoringDocument>> GetAuthoringDocumentAsync(
        string path,
        string contract,
        CancellationToken cancellationToken) =>
        GetApiResponseAsync<HonuaAdminLayerAuthoringDocument>(path, contract, cancellationToken);

    // The authoring PUT takes the raw document JSON object directly as the body (NOT wrapped); a null
    // document is sent as JSON null to clear the stored template. The response is the ApiResponse-enveloped
    // echo, so reuse the same send/envelope core as the other bodied admin writes.
    private Task<HonuaAdminEndpointResult<HonuaAdminLayerAuthoringDocument>> PutAuthoringDocumentAsync(
        string path,
        JsonElement? document,
        string contract,
        CancellationToken cancellationToken) =>
        SendApiResponseAsync<HonuaAdminLayerAuthoringDocument>(
            () => new HttpRequestMessage(HttpMethod.Put, path)
            {
                Content = JsonContent.Create(document, options: JsonOptions),
            },
            contract,
            cancellationToken);
}
