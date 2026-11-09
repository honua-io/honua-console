using Honua.Sdk.Admin.Models;
using System.Text.Json;

namespace Honua.Console.Contracts;

public interface IHonuaAdminConnectionsClient
{
    Task<HonuaAdminEndpointResult<JsonElement>> GetConnectionProviderManifestAsync(
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaAdminConnectionSummary[]>> ListConnectionsAsync(
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaAdminConnectionSummary>> CreateConnectionAsync(
        HonuaAdminCreateConnectionRequest request,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaAdminConnectionTestResult>> TestDraftConnectionAsync(
        HonuaAdminCreateConnectionRequest request,
        CancellationToken cancellationToken = default);

    // Unlike a draft test, the server stores the resulting health on the connection.
    Task<HonuaAdminEndpointResult<HonuaAdminConnectionTestResult>> TestConnectionAsync(
        string connectionId,
        CancellationToken cancellationToken = default);

    // The wire body is { "tables": [...] }, not the admin response envelope.
    Task<HonuaAdminEndpointResult<HonuaAdminTableInfo[]>> ListConnectionTablesAsync(
        string connectionId,
        CancellationToken cancellationToken = default);
}

public interface IHonuaAdminImportClient
{
    // The body is the format list itself, not the admin response envelope.
    Task<HonuaAdminEndpointResult<HonuaAdminImportFormats>> GetImportFormatsAsync(
        CancellationToken cancellationToken = default);

    // The server answers 200 even when the import failed; read Success on the body.
    Task<HonuaAdminEndpointResult<HonuaAdminImportResult>> ImportFileAsync(
        byte[] fileContent,
        string fileName,
        string tableName,
        string? targetSchema,
        CancellationToken cancellationToken = default);

    // The server accepts only an https URL, and the body is not wrapped in the admin envelope.
    Task<HonuaAdminEndpointResult<HonuaAdminExternalServiceDiscovery>> DiscoverExternalServiceAsync(
        string url,
        HonuaAdminExternalServiceCredentials? credentials = null,
        CancellationToken cancellationToken = default);

    // The server answers 202; poll the returned job.
    Task<HonuaAdminEndpointResult<HonuaAdminGeoservicesImportJob>> StartGeoservicesImportAsync(
        HonuaAdminGeoservicesImportRequest request,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaAdminGeoservicesImportProgress>> GetGeoservicesImportJobAsync(
        string jobId,
        CancellationToken cancellationToken = default);
}

public interface IHonuaAdminLayerMetadataClient
{
    Task<HonuaAdminEndpointResult<HonuaAdminLayerFields>> GetLayerFieldsAsync(
        int layerId,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaAdminLayerFields>> UpdateLayerFieldsAsync(
        int layerId,
        HonuaAdminLayerFieldsUpdate request,
        CancellationToken cancellationToken = default);

}

public interface IHonuaAdminLayerPublishingClient
{
    Task<HonuaAdminEndpointResult<HonuaAdminPublishedLayerSummary>> PublishLayerBoundAsync(
        string connectionId, PublishLayerRequest request, string authorityBinding, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Authority-bound publishing is unavailable.");
    Task<HonuaAdminEndpointResult<HonuaAdminLayerPage>> ListConnectionLayersAsync(
        string connectionId,
        HonuaAdminLayerListRequest request,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaAdminPublishedLayerSummary>> PublishLayerAsync(
        string connectionId,
        PublishLayerRequest request,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaAdminPublishedLayerSummary>> SetLayerEnabledAsync(
        string connectionId,
        int layerId,
        bool enabled,
        string? serviceName = null,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaAdminServicePage>> ListServicesAsync(
        HonuaAdminCatalogQuery query,
        CancellationToken cancellationToken = default);
}

public interface IHonuaAdminServiceSettingsClient
{
    Task<HonuaAdminEndpointResult<HonuaAdminServiceSettingsResponse>> GetServiceSettingsAsync(
        string serviceName,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaAdminServiceSettingsResponse>> UpdateServiceProtocolsAsync(
        string serviceName,
        IReadOnlyList<string> enabledProtocols,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaAdminServiceSettingsResponse>> UpdateServiceAccessPolicyAsync(
        string serviceName,
        HonuaAdminUpdateAccessPolicyRequest request,
        CancellationToken cancellationToken = default);

    // This write can answer 501 when the server has no MapServer settings route; surface that as unsupported.
    Task<HonuaAdminEndpointResult<HonuaAdminServiceSettingsResponse>> UpdateServiceMapServerSettingsAsync(
        string serviceName,
        HonuaAdminUpdateMapServerSettingsRequest request,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaAdminServiceSettingsResponse>> UpdateServiceTimeInfoAsync(
        string serviceName,
        HonuaAdminUpdateTimeInfoRequest request,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaAdminLayerMetadataResponse>> UpdateServiceLayerMetadataAsync(
        HonuaAdminServiceLayer layer,
        HonuaAdminUpdateLayerMetadataRequest request,
        CancellationToken cancellationToken = default);

}

public interface IHonuaAdminLayerSchemaClient
{
    Task<HonuaAdminEndpointResult<HonuaAdminLayerRelationships>> GetLayerRelationshipsAsync(
        int layerId,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaAdminLayerRelationships>> UpdateLayerRelationshipsAsync(
        int layerId,
        HonuaAdminLayerRelationshipsUpdate request,
        CancellationToken cancellationToken = default);

    // Default body so a hand-rolled fake still compiles; the HTTP client overrides it.
    Task<HonuaAdminEndpointResult<HonuaAdminLayerFilter>> GetLayerFilterAsync(
        int layerId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "This IHonuaAdminOperateClient implementation does not provide GetLayerFilterAsync.");

    // Default body so a hand-rolled fake still compiles; the HTTP client overrides it.
    Task<HonuaAdminEndpointResult<HonuaAdminLayerFilter>> UpdateLayerFilterAsync(
        int layerId,
        HonuaAdminLayerFilterUpdate request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "This IHonuaAdminOperateClient implementation does not provide UpdateLayerFilterAsync.");
}

public interface IHonuaAdminServerInfoClient
{
    Task<HonuaAdminEndpointResult<HonuaAdminVersionResponse>> GetVersionAsync(
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaAdminCapabilitiesResponse>> GetCapabilitiesAsync(
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaAdminLicenseStatusResponse>> GetLicenseStatusAsync(
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaAdminApiKeyResponse[]>> ListApiKeysAsync(
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaAdminOidcProviderResponse[]>> ListOidcProvidersAsync(
        CancellationToken cancellationToken = default);

}

public interface IHonuaAdminPresentationClient
{
    Task<HonuaAdminEndpointResult<HonuaAdminLayerAuthoringDocument>> GetLayerPopupInfoAsync(
        int layerId,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaAdminLayerAuthoringDocument>> UpdateLayerPopupInfoAsync(
        int layerId,
        JsonElement? document,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaAdminLayerAuthoringDocument>> GetLayerDrawingInfoAsync(
        int layerId,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaAdminLayerAuthoringDocument>> UpdateLayerDrawingInfoAsync(
        int layerId,
        JsonElement? document,
        CancellationToken cancellationToken = default);
}
