using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

// SHIM(honua-sdk-dotnet#166): the Honua.Sdk.Admin admin client (HonuaAdminClient with
// ListConnectionsAsync/ListServicesAsync/GetServiceSettingsAsync/ListLayersAsync/
// GetVersionAsync/GetCapabilitiesAsync/GetLicenseStatusAsync/ListOidcProvidersAsync)
// exists in honua-sdk-dotnet source, but there is no consumable package: the only
// restorable Honua.Sdk.Admin build is the prerelease 0.1.15-alpha.1 (the SDK ships a
// stable Honua.Sdk.Abstractions 1.0.0 but no stable Admin counterpart), and honua-console
// wires no SDK NuGet feed. Referencing it would pin Console to a prerelease through the
// non-hermetic global package cache and break clean/CI restores and the single deployable
// artifact, so per SDK_SHIM_POLICY.md the contract is treated as pending. Keep these wire
// records and the thin HTTP client in the Console contracts boundary until honua-sdk-dotnet
// publishes a consumable stable Admin package and honua-console#7 wires the feed and swaps
// to SDK types. Do not add a sibling-repo ProjectReference. See SDK_SHIM_POLICY "Active Shims".
public sealed record HonuaAdminOperateClientOptions(Uri BaseUri, string? ApiKey = null);

public partial interface IHonuaAdminOperateClient
{
    Uri BaseUri { get; }

