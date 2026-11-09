using Honua.Console.Contracts;

namespace Honua.Console.IntegrationTests;

internal class UnusedAdminOperateClient : IHonuaAdminOperateClient
{
    public virtual Task<HonuaAdminEndpointResult<HonuaAdminImportPreview>> PreviewImportFileAsync(byte[] bytes, string fileName, string authorityBinding, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public virtual Task<HonuaAdminEndpointResult<HonuaAdminCoordinateSystemSearch>> SearchCoordinateSystemsAsync(string query, string authorityBinding, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public virtual Task<HonuaAdminEndpointResult<HonuaAdminImportResult>> ImportFileBoundAsync(HonuaAdminFileImportRequest request, string authorityBinding, CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public virtual Uri BaseUri => new("https://server.test");

    public virtual Task<HonuaAdminEndpointResult<HonuaAdminLayerFields>> GetLayerFieldsAsync(
        int layerId, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public virtual Task<HonuaAdminEndpointResult<HonuaAdminLayerFields>> UpdateLayerFieldsAsync(
        int layerId, HonuaAdminLayerFieldsUpdate request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public virtual Task<HonuaAdminEndpointResult<HonuaAdminConnectionSummary[]>> ListConnectionsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public virtual Task<HonuaAdminEndpointResult<HonuaAdminConnectionSummary>> CreateConnectionAsync(HonuaAdminCreateConnectionRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public virtual Task<HonuaAdminEndpointResult<HonuaAdminConnectionTestResult>> TestDraftConnectionAsync(HonuaAdminCreateConnectionRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public virtual Task<HonuaAdminEndpointResult<HonuaAdminConnectionTestResult>> TestConnectionAsync(string connectionId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public virtual Task<HonuaAdminEndpointResult<HonuaAdminTableInfo[]>> ListConnectionTablesAsync(string connectionId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public virtual Task<HonuaAdminEndpointResult<HonuaAdminImportFormats>> GetImportFormatsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public virtual Task<HonuaAdminEndpointResult<HonuaAdminExternalServiceDiscovery>> DiscoverExternalServiceAsync(string url, HonuaAdminExternalServiceCredentials? credentials = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public virtual Task<HonuaAdminEndpointResult<HonuaAdminGeoservicesImportJob>> StartGeoservicesImportAsync(HonuaAdminGeoservicesImportRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public virtual Task<HonuaAdminEndpointResult<HonuaAdminGeoservicesImportProgress>> GetGeoservicesImportJobAsync(string jobId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public virtual Task<HonuaAdminEndpointResult<HonuaAdminLayerPage>> ListConnectionLayersAsync(string connectionId, HonuaAdminLayerListRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public virtual Task<HonuaAdminEndpointResult<HonuaAdminPublishedLayerSummary>> PublishLayerAsync(string connectionId, Honua.Sdk.Admin.Models.PublishLayerRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public virtual Task<HonuaAdminEndpointResult<HonuaAdminPublishedLayerSummary>> SetLayerEnabledAsync(string connectionId, int layerId, bool enabled, string? serviceName = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public virtual Task<HonuaAdminEndpointResult<HonuaAdminServicePage>> ListServicesAsync(HonuaAdminCatalogQuery query, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public virtual Task<HonuaAdminEndpointResult<HonuaAdminServiceSettingsResponse>> GetServiceSettingsAsync(string serviceName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public virtual Task<HonuaAdminEndpointResult<HonuaAdminServiceSettingsResponse>> UpdateServiceProtocolsAsync(string serviceName, IReadOnlyList<string> enabledProtocols, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public virtual Task<HonuaAdminEndpointResult<HonuaAdminServiceSettingsResponse>> UpdateServiceAccessPolicyAsync(string serviceName, HonuaAdminUpdateAccessPolicyRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public virtual Task<HonuaAdminEndpointResult<HonuaAdminVersionResponse>> GetVersionAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public virtual Task<HonuaAdminEndpointResult<HonuaAdminCapabilitiesResponse>> GetCapabilitiesAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public virtual Task<HonuaAdminEndpointResult<HonuaAdminLicenseStatusResponse>> GetLicenseStatusAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public virtual Task<HonuaAdminEndpointResult<HonuaAdminApiKeyResponse[]>> ListApiKeysAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public virtual Task<HonuaAdminEndpointResult<HonuaAdminOidcProviderResponse[]>> ListOidcProvidersAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public virtual Task<HonuaAdminEndpointResult<HonuaAdminServiceSettingsResponse>> UpdateServiceMapServerSettingsAsync(string serviceName, HonuaAdminUpdateMapServerSettingsRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public virtual Task<HonuaAdminEndpointResult<HonuaAdminServiceSettingsResponse>> UpdateServiceTimeInfoAsync(string serviceName, HonuaAdminUpdateTimeInfoRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public virtual Task<HonuaAdminEndpointResult<HonuaAdminLayerMetadataResponse>> UpdateServiceLayerMetadataAsync(HonuaAdminServiceLayer layer, HonuaAdminUpdateLayerMetadataRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public virtual Task<HonuaAdminEndpointResult<HonuaAdminLayerRelationships>> GetLayerRelationshipsAsync(int layerId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public virtual Task<HonuaAdminEndpointResult<HonuaAdminLayerRelationships>> UpdateLayerRelationshipsAsync(int layerId, HonuaAdminLayerRelationshipsUpdate request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public virtual Task<HonuaAdminEndpointResult<HonuaAdminLayerAuthoringDocument>> GetLayerPopupInfoAsync(int layerId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public virtual Task<HonuaAdminEndpointResult<HonuaAdminLayerAuthoringDocument>> UpdateLayerPopupInfoAsync(int layerId, System.Text.Json.JsonElement? document, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public virtual Task<HonuaAdminEndpointResult<HonuaAdminLayerAuthoringDocument>> GetLayerDrawingInfoAsync(int layerId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public virtual Task<HonuaAdminEndpointResult<HonuaAdminLayerAuthoringDocument>> UpdateLayerDrawingInfoAsync(int layerId, System.Text.Json.JsonElement? document, CancellationToken cancellationToken = default) => throw new NotImplementedException();
}