    Task<HonuaAdminEndpointResult<HonuaAdminConnectionSummary[]>> ListConnectionsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a data connection through the real honua-server admin endpoint
    /// (<c>POST /api/v1/admin/connections/</c>, mirrors <c>CreateConnectionRequest</c>). This is the console's
    /// connection-create OPERATION: it actually persists the connection on the server rather than recording
    /// local intent, and returns the created connection summary or a field-addressable
    /// <see cref="HonuaAdminEndpointIssue"/> when the server rejects the request.
    /// </summary>
    Task<HonuaAdminEndpointResult<HonuaAdminConnectionSummary>> CreateConnectionAsync(
        HonuaAdminCreateConnectionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests a draft connection's health WITHOUT persisting it through the real honua-server admin endpoint
    /// (<c>POST /api/v1/admin/connections/test</c>). This is the console's pre-save connection-test OPERATION:
    /// the server opens the target with the supplied credentials and reports health, so the Add Connection
    /// form can prove connectivity before creating the connection. Returns the test result or a
    /// field-addressable <see cref="HonuaAdminEndpointIssue"/> when the server rejects the request.
    /// </summary>
    Task<HonuaAdminEndpointResult<HonuaAdminConnectionTestResult>> TestDraftConnectionAsync(
        HonuaAdminCreateConnectionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests an EXISTING connection's health through the real honua-server admin endpoint
    /// (<c>POST /api/v1/admin/connections/{id}/test</c>). Unlike the draft test, the server persists the
    /// resulting health status on the connection, so a subsequent read reflects it. Returns the test result or
    /// an <see cref="HonuaAdminEndpointIssue"/> when the connection is missing or the server rejects the request.
    /// </summary>
    Task<HonuaAdminEndpointResult<HonuaAdminConnectionTestResult>> TestConnectionAsync(
        string connectionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Discovers the publishable (PostGIS spatial) tables on a connection through the real honua-server admin
    /// endpoint (<c>GET /api/v1/admin/connections/{id}/tables</c>, Issue #57). Powers the publish-layer table
    /// picker. Note: this endpoint returns a bare <c>{ "tables": [...] }</c> body (not the ApiResponse envelope).
    /// </summary>
    Task<HonuaAdminEndpointResult<HonuaAdminTableInfo[]>> ListConnectionTablesAsync(
        string connectionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the geospatial file formats the server can import (<c>GET /api/v1/admin/import/formats</c>),
    /// so the console can validate a chosen file's extension before uploading. Bare
    /// <c>{ supportedExtensions, formatDescriptions }</c> body (not the ApiResponse envelope).
    /// </summary>
    Task<HonuaAdminEndpointResult<HonuaAdminImportFormats>> GetImportFormatsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads a geospatial file to be imported into PostgreSQL via streamed multipart ingest
    /// (<c>POST /api/v1/admin/import/upload</c>; multipart <c>file</c> + <c>TableName</c> + optional
    /// <c>TargetSchema</c>). Returns the import result (bare body, HTTP 200 even on a failed import — check
    /// <see cref="HonuaAdminImportResult.Success"/>).
    /// </summary>
    Task<HonuaAdminEndpointResult<HonuaAdminImportResult>> ImportFileAsync(
        byte[] fileContent,
        string fileName,
        string tableName,
        string? targetSchema,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Discovers the importable layers of a remote Esri/OGC service (<c>POST /api/v1/admin/external-services/discover</c>,
    /// JSON <c>{ "url": "https://…" }</c>; the server requires an HTTPS URL). Returns the service type/name and
    /// candidate layers, or a field-addressable <see cref="HonuaAdminEndpointIssue"/> on rejection. Bare body.
    /// </summary>
    Task<HonuaAdminEndpointResult<HonuaAdminExternalServiceDiscovery>> DiscoverExternalServiceAsync(
        string url,
        HonuaAdminExternalServiceCredentials? credentials = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues an ArcGIS GeoServices layer import (<c>POST /api/v1/admin/import/geoservices/start</c>). Returns a
    /// job descriptor (HTTP 202) whose id is polled via <see cref="GetGeoservicesImportJobAsync"/>.
    /// </summary>
    Task<HonuaAdminEndpointResult<HonuaAdminGeoservicesImportJob>> StartGeoservicesImportAsync(
        HonuaAdminGeoservicesImportRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the progress of a queued GeoServices import job
    /// (<c>GET /api/v1/admin/import/geoservices/jobs/{jobId}</c>).
    /// </summary>
    Task<HonuaAdminEndpointResult<HonuaAdminGeoservicesImportProgress>> GetGeoservicesImportJobAsync(
        string jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a layer's persisted field configuration — aliases, coded-value domains, visibility
    /// (<c>GET /api/v1/admin/metadata/layers/{layerId}/fields</c>). <paramref name="layerId"/> is the global id.
    /// </summary>
    Task<HonuaAdminEndpointResult<HonuaAdminLayerFields>> GetLayerFieldsAsync(
        int layerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a layer's field configuration — set/clear coded-value domains and aliases
    /// (<c>PUT /api/v1/admin/metadata/layers/{layerId}/fields</c>).
    /// </summary>
    Task<HonuaAdminEndpointResult<HonuaAdminLayerFields>> UpdateLayerFieldsAsync(
        int layerId,
        HonuaAdminLayerFieldsUpdate request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a layer's persisted display hints — min/max scale, default visibility, display field, queryable,
    /// hasZ/hasM (<c>GET /api/v1/admin/metadata/layers/{layerId}/display</c>). <paramref name="layerId"/> is the
    /// global id.
    /// </summary>
    Task<HonuaAdminEndpointResult<HonuaAdminLayerDisplay>> GetLayerDisplayAsync(
        int layerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a layer's display hints (<c>PUT /api/v1/admin/metadata/layers/{layerId}/display</c>). A null/omitted
    /// request field leaves the corresponding server value unchanged; the server re-reads and returns the
    /// persisted display projection.
    /// </summary>
    Task<HonuaAdminEndpointResult<HonuaAdminLayerDisplay>> UpdateLayerDisplayAsync(
        int layerId,
        HonuaAdminLayerDisplayUpdate request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a layer's persisted editor-tracking + edit-capability metadata — globalId/creator/created-at/
    /// editor/updated-at fields, canModify, supportsAttachments, supportsRelatedRecords
    /// (<c>GET /api/v1/admin/metadata/layers/{layerId}/editing</c>). <paramref name="layerId"/> is the global id.
    /// </summary>
    Task<HonuaAdminEndpointResult<HonuaAdminLayerEditing>> GetLayerEditingAsync(
        int layerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a layer's editor-tracking + edit-capability metadata
    /// (<c>PUT /api/v1/admin/metadata/layers/{layerId}/editing</c>). A null/omitted request field leaves the
    /// corresponding server value unchanged; the server re-reads and returns the persisted editing projection.
    /// </summary>
    Task<HonuaAdminEndpointResult<HonuaAdminLayerEditing>> UpdateLayerEditingAsync(
        int layerId,
        HonuaAdminLayerEditingUpdate request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a layer's persisted spatial/CRS metadata — supported CRS list, storage CRS, storage-CRS coordinate
    /// epoch (<c>GET /api/v1/admin/metadata/layers/{layerId}/spatial</c>). <paramref name="layerId"/> is the
    /// global id. SRID/geometry are reported but not authored by the matching PUT.
    /// </summary>
    Task<HonuaAdminEndpointResult<HonuaAdminLayerSpatial>> GetLayerSpatialAsync(
        int layerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a layer's CRS-list/output spatial metadata only — supported CRS list, storage CRS, storage-CRS
    /// coordinate epoch (<c>PUT /api/v1/admin/metadata/layers/{layerId}/spatial</c>). The stored SRID/geometry
    /// are untouched. For the supported-CRS list: omit = unchanged, <c>[]</c> = clear; the explicit
    /// clear-storage flags clear the scalar output fields. The server re-reads and returns the persisted spatial
    /// projection.
    /// </summary>
    Task<HonuaAdminEndpointResult<HonuaAdminLayerSpatial>> UpdateLayerSpatialAsync(
        int layerId,
        HonuaAdminLayerSpatialUpdate request,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaAdminPublishedLayerSummary[]>> ListConnectionLayersAsync(
        string connectionId,
        string? serviceName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a PostGIS table as a queryable service layer through the real honua-server admin
    /// layer-publishing endpoint (<c>POST /api/v1/admin/connections/{id}/layers</c>). This is the
    /// console's service-layer-publish OPERATION (issue #144): it actually lands a layer on the server
    /// rather than recording local intent. The result carries the published layer summary, or a
    /// field-addressable <see cref="HonuaAdminEndpointIssue"/> when the server rejects the request.
    /// </summary>
    Task<HonuaAdminEndpointResult<HonuaAdminPublishedLayerSummary>> PublishLayerAsync(
        string connectionId,
        HonuaAdminPublishLayerRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables or disables a published layer through the real honua-server admin endpoint
    /// (<c>PUT /api/v1/admin/connections/{id}/layers/{layerId}/enabled</c>). This is the console's
    /// layer enable/disable OPERATION (Wave 5, plan §3 Family A): it actually toggles the layer's
    /// enabled state on the server rather than recording local intent, and returns the updated layer
    /// summary or a field-addressable issue on rejection.
    /// </summary>
    Task<HonuaAdminEndpointResult<HonuaAdminPublishedLayerSummary>> SetLayerEnabledAsync(
        string connectionId,
        int layerId,
        bool enabled,
        string? serviceName = null,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaAdminServiceSummary[]>> ListServicesAsync(
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaAdminServiceSettingsResponse>> GetServiceSettingsAsync(
        string serviceName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the set of enabled protocols for a service through the real honua-server admin endpoint
    /// (<c>PUT /api/v1/admin/services/{serviceName}/protocols</c>). This is the console's service
    /// protocol-configuration OPERATION (Wave 5, plan §3 Family A): the server re-reads and returns the
    /// updated settings projection, so the result reflects the canonical post-change state.
    /// </summary>
    Task<HonuaAdminEndpointResult<HonuaAdminServiceSettingsResponse>> UpdateServiceProtocolsAsync(
        string serviceName,
        IReadOnlyList<string> enabledProtocols,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the access policy (anonymous read/write + allowed roles) for a service through the real
    /// honua-server admin endpoint (<c>PUT /api/v1/admin/services/{serviceName}/access-policy</c>). This is
    /// the console's service visibility/access OPERATION (Wave 5). Null request fields are left unchanged
    /// server-side; the server re-reads and returns the updated settings projection.
    /// </summary>
    Task<HonuaAdminEndpointResult<HonuaAdminServiceSettingsResponse>> UpdateServiceAccessPolicyAsync(
        string serviceName,
        HonuaAdminUpdateAccessPolicyRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a service's MapServer render settings (max/default image size, DPI, default format,
    /// transparency, max features per layer) through the real honua-server admin endpoint
    /// (<c>PUT /api/v1/admin/services/{serviceName}/mapserver</c>, mirrors <c>UpdateMapServerSettingsRequest</c>).
    /// Null request fields are left unchanged server-side; the server re-reads and returns the updated settings
    /// projection. Note (gap analysis): this PUT may answer <c>501 Not Implemented</c> on a server build that
    /// has not landed the MapServer-settings write path yet — the resulting issue carries an "Unsupported"
    /// state and the 501 status so the caller can surface it honestly rather than fabricating a success.
    /// </summary>
    Task<HonuaAdminEndpointResult<HonuaAdminServiceSettingsResponse>> UpdateServiceMapServerSettingsAsync(
        string serviceName,
        HonuaAdminUpdateMapServerSettingsRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a service's temporal time-info (start/end time fields + track id field) through the real
    /// honua-server admin endpoint (<c>PUT /api/v1/admin/services/{serviceName}/timeinfo</c>, mirrors
    /// <c>UpdateTimeInfoRequest</c>). Null fields clear the corresponding time field server-side; the server
    /// re-reads and returns the updated settings projection so the result reflects the post-change state.
    /// </summary>
    Task<HonuaAdminEndpointResult<HonuaAdminServiceSettingsResponse>> UpdateServiceTimeInfoAsync(
        string serviceName,
        HonuaAdminUpdateTimeInfoRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a service's current settings caps (maxRecordCount, query timeout, attachment caps, supported
    /// formats, …) through the real honua-server admin endpoint
    /// (<c>GET /api/v1/admin/services/{serviceName}/settings-caps</c>). Returns the caps projection or a
    /// status-mapped <see cref="HonuaAdminEndpointIssue"/> (Unsupported on 404/501, etc.).
    /// </summary>
    /// <remarks>
    /// Default-implemented so existing implementors (e.g. test fakes) compile without change; the real
    /// <see cref="HonuaAdminOperateHttpClient"/> overrides it to call the server.
    /// </remarks>
    Task<HonuaAdminEndpointResult<HonuaAdminServiceSettingsCapsResponse>> GetServiceSettingsCapsAsync(
        string serviceName,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "This IHonuaAdminOperateClient implementation does not provide GetServiceSettingsCapsAsync.");

    /// <summary>
    /// Updates a service's settings caps — request-size and result-size limits the server enforces on this
    /// service (maxRecordCount, defaultRecordCount, maxFeaturesPerLayer, queryTimeoutMs, maxEditsPerTransaction,
    /// maxPayloadBytes, supportedFormats, defaultFormat, defaultTileMatrixSet, supportsAttachments,
    /// maxAttachmentSizeBytes) through the real honua-server admin endpoint
    /// (<c>PUT /api/v1/admin/services/{serviceName}/settings-caps</c>, mirrors
    /// <c>UpdateServiceSettingsCapsRequest</c>). Null/omitted fields are left unchanged server-side; the server
    /// rejects negative caps. The server re-reads and returns the updated caps projection so the result
    /// reflects the canonical post-change state. May answer <c>501 Not Implemented</c> on a server build that
    /// has not landed the write path — the resulting issue carries an "Unsupported" state and the 501 status so
    /// the caller can surface it honestly rather than fabricating a success.
    /// </summary>
    /// <remarks>
    /// Default-implemented so existing implementors (e.g. test fakes) compile without change; the real
    /// <see cref="HonuaAdminOperateHttpClient"/> overrides it to call the server.
    /// </remarks>
    Task<HonuaAdminEndpointResult<HonuaAdminServiceSettingsCapsResponse>> UpdateServiceSettingsCapsAsync(
        string serviceName,
        HonuaAdminUpdateServiceSettingsCapsRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "This IHonuaAdminOperateClient implementation does not provide UpdateServiceSettingsCapsAsync.");

    /// <summary>
    /// Reads a publication's persisted overrides — the title override, per-publication field aliases,
    /// capabilities, supported formats, and whether this is the primary publication of its layer
    /// (<c>GET /api/v1/admin/metadata/publications/{publicationId}/overrides</c>). <paramref name="publicationId"/>
    /// is the publication's metadata id (a layer's exposure within a service). Returns the overrides projection
    /// or a status-mapped <see cref="HonuaAdminEndpointIssue"/> (Unsupported on 404/501, etc.).
    /// </summary>
    /// <remarks>
    /// Default-implemented so existing implementors (e.g. test fakes) compile without change; the real
    /// <see cref="HonuaAdminOperateHttpClient"/> overrides it to call the server.
    /// </remarks>
    Task<HonuaAdminEndpointResult<HonuaAdminPublicationOverrides>> GetPublicationOverridesAsync(
        string publicationId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "This IHonuaAdminOperateClient implementation does not provide GetPublicationOverridesAsync.");

    /// <summary>
    /// Updates a publication's overrides — titleOverride, per-publication field aliases, capabilities,
    /// supported formats, and isPrimary
    /// (<c>PUT /api/v1/admin/metadata/publications/{publicationId}/overrides</c>). A null scalar leaves the
    /// corresponding value unchanged; an empty string clears the title; an empty array/map clears that list/map.
    /// The server re-reads and returns the persisted overrides projection so the result reflects the canonical
    /// post-change state. 404 when the publication id is unknown.
    /// </summary>
    /// <remarks>
    /// Default-implemented so existing implementors (e.g. test fakes) compile without change; the real
    /// <see cref="HonuaAdminOperateHttpClient"/> overrides it to call the server.
    /// </remarks>
    Task<HonuaAdminEndpointResult<HonuaAdminPublicationOverrides>> UpdatePublicationOverridesAsync(
        string publicationId,
        HonuaAdminPublicationOverridesUpdate request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "This IHonuaAdminOperateClient implementation does not provide UpdatePublicationOverridesAsync.");

    /// <summary>
    /// Reads a layer's persisted relationships (origin/destination, cardinality, esriRelationshipId)
    /// (<c>GET /api/v1/admin/metadata/layers/{layerId}/relationships</c>). <paramref name="layerId"/> is the
    /// global id. FeatureServer layer metadata emits these as <c>relationships[]</c>.
    /// </summary>
    Task<HonuaAdminEndpointResult<HonuaAdminLayerRelationships>> GetLayerRelationshipsAsync(
        int layerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces a layer's relationships (<c>PUT /api/v1/admin/metadata/layers/{layerId}/relationships</c>).
    /// The PUT body carries the full relationship set; the server re-reads and returns the persisted set.
    /// </summary>
    Task<HonuaAdminEndpointResult<HonuaAdminLayerRelationships>> UpdateLayerRelationshipsAsync(
        int layerId,
        HonuaAdminLayerRelationshipsUpdate request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a layer's discovery / catalog metadata — title, description, keywords, themes, language, license,
    /// attribution, publisher, contact point, links (<c>GET /api/v1/admin/metadata/layers/{layerId}/discovery</c>).
    /// <paramref name="layerId"/> is the global id. This metadata drives the layer's OGC API Records / STAC /
    /// DCAT / Esri documentInfo output.
    /// </summary>
    Task<HonuaAdminEndpointResult<HonuaAdminDiscoveryMetadata>> GetLayerDiscoveryAsync(
        int layerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a layer's discovery / catalog metadata
    /// (<c>PUT /api/v1/admin/metadata/layers/{layerId}/discovery</c>). A null/omitted scalar leaves that field
    /// unchanged server-side; an empty array (<c>[]</c>) clears a list. The server re-reads and returns the
    /// persisted metadata.
    /// </summary>
    Task<HonuaAdminEndpointResult<HonuaAdminDiscoveryMetadata>> UpdateLayerDiscoveryAsync(
        int layerId,
        HonuaAdminDiscoveryMetadataUpdate request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a service's discovery / catalog metadata
    /// (<c>GET /api/v1/admin/services/{serviceName}/discovery</c>). Drives the service's OGC API Records / STAC /
    /// DCAT / Esri documentInfo output.
    /// </summary>
    Task<HonuaAdminEndpointResult<HonuaAdminDiscoveryMetadata>> GetServiceDiscoveryAsync(
        string serviceName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a service's discovery / catalog metadata
    /// (<c>PUT /api/v1/admin/services/{serviceName}/discovery</c>). A null/omitted scalar leaves that field
    /// unchanged server-side; an empty array (<c>[]</c>) clears a list. The server re-reads and returns the
    /// persisted metadata.
    /// </summary>
    Task<HonuaAdminEndpointResult<HonuaAdminDiscoveryMetadata>> UpdateServiceDiscoveryAsync(
        string serviceName,
        HonuaAdminDiscoveryMetadataUpdate request,
        CancellationToken cancellationToken = default);

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

    /// <summary>
    /// GET-probes an admin endpoint and reports reachability: <c>Data=true</c> on a 2xx, otherwise the
    /// status-mapped issue (Unsupported on 404/501, Missing permission on 401/403, Unavailable otherwise).
    /// Used to drive capability states from the live server instead of hardcoded assumptions.
    /// </summary>
    Task<HonuaAdminEndpointResult<bool>> ProbeEndpointAsync(
        string contract,
        string relativePath,
        CancellationToken cancellationToken = default);
}

public sealed partial class HonuaAdminOperateHttpClient : IHonuaAdminOperateClient, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;

    public HonuaAdminOperateHttpClient(HttpClient httpClient, HonuaAdminOperateClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        BaseUri = options.BaseUri;
        _apiKey = options.ApiKey;
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= BaseUri;
    }

    public Uri BaseUri { get; }

    public Task<HonuaAdminEndpointResult<HonuaAdminConnectionSummary[]>> ListConnectionsAsync(
        CancellationToken cancellationToken = default) =>
        GetApiResponseAsync<HonuaAdminConnectionSummary[]>(
            "/api/v1/admin/connections/",
            "GET /api/v1/admin/connections",
            cancellationToken);

    public Task<HonuaAdminEndpointResult<HonuaAdminConnectionSummary>> CreateConnectionAsync(
        HonuaAdminCreateConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return PostApiResponseAsync<HonuaAdminCreateConnectionRequest, HonuaAdminConnectionSummary>(
            "/api/v1/admin/connections/",
            request,
            "POST /api/v1/admin/connections",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaAdminConnectionTestResult>> TestDraftConnectionAsync(
        HonuaAdminCreateConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return PostApiResponseAsync<HonuaAdminCreateConnectionRequest, HonuaAdminConnectionTestResult>(
            "/api/v1/admin/connections/test",
            request,
            "POST /api/v1/admin/connections/test",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaAdminConnectionTestResult>> TestConnectionAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);

        return PostApiResponseAsync<HonuaAdminConnectionTestResult>(
            $"/api/v1/admin/connections/{Uri.EscapeDataString(connectionId)}/test",
            "POST /api/v1/admin/connections/{id}/test",
            cancellationToken);
    }

    public async Task<HonuaAdminEndpointResult<HonuaAdminTableInfo[]>> ListConnectionTablesAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);

        const string contract = "GET /api/v1/admin/connections/{id}/tables";
        var path = $"/api/v1/admin/connections/{Uri.EscapeDataString(connectionId)}/tables";

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", _apiKey);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return HonuaAdminEndpointResult<HonuaAdminTableInfo[]>.FromIssue(new HonuaAdminEndpointIssue(
                "Unavailable", contract, $"The Honua server endpoint could not be reached: {ex.Message}"));
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return HonuaAdminEndpointResult<HonuaAdminTableInfo[]>.FromIssue(CreateIssue(contract, response.StatusCode));
            }

            // This endpoint returns a bare { "tables": [...] } body, NOT the ApiResponse<T> envelope.
            HonuaAdminTableDiscoveryBody? body;
            try
            {
                body = await response.Content
                    .ReadFromJsonAsync<HonuaAdminTableDiscoveryBody>(JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                return HonuaAdminEndpointResult<HonuaAdminTableInfo[]>.FromIssue(new HonuaAdminEndpointIssue(
                    "Unsupported", contract,
                    $"The Honua server response did not match the expected table-discovery shape: {ex.Message}",
                    (int)response.StatusCode));
            }

            return HonuaAdminEndpointResult<HonuaAdminTableInfo[]>.FromData(body?.Tables ?? []);
        }
    }

    public async Task<HonuaAdminEndpointResult<HonuaAdminImportFormats>> GetImportFormatsAsync(
        CancellationToken cancellationToken = default)
    {
        const string contract = "GET /api/v1/admin/import/formats";
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/import/formats");
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", _apiKey);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return HonuaAdminEndpointResult<HonuaAdminImportFormats>.FromIssue(new HonuaAdminEndpointIssue(
                "Unavailable", contract, $"The Honua server endpoint could not be reached: {ex.Message}"));
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return HonuaAdminEndpointResult<HonuaAdminImportFormats>.FromIssue(CreateIssue(contract, response.StatusCode));
            }

            try
            {
                var formats = await response.Content
                    .ReadFromJsonAsync<HonuaAdminImportFormats>(JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                return HonuaAdminEndpointResult<HonuaAdminImportFormats>.FromData(formats ?? new HonuaAdminImportFormats());
            }
            catch (JsonException ex)
            {
                return HonuaAdminEndpointResult<HonuaAdminImportFormats>.FromIssue(new HonuaAdminEndpointIssue(
                    "Unsupported", contract,
                    $"The Honua server response did not match the expected formats shape: {ex.Message}",
                    (int)response.StatusCode));
            }
        }
    }

    public async Task<HonuaAdminEndpointResult<HonuaAdminImportResult>> ImportFileAsync(
        byte[] fileContent,
        string fileName,
        string tableName,
        string? targetSchema,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileContent);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        const string contract = "POST /api/v1/admin/import/upload";
        using var form = new MultipartFormDataContent();
        var fileEntry = new ByteArrayContent(fileContent);
        fileEntry.Headers.TryAddWithoutValidation("Content-Type", "application/octet-stream");
        form.Add(fileEntry, "file", fileName);
        form.Add(new StringContent(tableName), "TableName");
        if (!string.IsNullOrWhiteSpace(targetSchema))
        {
            form.Add(new StringContent(targetSchema), "TargetSchema");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/import/upload") { Content = form };
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", _apiKey);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return HonuaAdminEndpointResult<HonuaAdminImportResult>.FromIssue(new HonuaAdminEndpointIssue(
                "Unavailable", contract, $"The Honua server endpoint could not be reached: {ex.Message}"));
        }

        using (response)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var issue = CreateIssue(contract, response.StatusCode);
                return HonuaAdminEndpointResult<HonuaAdminImportResult>.FromIssue(issue with
                {
                    Detail = ParseFailureMessage(payload) is { Length: > 0 } m ? m : issue.Detail,
                });
            }

            try
            {
                var result = string.IsNullOrWhiteSpace(payload)
                    ? null
                    : JsonSerializer.Deserialize<HonuaAdminImportResult>(payload, JsonOptions);
                return HonuaAdminEndpointResult<HonuaAdminImportResult>.FromData(result ?? new HonuaAdminImportResult());
            }
            catch (JsonException ex)
            {
                return HonuaAdminEndpointResult<HonuaAdminImportResult>.FromIssue(new HonuaAdminEndpointIssue(
                    "Unsupported", contract,
                    $"The Honua server response did not match the expected import-result shape: {ex.Message}",
                    (int)response.StatusCode));
            }
        }
    }

    public async Task<HonuaAdminEndpointResult<HonuaAdminExternalServiceDiscovery>> DiscoverExternalServiceAsync(
        string url,
        HonuaAdminExternalServiceCredentials? credentials = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        const string contract = "POST /api/v1/admin/external-services/discover";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/external-services/discover")
        {
            Content = JsonContent.Create(new ExternalServiceDiscoverBody(url, credentials), options: JsonOptions),
        };
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", _apiKey);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return HonuaAdminEndpointResult<HonuaAdminExternalServiceDiscovery>.FromIssue(new HonuaAdminEndpointIssue(
                "Unavailable", contract, $"The Honua server endpoint could not be reached: {ex.Message}"));
        }

        using (response)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var issue = CreateIssue(contract, response.StatusCode);
                var message = ParseFailureMessage(payload);
                return HonuaAdminEndpointResult<HonuaAdminExternalServiceDiscovery>.FromIssue(issue with
                {
                    Detail = string.IsNullOrWhiteSpace(message) ? issue.Detail : message,
                });
            }

            try
            {
                var discovery = string.IsNullOrWhiteSpace(payload)
                    ? null
                    : JsonSerializer.Deserialize<HonuaAdminExternalServiceDiscovery>(payload, JsonOptions);
                return HonuaAdminEndpointResult<HonuaAdminExternalServiceDiscovery>.FromData(discovery ?? new HonuaAdminExternalServiceDiscovery());
            }
            catch (JsonException ex)
            {
                return HonuaAdminEndpointResult<HonuaAdminExternalServiceDiscovery>.FromIssue(new HonuaAdminEndpointIssue(
                    "Unsupported", contract,
                    $"The Honua server response did not match the expected discovery shape: {ex.Message}",
                    (int)response.StatusCode));
            }
        }
    }

    public async Task<HonuaAdminEndpointResult<HonuaAdminGeoservicesImportJob>> StartGeoservicesImportAsync(
        HonuaAdminGeoservicesImportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        const string contract = "POST /api/v1/admin/import/geoservices/start";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/import/geoservices/start")
        {
            Content = JsonContent.Create(request, options: JsonOptions),
        };
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            httpRequest.Headers.TryAddWithoutValidation("X-API-Key", _apiKey);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return HonuaAdminEndpointResult<HonuaAdminGeoservicesImportJob>.FromIssue(new HonuaAdminEndpointIssue(
                "Unavailable", contract, $"The Honua server endpoint could not be reached: {ex.Message}"));
        }

        using (response)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var issue = CreateIssue(contract, response.StatusCode);
                var message = ParseFailureMessage(payload);
                return HonuaAdminEndpointResult<HonuaAdminGeoservicesImportJob>.FromIssue(issue with
                {
                    Detail = string.IsNullOrWhiteSpace(message) ? issue.Detail : message,
                });
            }

            try
            {
                var job = string.IsNullOrWhiteSpace(payload)
                    ? null
                    : JsonSerializer.Deserialize<HonuaAdminGeoservicesImportJob>(payload, JsonOptions);
                return job is null || string.IsNullOrWhiteSpace(job.JobId)
                    ? HonuaAdminEndpointResult<HonuaAdminGeoservicesImportJob>.FromIssue(new HonuaAdminEndpointIssue(
                        "Unsupported", contract, "The Honua server did not return an import job id.",
                        (int)response.StatusCode))
                    : HonuaAdminEndpointResult<HonuaAdminGeoservicesImportJob>.FromData(job);
            }
            catch (JsonException ex)
            {
                return HonuaAdminEndpointResult<HonuaAdminGeoservicesImportJob>.FromIssue(new HonuaAdminEndpointIssue(
                    "Unsupported", contract,
                    $"The Honua server response did not match the expected import-job shape: {ex.Message}",
                    (int)response.StatusCode));
            }
        }
    }

    public async Task<HonuaAdminEndpointResult<HonuaAdminGeoservicesImportProgress>> GetGeoservicesImportJobAsync(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        const string contract = "GET /api/v1/admin/import/geoservices/jobs/{jobId}";
        var path = $"/api/v1/admin/import/geoservices/jobs/{Uri.EscapeDataString(jobId)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", _apiKey);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return HonuaAdminEndpointResult<HonuaAdminGeoservicesImportProgress>.FromIssue(new HonuaAdminEndpointIssue(
                "Unavailable", contract, $"The Honua server endpoint could not be reached: {ex.Message}"));
        }

        using (response)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var issue = CreateIssue(contract, response.StatusCode);
                var message = ParseFailureMessage(payload);
                return HonuaAdminEndpointResult<HonuaAdminGeoservicesImportProgress>.FromIssue(issue with
                {
                    Detail = string.IsNullOrWhiteSpace(message) ? issue.Detail : message,
                });
            }

            try
            {
                var progress = string.IsNullOrWhiteSpace(payload)
                    ? null
                    : JsonSerializer.Deserialize<HonuaAdminGeoservicesImportProgress>(payload, JsonOptions);
                return HonuaAdminEndpointResult<HonuaAdminGeoservicesImportProgress>.FromData(
                    progress ?? new HonuaAdminGeoservicesImportProgress());
            }
            catch (JsonException ex)
            {
                return HonuaAdminEndpointResult<HonuaAdminGeoservicesImportProgress>.FromIssue(new HonuaAdminEndpointIssue(
                    "Unsupported", contract,
                    $"The Honua server response did not match the expected import-progress shape: {ex.Message}",
                    (int)response.StatusCode));
            }
        }
    }

    public Task<HonuaAdminEndpointResult<HonuaAdminPublishedLayerSummary[]>> ListConnectionLayersAsync(
        string connectionId,
        string? serviceName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);

        var path = $"/api/v1/admin/connections/{Uri.EscapeDataString(connectionId)}/layers/";
        if (!string.IsNullOrWhiteSpace(serviceName))
        {
            path += $"?serviceName={Uri.EscapeDataString(serviceName)}";
        }

        return GetApiResponseAsync<HonuaAdminPublishedLayerSummary[]>(
            path,
            "GET /api/v1/admin/connections/{id}/layers",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaAdminPublishedLayerSummary>> PublishLayerAsync(
        string connectionId,
        HonuaAdminPublishLayerRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentNullException.ThrowIfNull(request);

        var path = $"/api/v1/admin/connections/{Uri.EscapeDataString(connectionId)}/layers/";
        return PostApiResponseAsync<HonuaAdminPublishLayerRequest, HonuaAdminPublishedLayerSummary>(
            path,
            request,
            "POST /api/v1/admin/connections/{id}/layers",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaAdminPublishedLayerSummary>> SetLayerEnabledAsync(
        string connectionId,
        int layerId,
        bool enabled,
        string? serviceName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);

        var path =
            $"/api/v1/admin/connections/{Uri.EscapeDataString(connectionId)}/layers/{layerId.ToString(CultureInfo.InvariantCulture)}/enabled";
        if (!string.IsNullOrWhiteSpace(serviceName))
        {
            path += $"?serviceName={Uri.EscapeDataString(serviceName)}";
        }

        return PutApiResponseAsync<LayerEnabledBody, HonuaAdminPublishedLayerSummary>(
            path,
            new LayerEnabledBody(enabled),
            "PUT /api/v1/admin/connections/{id}/layers/{layerId}/enabled",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaAdminServiceSummary[]>> ListServicesAsync(
        CancellationToken cancellationToken = default) =>
        GetApiResponseAsync<HonuaAdminServiceSummary[]>(
            "/api/v1/admin/services/",
            "GET /api/v1/admin/services",
            cancellationToken);

    public Task<HonuaAdminEndpointResult<HonuaAdminServiceSettingsResponse>> GetServiceSettingsAsync(
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        return GetApiResponseAsync<HonuaAdminServiceSettingsResponse>(
            $"/api/v1/admin/services/{Uri.EscapeDataString(serviceName)}/settings",
            "GET /api/v1/admin/services/{serviceName}/settings",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaAdminServiceSettingsResponse>> UpdateServiceProtocolsAsync(
        string serviceName,
        IReadOnlyList<string> enabledProtocols,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentNullException.ThrowIfNull(enabledProtocols);

        return PutApiResponseAsync<UpdateProtocolsBody, HonuaAdminServiceSettingsResponse>(
            $"/api/v1/admin/services/{Uri.EscapeDataString(serviceName)}/protocols",
            new UpdateProtocolsBody(enabledProtocols),
            "PUT /api/v1/admin/services/{serviceName}/protocols",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaAdminServiceSettingsResponse>> UpdateServiceAccessPolicyAsync(
        string serviceName,
        HonuaAdminUpdateAccessPolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentNullException.ThrowIfNull(request);

        return PutApiResponseAsync<HonuaAdminUpdateAccessPolicyRequest, HonuaAdminServiceSettingsResponse>(
            $"/api/v1/admin/services/{Uri.EscapeDataString(serviceName)}/access-policy",
            request,
            "PUT /api/v1/admin/services/{serviceName}/access-policy",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaAdminServiceSettingsResponse>> UpdateServiceMapServerSettingsAsync(
        string serviceName,
        HonuaAdminUpdateMapServerSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentNullException.ThrowIfNull(request);

        return PutApiResponseAsync<HonuaAdminUpdateMapServerSettingsRequest, HonuaAdminServiceSettingsResponse>(
            $"/api/v1/admin/services/{Uri.EscapeDataString(serviceName)}/mapserver",
            request,
            "PUT /api/v1/admin/services/{serviceName}/mapserver",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaAdminServiceSettingsResponse>> UpdateServiceTimeInfoAsync(
        string serviceName,
        HonuaAdminUpdateTimeInfoRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentNullException.ThrowIfNull(request);

        return PutApiResponseAsync<HonuaAdminUpdateTimeInfoRequest, HonuaAdminServiceSettingsResponse>(
            $"/api/v1/admin/services/{Uri.EscapeDataString(serviceName)}/timeinfo",
            request,
            "PUT /api/v1/admin/services/{serviceName}/timeinfo",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaAdminServiceSettingsCapsResponse>> GetServiceSettingsCapsAsync(
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        return GetApiResponseAsync<HonuaAdminServiceSettingsCapsResponse>(
            $"/api/v1/admin/services/{Uri.EscapeDataString(serviceName)}/settings-caps",
            "GET /api/v1/admin/services/{serviceName}/settings-caps",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaAdminServiceSettingsCapsResponse>> UpdateServiceSettingsCapsAsync(
        string serviceName,
        HonuaAdminUpdateServiceSettingsCapsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentNullException.ThrowIfNull(request);

        return PutApiResponseAsync<HonuaAdminUpdateServiceSettingsCapsRequest, HonuaAdminServiceSettingsCapsResponse>(
            $"/api/v1/admin/services/{Uri.EscapeDataString(serviceName)}/settings-caps",
            request,
            "PUT /api/v1/admin/services/{serviceName}/settings-caps",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaAdminPublicationOverrides>> GetPublicationOverridesAsync(
        string publicationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);

        return GetApiResponseAsync<HonuaAdminPublicationOverrides>(
            $"/api/v1/admin/metadata/publications/{Uri.EscapeDataString(publicationId)}/overrides",
            "GET /api/v1/admin/metadata/publications/{publicationId}/overrides",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaAdminPublicationOverrides>> UpdatePublicationOverridesAsync(
        string publicationId,
        HonuaAdminPublicationOverridesUpdate request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        ArgumentNullException.ThrowIfNull(request);

        return PutApiResponseAsync<HonuaAdminPublicationOverridesUpdate, HonuaAdminPublicationOverrides>(
            $"/api/v1/admin/metadata/publications/{Uri.EscapeDataString(publicationId)}/overrides",
            request,
            "PUT /api/v1/admin/metadata/publications/{publicationId}/overrides",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaAdminLayerRelationships>> GetLayerRelationshipsAsync(
        int layerId,
        CancellationToken cancellationToken = default) =>
        GetApiResponseAsync<HonuaAdminLayerRelationships>(
            $"/api/v1/admin/metadata/layers/{layerId}/relationships",
            "GET /api/v1/admin/metadata/layers/{layerId}/relationships",
            cancellationToken);

    public Task<HonuaAdminEndpointResult<HonuaAdminLayerRelationships>> UpdateLayerRelationshipsAsync(
        int layerId,
        HonuaAdminLayerRelationshipsUpdate request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return PutApiResponseAsync<HonuaAdminLayerRelationshipsUpdate, HonuaAdminLayerRelationships>(
            $"/api/v1/admin/metadata/layers/{layerId}/relationships",
            request,
            "PUT /api/v1/admin/metadata/layers/{layerId}/relationships",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaAdminDiscoveryMetadata>> GetLayerDiscoveryAsync(
        int layerId,
        CancellationToken cancellationToken = default) =>
        GetApiResponseAsync<HonuaAdminDiscoveryMetadata>(
            $"/api/v1/admin/metadata/layers/{layerId}/discovery",
            "GET /api/v1/admin/metadata/layers/{layerId}/discovery",
            cancellationToken);

    public Task<HonuaAdminEndpointResult<HonuaAdminDiscoveryMetadata>> UpdateLayerDiscoveryAsync(
        int layerId,
        HonuaAdminDiscoveryMetadataUpdate request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return PutApiResponseAsync<HonuaAdminDiscoveryMetadataUpdate, HonuaAdminDiscoveryMetadata>(
            $"/api/v1/admin/metadata/layers/{layerId}/discovery",
            request,
            "PUT /api/v1/admin/metadata/layers/{layerId}/discovery",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaAdminDiscoveryMetadata>> GetServiceDiscoveryAsync(
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        return GetApiResponseAsync<HonuaAdminDiscoveryMetadata>(
            $"/api/v1/admin/services/{Uri.EscapeDataString(serviceName)}/discovery",
            "GET /api/v1/admin/services/{serviceName}/discovery",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaAdminDiscoveryMetadata>> UpdateServiceDiscoveryAsync(
        string serviceName,
        HonuaAdminDiscoveryMetadataUpdate request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentNullException.ThrowIfNull(request);

        return PutApiResponseAsync<HonuaAdminDiscoveryMetadataUpdate, HonuaAdminDiscoveryMetadata>(
            $"/api/v1/admin/services/{Uri.EscapeDataString(serviceName)}/discovery",
            request,
            "PUT /api/v1/admin/services/{serviceName}/discovery",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaAdminLayerFields>> GetLayerFieldsAsync(
        int layerId,
        CancellationToken cancellationToken = default) =>
        GetApiResponseAsync<HonuaAdminLayerFields>(
            $"/api/v1/admin/metadata/layers/{layerId}/fields",
            "GET /api/v1/admin/metadata/layers/{layerId}/fields",
            cancellationToken);

    public Task<HonuaAdminEndpointResult<HonuaAdminLayerFields>> UpdateLayerFieldsAsync(
        int layerId,
        HonuaAdminLayerFieldsUpdate request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return PutApiResponseAsync<HonuaAdminLayerFieldsUpdate, HonuaAdminLayerFields>(
            $"/api/v1/admin/metadata/layers/{layerId}/fields",
            request,
            "PUT /api/v1/admin/metadata/layers/{layerId}/fields",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaAdminLayerDisplay>> GetLayerDisplayAsync(
        int layerId,
        CancellationToken cancellationToken = default) =>
        GetApiResponseAsync<HonuaAdminLayerDisplay>(
            $"/api/v1/admin/metadata/layers/{layerId}/display",
            "GET /api/v1/admin/metadata/layers/{layerId}/display",
            cancellationToken);

    public Task<HonuaAdminEndpointResult<HonuaAdminLayerDisplay>> UpdateLayerDisplayAsync(
        int layerId,
        HonuaAdminLayerDisplayUpdate request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return PutApiResponseAsync<HonuaAdminLayerDisplayUpdate, HonuaAdminLayerDisplay>(
            $"/api/v1/admin/metadata/layers/{layerId}/display",
            request,
            "PUT /api/v1/admin/metadata/layers/{layerId}/display",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaAdminLayerEditing>> GetLayerEditingAsync(
        int layerId,
        CancellationToken cancellationToken = default) =>
        GetApiResponseAsync<HonuaAdminLayerEditing>(
            $"/api/v1/admin/metadata/layers/{layerId}/editing",
            "GET /api/v1/admin/metadata/layers/{layerId}/editing",
            cancellationToken);

    public Task<HonuaAdminEndpointResult<HonuaAdminLayerEditing>> UpdateLayerEditingAsync(
        int layerId,
        HonuaAdminLayerEditingUpdate request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return PutApiResponseAsync<HonuaAdminLayerEditingUpdate, HonuaAdminLayerEditing>(
            $"/api/v1/admin/metadata/layers/{layerId}/editing",
            request,
            "PUT /api/v1/admin/metadata/layers/{layerId}/editing",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaAdminLayerSpatial>> GetLayerSpatialAsync(
        int layerId,
        CancellationToken cancellationToken = default) =>
        GetApiResponseAsync<HonuaAdminLayerSpatial>(
            $"/api/v1/admin/metadata/layers/{layerId}/spatial",
            "GET /api/v1/admin/metadata/layers/{layerId}/spatial",
            cancellationToken);

    public Task<HonuaAdminEndpointResult<HonuaAdminLayerSpatial>> UpdateLayerSpatialAsync(
        int layerId,
        HonuaAdminLayerSpatialUpdate request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return PutApiResponseAsync<HonuaAdminLayerSpatialUpdate, HonuaAdminLayerSpatial>(
            $"/api/v1/admin/metadata/layers/{layerId}/spatial",
            request,
            "PUT /api/v1/admin/metadata/layers/{layerId}/spatial",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaAdminVersionResponse>> GetVersionAsync(
        CancellationToken cancellationToken = default) =>
        GetApiResponseAsync<HonuaAdminVersionResponse>(
            "/api/v1/admin/version",
            "GET /api/v1/admin/version",
            cancellationToken);

    public Task<HonuaAdminEndpointResult<HonuaAdminCapabilitiesResponse>> GetCapabilitiesAsync(
        CancellationToken cancellationToken = default) =>
        GetApiResponseAsync<HonuaAdminCapabilitiesResponse>(
            "/api/v1/admin/capabilities",
            "GET /api/v1/admin/capabilities",
            cancellationToken);

    public Task<HonuaAdminEndpointResult<HonuaAdminLicenseStatusResponse>> GetLicenseStatusAsync(
        CancellationToken cancellationToken = default) =>
        GetApiResponseAsync<HonuaAdminLicenseStatusResponse>(
            "/api/v1/admin/license/",
            "GET /api/v1/admin/license",
            cancellationToken);

    public Task<HonuaAdminEndpointResult<HonuaAdminApiKeyResponse[]>> ListApiKeysAsync(
        CancellationToken cancellationToken = default) =>
        GetApiResponseAsync<HonuaAdminApiKeyResponse[]>(
            "/api/v1/admin/api-keys/",
            "GET /api/v1/admin/api-keys",
            cancellationToken);

    public Task<HonuaAdminEndpointResult<HonuaAdminOidcProviderResponse[]>> ListOidcProvidersAsync(
        CancellationToken cancellationToken = default) =>
        GetApiResponseAsync<HonuaAdminOidcProviderResponse[]>(
            "/api/v1/admin/oidc/providers/",
            "GET /api/v1/admin/oidc/providers",
            cancellationToken);

    public async Task<HonuaAdminEndpointResult<bool>> ProbeEndpointAsync(
        string contract,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativePath);
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", _apiKey);
        }

        try
        {
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? HonuaAdminEndpointResult<bool>.FromData(true)
                : HonuaAdminEndpointResult<bool>.FromIssue(CreateIssue(contract, response.StatusCode));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return HonuaAdminEndpointResult<bool>.FromIssue(new HonuaAdminEndpointIssue(
                "Unavailable", contract, $"The Honua server endpoint could not be reached: {ex.Message}"));
        }
    }

    public void Dispose() => _httpClient.Dispose();

    private async Task<HonuaAdminEndpointResult<T>> GetApiResponseAsync<T>(
        string path,
        string contract,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", _apiKey);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return HonuaAdminEndpointResult<T>.FromIssue(new HonuaAdminEndpointIssue(
                "Unavailable",
                contract,
                $"The Honua server endpoint could not be reached: {ex.Message}"));
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return HonuaAdminEndpointResult<T>.FromIssue(CreateIssue(contract, response.StatusCode));
            }

            HonuaAdminApiResponse<T>? envelope;
            try
            {
                envelope = await response.Content
                    .ReadFromJsonAsync<HonuaAdminApiResponse<T>>(JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                return HonuaAdminEndpointResult<T>.FromIssue(new HonuaAdminEndpointIssue(
                    "Unsupported",
                    contract,
                    $"The Honua server response did not match the expected admin API shape: {ex.Message}",
                    (int)response.StatusCode));
            }

            if (envelope?.Success == true && envelope.Data is not null)
            {
                return HonuaAdminEndpointResult<T>.FromData(envelope.Data);
            }

            return HonuaAdminEndpointResult<T>.FromIssue(new HonuaAdminEndpointIssue(
                "Unavailable",
                contract,
                envelope?.Message ?? "The Honua server response did not include data.",
                (int)response.StatusCode));
        }
    }

    // No-body POST for action endpoints whose input is entirely in the route (e.g. test-by-id). Reuses the
    // same envelope/field-error handling as the bodied overload via a shared core.
    private Task<HonuaAdminEndpointResult<TResponse>> PostApiResponseAsync<TResponse>(
        string path,
        string contract,
        CancellationToken cancellationToken) =>
        SendApiResponseAsync<TResponse>(() => new HttpRequestMessage(HttpMethod.Post, path), contract, cancellationToken);

    private Task<HonuaAdminEndpointResult<TResponse>> PostApiResponseAsync<TRequest, TResponse>(
        string path,
        TRequest body,
        string contract,
        CancellationToken cancellationToken) =>
        SendApiResponseAsync<TResponse>(
            () => new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = JsonContent.Create(body, options: JsonOptions)
            },
            contract,
            cancellationToken);

    private async Task<HonuaAdminEndpointResult<TResponse>> SendApiResponseAsync<TResponse>(
        Func<HttpRequestMessage> requestFactory,
        string contract,
        CancellationToken cancellationToken)
    {
        using var request = requestFactory();
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", _apiKey);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return HonuaAdminEndpointResult<TResponse>.FromIssue(new HonuaAdminEndpointIssue(
                "Unavailable",
                contract,
                $"The Honua server endpoint could not be reached: {ex.Message}"));
        }

        using (response)
        {
            // Read the body once so a rejection can surface its field-addressable validation errors,
            // and a success can surface its data envelope — without re-reading the stream.
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var issue = CreateIssue(contract, response.StatusCode);
                // The layer-publish endpoint currently returns a flat ApiResponse failure (a `message`
                // field, no errors[]); surface that actionable server reason instead of the generic
                // status text so validation/conflict rejections reach the operator verbatim.
                var serverMessage = ParseFailureMessage(payload);
                return HonuaAdminEndpointResult<TResponse>.FromIssue(issue with
                {
                    Detail = string.IsNullOrWhiteSpace(serverMessage) ? issue.Detail : serverMessage,
                    FieldErrors = ParseFieldErrors(payload)
                });
            }

            HonuaAdminApiResponse<TResponse>? envelope;
            try
            {
                envelope = string.IsNullOrWhiteSpace(payload)
                    ? null
                    : JsonSerializer.Deserialize<HonuaAdminApiResponse<TResponse>>(payload, JsonOptions);
            }
            catch (JsonException ex)
            {
                return HonuaAdminEndpointResult<TResponse>.FromIssue(new HonuaAdminEndpointIssue(
                    "Unsupported",
                    contract,
                    $"The Honua server response did not match the expected admin API shape: {ex.Message}",
                    (int)response.StatusCode));
            }

            if (envelope?.Success == true && envelope.Data is not null)
            {
                return HonuaAdminEndpointResult<TResponse>.FromData(envelope.Data);
            }

            return HonuaAdminEndpointResult<TResponse>.FromIssue(new HonuaAdminEndpointIssue(
                "Unavailable",
                contract,
                envelope?.Message ?? "The Honua server response did not include data.",
                (int)response.StatusCode));
        }
    }

    private async Task<HonuaAdminEndpointResult<TResponse>> PutApiResponseAsync<TRequest, TResponse>(
        string path,
        TRequest body,
        string contract,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, path)
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", _apiKey);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return HonuaAdminEndpointResult<TResponse>.FromIssue(new HonuaAdminEndpointIssue(
                "Unavailable",
                contract,
                $"The Honua server endpoint could not be reached: {ex.Message}"));
        }

        using (response)
        {
            // Read once so a rejection surfaces its field-addressable validation errors and a success
            // surfaces its data envelope — without re-reading the stream (mirrors the POST path).
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var issue = CreateIssue(contract, response.StatusCode);
                var serverMessage = ParseFailureMessage(payload);
                return HonuaAdminEndpointResult<TResponse>.FromIssue(issue with
                {
                    Detail = string.IsNullOrWhiteSpace(serverMessage) ? issue.Detail : serverMessage,
                    FieldErrors = ParseFieldErrors(payload)
                });
            }

            HonuaAdminApiResponse<TResponse>? envelope;
            try
            {
                envelope = string.IsNullOrWhiteSpace(payload)
                    ? null
                    : JsonSerializer.Deserialize<HonuaAdminApiResponse<TResponse>>(payload, JsonOptions);
            }
            catch (JsonException ex)
            {
                return HonuaAdminEndpointResult<TResponse>.FromIssue(new HonuaAdminEndpointIssue(
                    "Unsupported",
                    contract,
                    $"The Honua server response did not match the expected admin API shape: {ex.Message}",
                    (int)response.StatusCode));
            }

            if (envelope?.Success == true && envelope.Data is not null)
            {
                return HonuaAdminEndpointResult<TResponse>.FromData(envelope.Data);
            }

            return HonuaAdminEndpointResult<TResponse>.FromIssue(new HonuaAdminEndpointIssue(
                "Unavailable",
                contract,
                envelope?.Message ?? "The Honua server response did not include data.",
                (int)response.StatusCode));
        }
    }

    // Parse the shared RFC-7807 ProblemDetails errors[] extension (the honua-server FieldValidationError
    // projection) when present so the console can bind field-level rejections onto the offending inputs.
    // Layer-publish rejections currently return a flat ApiResponse failure message; this stays defensive so
    // it lights up the moment the endpoint adopts the field-addressable contract (task #70).
    private static IReadOnlyList<HonuaFieldValidationError> ParseFieldErrors(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("errors", out var errors)
                || errors.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var parsed = new List<HonuaFieldValidationError>();
            foreach (var error in errors.EnumerateArray())
            {
                if (error.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var item = error.Deserialize<HonuaFieldValidationError>(JsonOptions);
                if (item is not null && !string.IsNullOrWhiteSpace(item.Message))
                {
                    parsed.Add(item);
                }
            }

            return parsed;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // Extracts the server's actionable rejection text from a flat ApiResponse failure body
    // ({ success:false, message:"..." }) or an RFC-7807 ProblemDetails ({ title/detail }), so the wizard
    // surfaces the real reason rather than a generic status string. Returns null when no message is present.
    private static string? ParseFailureMessage(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var property in new[] { "message", "detail", "title" })
            {
                if (document.RootElement.TryGetProperty(property, out var value)
                    && value.ValueKind == JsonValueKind.String
                    && value.GetString() is { Length: > 0 } message)
                {
                    return message;
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static HonuaAdminEndpointIssue CreateIssue(string contract, HttpStatusCode statusCode)
    {
        var state = statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Missing permission",
            HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented => "Unsupported",
            _ => "Unavailable"
        };

        var detail = statusCode switch
        {
            HttpStatusCode.Unauthorized => "The Honua server rejected the request because admin authentication is missing.",
            HttpStatusCode.Forbidden => "The Honua server rejected the request because the current principal lacks admin permission.",
            HttpStatusCode.NotFound => "The Honua server does not expose this admin API contract.",
            HttpStatusCode.MethodNotAllowed => "The Honua server exposes the route but not the required admin API verb.",
            HttpStatusCode.NotImplemented => "The Honua server reports this admin capability is not implemented.",
            _ => string.Format(
                CultureInfo.InvariantCulture,
                "The Honua server returned HTTP {0} ({1}).",
                (int)statusCode,
                statusCode)
        };

        return new HonuaAdminEndpointIssue(state, contract, detail, (int)statusCode);
    }
}

public sealed record HonuaAdminEndpointResult<T>(T? Data, HonuaAdminEndpointIssue? Issue)
{
    public static HonuaAdminEndpointResult<T> FromData(T data) => new(data, null);

    public static HonuaAdminEndpointResult<T> FromIssue(HonuaAdminEndpointIssue issue) => new(default, issue);
}

public sealed record HonuaAdminEndpointIssue(
    string State,
    string Contract,
    string Detail,
    int? StatusCode = null)
{
    /// <summary>
    /// Field-addressable validation errors parsed from an RFC-7807 ProblemDetails <c>errors[]</c> extension
    /// when the server rejected the request with the shared field-level validation contract (the
    /// honua-server Wave-0 <c>FieldValidationError</c>). Empty for non-validation issues (transport,
    /// auth, conflict) and for flat rejections that carried no <c>errors[]</c>. Console clients bind these
    /// onto the offending inputs via the Wave-0 <c>ServerFieldErrorMapper</c>.
    /// </summary>
    public IReadOnlyList<HonuaFieldValidationError> FieldErrors { get; init; } = [];
}

/// <summary>
/// Wire shape of one RFC-7807 ProblemDetails <c>errors[]</c> item — the honua-server shared
/// <c>FieldValidationError</c> projection (<c>{code,severity,path,message,fieldId}</c>). Parsed by HTTP
/// clients from a field-addressable rejection body and surfaced on <see cref="HonuaAdminEndpointIssue"/>.
/// </summary>
public sealed record HonuaFieldValidationError
{
    [System.Text.Json.Serialization.JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("severity")]
    public string? Severity { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("path")]
    public string? Path { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("fieldId")]
    public string? FieldId { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}

public sealed record HonuaAdminApiResponse<T>(
    bool Success,
    T? Data,
    string? Message,
    DateTimeOffset? Timestamp);

/// <summary>
/// Wire shape of the honua-server connection-create request body
/// (<c>POST /api/v1/admin/connections/</c>, mirrors <c>CreateConnectionRequest</c>). Carries the connection
/// identity, PostGIS target (host/port/database), credentials, provider, and SSL posture. The secret
/// (<see cref="Password"/>) is sent to the server secret store and never echoed back in the summary.
/// </summary>
public sealed record HonuaAdminCreateConnectionRequest
{
    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Optional with a secret reference (display metadata only); required with an inline password.</summary>
    public string? Host { get; init; }

    public int Port { get; init; } = 5432;

    public string? DatabaseName { get; init; }

    public string? Username { get; init; }

    /// <summary>Inline password. Mutually exclusive with <see cref="SecretReference"/>.</summary>
    public string? Password { get; init; }

    /// <summary>
    /// External secret reference holding the full connection string (e.g. <c>env:PROD_DB_DSN</c>,
    /// <c>aws:secretsmanager:prod-db-creds</c>). Mutually exclusive with <see cref="Password"/>.
    /// </summary>
    public string? SecretReference { get; init; }

    /// <summary>Secret store kind (env, aws, azure) — required when <see cref="SecretReference"/> is set.</summary>
    public string? SecretType { get; init; }

    public string Provider { get; init; } = "postgis";

    public bool SslRequired { get; init; }

    public string SslMode { get; init; } = "Disable";
}

/// <summary>
/// Wire shape of the honua-server connection-test response (<c>ConnectionTestResult</c>), returned by both the
/// draft test (<c>POST /api/v1/admin/connections/test</c>) and the existing-connection test
/// (<c>POST /api/v1/admin/connections/{id}/test</c>). <see cref="ConnectionId"/> is <c>Guid.Empty</c> for a draft test.
/// </summary>
public sealed record HonuaAdminConnectionTestResult
{
    public Guid ConnectionId { get; init; }

    public string? ConnectionName { get; init; }

    public bool IsHealthy { get; init; }

    public DateTimeOffset? TestedAt { get; init; }

    public string? Message { get; init; }
}

/// <summary>Wire shape of the honua-server table-discovery response (<c>GET /connections/{id}/tables</c>, Issue #57).</summary>
internal sealed record HonuaAdminTableDiscoveryBody
{
    public HonuaAdminTableInfo[]? Tables { get; init; }
}

/// <summary>A publishable (PostGIS spatial) table discovered on a connection.</summary>
public sealed record HonuaAdminTableInfo
{
    public string? Schema { get; init; }

    public string? Table { get; init; }

    public string? GeometryColumn { get; init; }

    public string? GeometryType { get; init; }

    public int? Srid { get; init; }

    public long? EstimatedRows { get; init; }

    public IReadOnlyList<HonuaAdminColumnInfo> Columns { get; init; } = [];
}

/// <summary>A column within a discovered table.</summary>
public sealed record HonuaAdminColumnInfo
{
    public string? Name { get; init; }

    public string? DataType { get; init; }
}

/// <summary>Supported geospatial import formats (<c>GET /api/v1/admin/import/formats</c>).</summary>
public sealed record HonuaAdminImportFormats
{
    public IReadOnlyList<string> SupportedExtensions { get; init; } = [];

    public IReadOnlyDictionary<string, string> FormatDescriptions { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Wire shape of the external-service discovery request body.</summary>
internal sealed record ExternalServiceDiscoverBody(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("credentials")] HonuaAdminExternalServiceCredentials? Credentials = null);

/// <summary>
/// Credentials supplied to authenticate against a protected external service or catalog. Secrets are used
/// only for the discovery request and are never stored by the console.
/// </summary>
public sealed record HonuaAdminExternalServiceCredentials
{
    /// <summary>arcgis-token, token, basic, or oauth.</summary>
    public string? Mode { get; init; }

    public string? Username { get; init; }

    public string? Password { get; init; }

    public string? Token { get; init; }

    public string? TokenUrl { get; init; }

    public string? ClientId { get; init; }

    public string? ClientSecret { get; init; }

    public string? Referer { get; init; }
}

/// <summary>Result of discovering a remote Esri/OGC service (<c>POST /api/v1/admin/external-services/discover</c>).</summary>
public sealed record HonuaAdminExternalServiceDiscovery
{
    public string? SourceUrl { get; init; }

    public string? NormalizedUrl { get; init; }

    public string? SourceKind { get; init; }

    public string? ServiceType { get; init; }

    public string? ServiceName { get; init; }

    public string? Description { get; init; }

    public int? Srid { get; init; }

    /// <summary>True when the URL was an ArcGIS catalog root/folder enumerated into multiple services.</summary>
    public bool IsCatalog { get; init; }

    public IReadOnlyList<HonuaAdminExternalLayerCandidate> Candidates { get; init; } = [];

    /// <summary>Discovered services (one for a single service URL; many for a catalog), grouped by folder.</summary>
    public IReadOnlyList<HonuaAdminExternalServiceSummary> Services { get; init; } = [];
}

/// <summary>A layer's persisted field configuration (<c>GET /api/v1/admin/metadata/layers/{id}/fields</c>).</summary>
public sealed record HonuaAdminLayerFields
{
    public int LayerId { get; init; }

    public IReadOnlyList<HonuaAdminLayerField> Fields { get; init; } = [];
}

/// <summary>One field's persisted configuration: type, alias, domain, visibility, default value.</summary>
public sealed record HonuaAdminLayerField
{
    public string? Name { get; init; }

    public string? Type { get; init; }

    public string? Alias { get; init; }

    public HonuaAdminFieldDomain? Domain { get; init; }

    public bool Hidden { get; init; }

    /// <summary>
    /// The field's persisted default value (any JSON scalar) as emitted by the server's field metadata, or
    /// null when the field has no default. Round-tripped so the editor reflects the persisted default.
    /// </summary>
    public JsonElement? DefaultValue { get; init; }
}

/// <summary>
/// A domain on a field. <see cref="Type"/> is <c>"codedValue"</c> (carry <see cref="CodedValues"/>) or
/// <c>"range"</c> (carry <see cref="Range"/> as a two-element [min,max] array). The optional
/// <see cref="MergePolicy"/>/<see cref="SplitPolicy"/> are Esri policy tokens
/// (e.g. <c>esriMPTDefaultValue</c>, <c>esriSPTDuplicate</c>).
/// </summary>
public sealed record HonuaAdminFieldDomain
{
    public string? Name { get; init; }

    /// <summary>"codedValue" or "range".</summary>
    public string? Type { get; init; }

    public IReadOnlyList<HonuaAdminCodedValue> CodedValues { get; init; } = [];

    /// <summary>Two-element <c>[min, max]</c> bound for a range domain (min ≤ max); null for coded-value.</summary>
    public IReadOnlyList<double>? Range { get; init; }

    /// <summary>Esri merge-policy token (e.g. <c>esriMPTDefaultValue</c>); null leaves the server default.</summary>
    public string? MergePolicy { get; init; }

    /// <summary>Esri split-policy token (e.g. <c>esriSPTDuplicate</c>); null leaves the server default.</summary>
    public string? SplitPolicy { get; init; }
}

/// <summary>A single code/label pair in a coded-value domain.</summary>
public sealed record HonuaAdminCodedValue
{
    public string? Code { get; init; }

    public string? Name { get; init; }
}

/// <summary>Request body for <c>PUT /api/v1/admin/metadata/layers/{id}/fields</c>.</summary>
public sealed record HonuaAdminLayerFieldsUpdate
{
    public IReadOnlyList<HonuaAdminLayerFieldUpdate> Fields { get; init; } = [];
}

/// <summary>
/// A single field update: set/clear the domain (coded-value or range) and optionally alias/hidden/default.
/// </summary>
public sealed record HonuaAdminLayerFieldUpdate
{
    public required string Name { get; init; }

    public string? Alias { get; init; }

    /// <summary>Set the domain (coded-value or range); null leaves the field's existing domain untouched.</summary>
    public HonuaAdminFieldDomain? Domain { get; init; }

    public bool? Hidden { get; init; }

    /// <summary>
    /// Set the field's default value (any JSON scalar). A JSON <c>null</c> (a <see cref="JsonElement"/> whose
    /// <see cref="JsonElement.ValueKind"/> is <see cref="JsonValueKind.Null"/>) clears the default; a C# null
    /// (the property left unset) leaves the existing default untouched. Serialized only when set so an
    /// alias/hidden/domain-only update never disturbs the persisted default.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? DefaultValue { get; init; }
}

/// <summary>
/// A layer's persisted relationships (<c>GET /api/v1/admin/metadata/layers/{id}/relationships</c>). The
/// server emits these as <c>relationships[]</c> on FeatureServer layer metadata.
/// </summary>
public sealed record HonuaAdminLayerRelationships
{
    public int LayerId { get; init; }

    public IReadOnlyList<HonuaAdminLayerRelationship> Relationships { get; init; } = [];
}

/// <summary>
/// One layer relationship: identity, the related layer, origin/destination role + cardinality, the join
/// fields, and the Esri relationship id. Mirrors <c>MetadataV2Relationship</c>.
/// </summary>
public sealed record HonuaAdminLayerRelationship
{
    public string? Id { get; init; }

    public string? Name { get; init; }

    public int? RelatedLayerId { get; init; }

    /// <summary>"origin" or "destination".</summary>
    public string? Role { get; init; }

    /// <summary>e.g. "one-to-many".</summary>
    public string? Cardinality { get; init; }

    public string? OriginField { get; init; }

    public string? DestinationField { get; init; }

    public int? EsriRelationshipId { get; init; }
}

/// <summary>Request body for <c>PUT /api/v1/admin/metadata/layers/{id}/relationships</c> (replaces the set).</summary>
public sealed record HonuaAdminLayerRelationshipsUpdate
{
    public IReadOnlyList<HonuaAdminLayerRelationship> Relationships { get; init; } = [];
}

/// <summary>
/// A layer's persisted display hints (<c>GET /api/v1/admin/metadata/layers/{id}/display</c>): scale-dependent
/// visibility window, default visibility, the display (label) field, queryable, and the hasZ/hasM geometry
/// dimensionality flags.
/// </summary>
public sealed record HonuaAdminLayerDisplay
{
    public int LayerId { get; init; }

    public double? MinScale { get; init; }

    public double? MaxScale { get; init; }

    public bool? DefaultVisibility { get; init; }

    public string? DisplayField { get; init; }

    public bool? Queryable { get; init; }

    public bool? HasZ { get; init; }

    public bool? HasM { get; init; }
}

/// <summary>
/// Request body for <c>PUT /api/v1/admin/metadata/layers/{id}/display</c>. Every field is nullable; a
/// null/omitted field leaves the corresponding server value unchanged.
/// </summary>
public sealed record HonuaAdminLayerDisplayUpdate
{
    public double? MinScale { get; init; }

    public double? MaxScale { get; init; }

    public bool? DefaultVisibility { get; init; }

    public string? DisplayField { get; init; }

    public bool? Queryable { get; init; }

    public bool? HasZ { get; init; }

    public bool? HasM { get; init; }
}

/// <summary>
/// A layer's persisted editor-tracking + edit-capability metadata
/// (<c>GET /api/v1/admin/metadata/layers/{id}/editing</c>): the global-id / creator / created-at / editor /
/// updated-at field names, whether features can be modified, and attachment / related-record support.
/// </summary>
public sealed record HonuaAdminLayerEditing
{
    public int LayerId { get; init; }

    public string? GlobalIdField { get; init; }

    public string? CreatorField { get; init; }

    public string? CreatedAtField { get; init; }

    public string? EditorField { get; init; }

    public string? UpdatedAtField { get; init; }

    public bool? CanModify { get; init; }

    public bool? SupportsAttachments { get; init; }

    public bool? SupportsRelatedRecords { get; init; }
}

/// <summary>
/// Request body for <c>PUT /api/v1/admin/metadata/layers/{id}/editing</c>. Every field is nullable; a
/// null/omitted field leaves the corresponding server value unchanged.
/// </summary>
public sealed record HonuaAdminLayerEditingUpdate
{
    public string? GlobalIdField { get; init; }

    public string? CreatorField { get; init; }

    public string? CreatedAtField { get; init; }

    public string? EditorField { get; init; }

    public string? UpdatedAtField { get; init; }

    public bool? CanModify { get; init; }

    public bool? SupportsAttachments { get; init; }

    public bool? SupportsRelatedRecords { get; init; }
}

/// <summary>
/// A layer's persisted spatial/CRS metadata (<c>GET /api/v1/admin/metadata/layers/{id}/spatial</c>): the
/// advertised supported-CRS list, the storage CRS, and its coordinate epoch. The stored SRID/geometry are
/// reported here for context but are not authored by the matching PUT.
/// </summary>
public sealed record HonuaAdminLayerSpatial
{
    public int LayerId { get; init; }

    /// <summary>Stored SRID (read-only context; not authored by the spatial PUT).</summary>
    public int? Srid { get; init; }

    /// <summary>Geometry type (read-only context; not authored by the spatial PUT).</summary>
    public string? GeometryType { get; init; }

    public IReadOnlyList<string> SupportedCrs { get; init; } = [];

    public string? StorageCrs { get; init; }

    public double? StorageCrsCoordinateEpoch { get; init; }
}

/// <summary>
/// Request body for <c>PUT /api/v1/admin/metadata/layers/{id}/spatial</c>. Only the CRS-list/output fields are
/// written (the stored SRID/geometry are untouched). For <see cref="SupportedCrs"/>: omit (null) = unchanged,
/// <c>[]</c> = clear. The scalar output fields are cleared via the explicit clear flags rather than by sending
/// null, so a present-but-null scalar is unambiguous.
/// </summary>
public sealed record HonuaAdminLayerSpatialUpdate
{
    /// <summary>Omit (null) leaves the list unchanged; <c>[]</c> clears it. Not serialized when null.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? SupportedCrs { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StorageCrs { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? StorageCrsCoordinateEpoch { get; init; }

    /// <summary>When true, clears the storage CRS server-side.</summary>
    public bool ClearStorageCrs { get; init; }

    /// <summary>When true, clears the storage-CRS coordinate epoch server-side.</summary>
    public bool ClearStorageCrsCoordinateEpoch { get; init; }
}

/// <summary>
/// A layer's or service's discovery / catalog metadata as read from honua-server
/// (<c>GET /api/v1/admin/metadata/layers/{id}/discovery</c> and
/// <c>GET /api/v1/admin/services/{svc}/discovery</c>). Drives the OGC API Records / STAC / DCAT / Esri
/// documentInfo output. Title/description/license/attribution/publisher/language/contactPoint are scalar;
/// keywords/themes/links are lists.
/// </summary>
public sealed record HonuaAdminDiscoveryMetadata
{
    public string? Title { get; init; }

    public string? Description { get; init; }

    public IReadOnlyList<string> Keywords { get; init; } = [];

    public IReadOnlyList<string> Themes { get; init; } = [];

    public string? Language { get; init; }

    public string? License { get; init; }

    public string? Attribution { get; init; }

    public string? Publisher { get; init; }

    public HonuaAdminDiscoveryContactPoint? ContactPoint { get; init; }

    public IReadOnlyList<HonuaAdminDiscoveryLink> Links { get; init; } = [];
}

/// <summary>Discovery contact point (DCAT <c>contactPoint</c> / OGC Records <c>contacts</c>).</summary>
public sealed record HonuaAdminDiscoveryContactPoint
{
    public string? Name { get; init; }

    public string? Email { get; init; }

    public string? Url { get; init; }
}

/// <summary>One discovery link (OGC <c>links[]</c> / STAC link). <c>Href</c>+<c>Rel</c> are the load-bearing fields.</summary>
public sealed record HonuaAdminDiscoveryLink
{
    public string? Href { get; init; }

    public string? Rel { get; init; }

    public string? Type { get; init; }

    public string? Title { get; init; }

    public string? Hreflang { get; init; }
}

/// <summary>
/// Request body for the discovery PUT endpoints (layer + service). A null scalar leaves that field unchanged
/// server-side; a non-null list replaces (an empty list clears). Mirrors <see cref="HonuaAdminDiscoveryMetadata"/>
/// but keeps the lists nullable so an omitted list is "unchanged" rather than "clear".
/// </summary>
public sealed record HonuaAdminDiscoveryMetadataUpdate
{
    public string? Title { get; init; }

    public string? Description { get; init; }

    public IReadOnlyList<string>? Keywords { get; init; }

    public IReadOnlyList<string>? Themes { get; init; }

    public string? Language { get; init; }

    public string? License { get; init; }

    public string? Attribution { get; init; }

    public string? Publisher { get; init; }

    public HonuaAdminDiscoveryContactPoint? ContactPoint { get; init; }

    public IReadOnlyList<HonuaAdminDiscoveryLink>? Links { get; init; }
}

/// <summary>
/// Wire shape of the honua-server time-info update request body
/// (<c>PUT /api/v1/admin/services/{serviceName}/timeinfo</c>, mirrors <c>UpdateTimeInfoRequest</c>). A null
/// field clears the corresponding time field server-side.
/// </summary>
public sealed record HonuaAdminUpdateTimeInfoRequest
{
    public string? StartTimeField { get; init; }

    public string? EndTimeField { get; init; }

    public string? TrackIdField { get; init; }
}

/// <summary>Request body for <c>POST /api/v1/admin/import/geoservices/start</c>.</summary>
public sealed record HonuaAdminGeoservicesImportRequest
{
    public required string ServiceUrl { get; init; }

    public int LayerId { get; init; }

    public required string TableName { get; init; }

    public string? TargetSchema { get; init; }

    public int? TargetSrid { get; init; }

    public bool? OverwriteExisting { get; init; }

    public bool? AutoPublish { get; init; }

    public string? ServiceName { get; init; }

    public HonuaAdminGeoservicesImportCredentials? Credentials { get; init; }
}

/// <summary>ArcGIS import credentials (server-side queued imports may require secret references).</summary>
public sealed record HonuaAdminGeoservicesImportCredentials
{
    public string? Mode { get; init; }

    public string? AccessToken { get; init; }

    public string? Username { get; init; }

    public string? Password { get; init; }
}

/// <summary>Response (HTTP 202) when a GeoServices import job is queued.</summary>
public sealed record HonuaAdminGeoservicesImportJob
{
    public string? JobId { get; init; }

    public string? Message { get; init; }

    public string? StatusUrl { get; init; }

    public string? CancelUrl { get; init; }
}

/// <summary>Progress of a queued GeoServices import job (<c>GET .../jobs/{jobId}</c>).</summary>
public sealed record HonuaAdminGeoservicesImportProgress
{
    public string? JobId { get; init; }

    /// <summary>Numeric GeoservicesImportStatus enum: 0 Queued, 1 Discovering, 2 RetrievingFeatures,
    /// 3 CreatingTable, 4 InsertingFeatures, 5 Publishing, 6 Completed, 7 Failed, 8 Cancelled.</summary>
    [JsonPropertyName("status")]
    public int? StatusCode { get; init; }

    public int FeaturesProcessed { get; init; }

    public int? EstimatedTotalFeatures { get; init; }

    public double? PercentComplete { get; init; }

    public string? TableName { get; init; }

    public string? ServiceName { get; init; }

    public int? PublishedLayerId { get; init; }

    public string? ErrorMessage { get; init; }

    public string? CurrentPhase { get; init; }
}

/// <summary>A single service discovered within a catalog (or the sole service for a single-service URL).</summary>
public sealed record HonuaAdminExternalServiceSummary
{
    public string? SourceKind { get; init; }

    public string? ServiceName { get; init; }

    public string? ServiceType { get; init; }

    public string? ServiceUrl { get; init; }

    public string? FolderPath { get; init; }

    public int? Srid { get; init; }

    public IReadOnlyList<HonuaAdminExternalLayerCandidate> Candidates { get; init; } = [];
}

/// <summary>A candidate layer discovered on a remote service.</summary>
public sealed record HonuaAdminExternalLayerCandidate
{
    public int? LayerId { get; init; }

    public string? Name { get; init; }

    public string? GeometryType { get; init; }

    public int? FeatureCount { get; init; }

    public string? Description { get; init; }

    public string? ServiceUrl { get; init; }
}

/// <summary>Result of a geospatial file import (<c>POST /api/v1/admin/import/upload</c>).</summary>
public sealed record HonuaAdminImportResult
{
    public bool Success { get; init; }

    public long FeatureCount { get; init; }

    public string? TableName { get; init; }

    public string? Format { get; init; }

    public string? ErrorMessage { get; init; }

    public IReadOnlyList<string> ValidationErrors { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record HonuaAdminConnectionSummary
{
    public Guid ConnectionId { get; init; }

    public string? Name { get; init; }

    public string? Description { get; init; }

    public string? Host { get; init; }

    public int? Port { get; init; }

    public string? DatabaseName { get; init; }

    public string? Username { get; init; }

    public string? Provider { get; init; }

    public bool? SslRequired { get; init; }

    public string? SslMode { get; init; }

    public string? StorageType { get; init; }

    public bool? IsActive { get; init; }

    public string? HealthStatus { get; init; }

    public DateTimeOffset? LastHealthCheck { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    public string? CreatedBy { get; init; }
}

/// <summary>
/// Wire shape of the honua-server layer-publish request body
/// (<c>POST /api/v1/admin/connections/{id}/layers</c>, mirrors <c>PublishLayerRequest</c>). Carries the
/// source table, layer identity, geometry/SRID/primary-key, selected fields, and target service name.
/// </summary>
public sealed record HonuaAdminPublishLayerRequest
{
    public required string Schema { get; init; }

    public required string Table { get; init; }

    public required string LayerName { get; init; }

    public string? Description { get; init; }

    public string? GeometryColumn { get; init; }

    public string? GeometryType { get; init; }

    public int? Srid { get; init; }

    public string? PrimaryKey { get; init; }

    public IReadOnlyList<string> Fields { get; init; } = [];

    public string? ServiceName { get; init; }

    public bool Enabled { get; init; } = true;
}

/// <summary>
/// Wire shape of the honua-server layer enable/disable request body
/// (<c>PUT /api/v1/admin/connections/{id}/layers/{layerId}/enabled</c>, mirrors <c>LayerEnabledRequest</c>).
/// </summary>
internal sealed record LayerEnabledBody(
    [property: JsonPropertyName("enabled")] bool Enabled);

/// <summary>
/// Wire shape of the honua-server protocol-update request body
/// (<c>PUT /api/v1/admin/services/{serviceName}/protocols</c>, mirrors <c>UpdateProtocolsRequest</c>).
/// </summary>
internal sealed record UpdateProtocolsBody(
    [property: JsonPropertyName("enabledProtocols")] IReadOnlyList<string> EnabledProtocols);

/// <summary>
/// Wire shape of the honua-server access-policy update request body
/// (<c>PUT /api/v1/admin/services/{serviceName}/access-policy</c>, mirrors <c>UpdateAccessPolicyRequest</c>).
/// Null fields are left unchanged server-side.
/// </summary>
public sealed record HonuaAdminUpdateAccessPolicyRequest
{
    public bool? AllowAnonymous { get; init; }

    public bool? AllowAnonymousWrite { get; init; }

    public IReadOnlyList<string>? AllowedRoles { get; init; }

    public IReadOnlyList<string>? AllowedWriteRoles { get; init; }
}

/// <summary>
/// Wire shape of the honua-server MapServer render-settings update request body
/// (<c>PUT /api/v1/admin/services/{serviceName}/mapserver</c>, mirrors <c>UpdateMapServerSettingsRequest</c>).
/// Null fields are left unchanged server-side. Caps the MapServer export surface: max/default image size,
/// DPI, default image format, default transparency, and the per-layer feature cap.
/// </summary>
public sealed record HonuaAdminUpdateMapServerSettingsRequest
{
    public int? MaxImageWidth { get; init; }

    public int? MaxImageHeight { get; init; }

    public int? DefaultImageWidth { get; init; }

    public int? DefaultImageHeight { get; init; }

    public int? DefaultDpi { get; init; }

    public string? DefaultFormat { get; init; }

    public bool? DefaultTransparent { get; init; }

    public int? MaxFeaturesPerLayer { get; init; }
}

/// <summary>
/// Wire shape of the honua-server service settings-caps update request body
/// (<c>PUT /api/v1/admin/services/{serviceName}/settings-caps</c>, mirrors
/// <c>UpdateServiceSettingsCapsRequest</c>). Null fields are left unchanged server-side; the server rejects
/// negative caps. Caps the service's result/request surface: max/default record counts, per-layer feature
/// cap, query timeout, edit-transaction cap, payload size, supported output formats + default, default
/// tile-matrix set, and attachment support + size cap.
/// </summary>
public sealed record HonuaAdminUpdateServiceSettingsCapsRequest
{
    public int? MaxRecordCount { get; init; }

    public int? DefaultRecordCount { get; init; }

    public int? MaxFeaturesPerLayer { get; init; }

    public int? QueryTimeoutMs { get; init; }

    public int? MaxEditsPerTransaction { get; init; }

    public long? MaxPayloadBytes { get; init; }

    public IReadOnlyList<string>? SupportedFormats { get; init; }

    public string? DefaultFormat { get; init; }

    public string? DefaultTileMatrixSet { get; init; }

    public bool? SupportsAttachments { get; init; }

    public long? MaxAttachmentSizeBytes { get; init; }
}

/// <summary>
/// Wire shape of the honua-server service settings-caps projection
/// (<c>GET/PUT /api/v1/admin/services/{serviceName}/settings-caps</c>, the <c>data</c> of
/// <c>ApiResponse&lt;ServiceSettingsCapsResponse&gt;</c>). All fields nullable — the server only reports the
/// caps it actually has configured. Pre-populates the settings-caps editor and is read back after a save.
/// </summary>
public sealed record HonuaAdminServiceSettingsCapsResponse
{
    public int? MaxRecordCount { get; init; }

    public int? DefaultRecordCount { get; init; }

    public int? MaxFeaturesPerLayer { get; init; }

    public int? QueryTimeoutMs { get; init; }

    public int? MaxEditsPerTransaction { get; init; }

    public long? MaxPayloadBytes { get; init; }

    public IReadOnlyList<string> SupportedFormats { get; init; } = [];

    public string? DefaultFormat { get; init; }

    public string? DefaultTileMatrixSet { get; init; }

    public bool? SupportsAttachments { get; init; }

    public long? MaxAttachmentSizeBytes { get; init; }
}

public sealed record HonuaAdminPublishedLayerSummary
{
    public int LayerId { get; init; }

    public string? LayerName { get; init; }

    public string? Schema { get; init; }

    public string? Table { get; init; }

    public string? Description { get; init; }

    public string? GeometryType { get; init; }

    public int? Srid { get; init; }

    public string? PrimaryKey { get; init; }

    public int? FieldCount { get; init; }

    public bool? Enabled { get; init; }

    public string? ServiceName { get; init; }

    /// <summary>
    /// Layer's cached spatial extent in EPSG:4326 (lng/lat), recomputed when the layer is published or
    /// refreshed server-side. Null when the layer has no stored extent. Used to frame the map preview.
    /// </summary>
    public HonuaAdminLayerExtent? Extent { get; init; }
}

/// <summary>Axis-aligned bounding box of a layer in EPSG:4326 (longitude/latitude degrees).</summary>
public sealed record HonuaAdminLayerExtent
{
    public double MinX { get; init; }

    public double MinY { get; init; }

    public double MaxX { get; init; }

    public double MaxY { get; init; }
}

public sealed record HonuaAdminServiceSummary
{
    public string? ServiceName { get; init; }

    public string? Description { get; init; }

    public int LayerCount { get; init; }

    public IReadOnlyList<string> EnabledProtocols { get; init; } = [];
}

public sealed record HonuaAdminServiceSettingsResponse
{
    public string? ServiceName { get; init; }

    public IReadOnlyList<string> EnabledProtocols { get; init; } = [];

    public IReadOnlyList<string> AvailableProtocols { get; init; } = [];

    public HonuaAdminAccessPolicyResponse? AccessPolicy { get; init; }

    public HonuaAdminTimeInfoResponse? TimeInfo { get; init; }

    public HonuaAdminMapServerSettingsResponse? MapServer { get; init; }

    /// <summary>
    /// The service's current settings caps as read back from the server settings projection, or <c>null</c>
    /// when the server build does not include a caps block in the settings GET. Pre-populates the
    /// settings-caps editor when the dedicated caps GET is not separately queried.
    /// </summary>
    public HonuaAdminServiceSettingsCapsResponse? SettingsCaps { get; init; }
}

public sealed record HonuaAdminAccessPolicyResponse
{
    public bool? AllowAnonymous { get; init; }

    public bool? AllowAnonymousWrite { get; init; }

    public IReadOnlyList<string> AllowedRoles { get; init; } = [];

    public IReadOnlyList<string> AllowedWriteRoles { get; init; } = [];
}

public sealed record HonuaAdminTimeInfoResponse
{
    public string? StartTimeField { get; init; }

    public string? EndTimeField { get; init; }

    public string? TrackIdField { get; init; }
}

public sealed record HonuaAdminMapServerSettingsResponse
{
    public int? MaxImageWidth { get; init; }

    public int? MaxImageHeight { get; init; }

    public int? DefaultImageWidth { get; init; }

    public int? DefaultImageHeight { get; init; }

    public int? DefaultDpi { get; init; }

    public string? DefaultFormat { get; init; }

    public bool? DefaultTransparent { get; init; }

    public int? MaxFeaturesPerLayer { get; init; }
}

public sealed record HonuaAdminVersionResponse
{
    public string? Version { get; init; }

    public string? MetadataApiVersion { get; init; }

    public string? MetadataSchemaVersion { get; init; }

    public DateTimeOffset? ServerTime { get; init; }
}

public sealed record HonuaAdminCapabilitiesResponse
{
    public string? MetadataApiVersion { get; init; }

    public string? MetadataSchemaVersion { get; init; }

    public string? ServerVersion { get; init; }
}

public sealed record HonuaAdminLicenseStatusResponse
{
    public string? Edition { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public bool? IsValid { get; init; }

    public int? DaysUntilExpiry { get; init; }

    public bool? ExpiryWarning { get; init; }

    public string? ValidationState { get; init; }

    public string? LicensedTo { get; init; }

    public string? LicenseId { get; init; }

    public DateTimeOffset? IssuedAt { get; init; }

    public IReadOnlyList<HonuaAdminEntitlementResponse> Entitlements { get; init; } = [];
}

public sealed record HonuaAdminEntitlementResponse
{
    public string? Key { get; init; }

    public string? Name { get; init; }

    public bool? IsActive { get; init; }
}

public sealed record HonuaAdminApiKeyResponse
{
    public Guid Id { get; init; }

    public string? Name { get; init; }

    public string? KeyPrefix { get; init; }

    public IReadOnlyList<string> Permissions { get; init; } = [];

    public string? Status { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public DateTimeOffset? LastUsedAt { get; init; }

    public DateTimeOffset? RotatedAt { get; init; }

    public DateTimeOffset? RevokedAt { get; init; }

    public string? CreatedBy { get; init; }
}

public sealed record HonuaAdminOidcProviderResponse
{
    public Guid ProviderId { get; init; }

    public string? Name { get; init; }

    public string? ProviderType { get; init; }

    public string? Authority { get; init; }

    public string? ClientId { get; init; }

    public bool? Enabled { get; init; }

    public bool? IsHealthy { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }

    public DateTimeOffset? LastHealthCheck { get; init; }
}

/// <summary>
/// A publication's persisted overrides as read from honua-server
/// (<c>GET /api/v1/admin/metadata/publications/{publicationId}/overrides</c>). A "publication" is a layer's
/// exposure within a service; these overrides let an operator re-title it, alias its fields per-publication,
/// constrain its capabilities and supported formats, and mark it primary — without disturbing the underlying
/// layer metadata.
/// </summary>
public sealed record HonuaAdminPublicationOverrides
{
    /// <summary>The publication's metadata id (echoed by the server).</summary>
    public string? PublicationId { get; init; }

    /// <summary>Display title that overrides the layer's title for this publication; null/empty when unset.</summary>
    public string? TitleOverride { get; init; }

    /// <summary>Per-publication field aliases as a <c>{ "&lt;field&gt;": "&lt;alias&gt;" }</c> map.</summary>
    public IReadOnlyDictionary<string, string> FieldAliases { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The capabilities this publication exposes (e.g. <c>Query</c>, <c>Create</c>, <c>Update</c>).</summary>
    public IReadOnlyList<string> Capabilities { get; init; } = [];

    /// <summary>The output formats this publication supports (e.g. <c>json</c>, <c>geojson</c>, <c>pbf</c>).</summary>
    public IReadOnlyList<string> SupportedFormats { get; init; } = [];

    /// <summary>Whether this is the primary publication of its layer.</summary>
    public bool IsPrimary { get; init; }
}

/// <summary>
/// Request body for <c>PUT /api/v1/admin/metadata/publications/{publicationId}/overrides</c>. A null scalar
/// leaves that value unchanged server-side; an empty <see cref="TitleOverride"/> string clears the title; an
/// empty array/map (<c>[]</c>/<c>{}</c>) clears that list/map. Lists/map are nullable so an omitted list is
/// "unchanged" rather than "clear".
/// </summary>
public sealed record HonuaAdminPublicationOverridesUpdate
{
    public string? TitleOverride { get; init; }

    public IReadOnlyDictionary<string, string>? FieldAliases { get; init; }

    public IReadOnlyList<string>? Capabilities { get; init; }

    public IReadOnlyList<string>? SupportedFormats { get; init; }

    public bool? IsPrimary { get; init; }
}
