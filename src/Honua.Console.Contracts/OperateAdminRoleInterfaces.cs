using System.Text.Json;

namespace Honua.Console.Contracts;

// Role-based decomposition of the IHonuaAdminOperateClient god interface (honua-console#279 PA-242).
// The aggregate previously declared 57 methods spanning nine unrelated admin domains; every one of its
// 14+ consumers received all 57 but used only a handful (ISP violation). These focused role interfaces
// group the methods by domain so a consumer can depend on the narrow slice it actually uses. The
// aggregate IHonuaAdminOperateClient (OperateAdminShims.cs) inherits all of them, so the single
// HonuaAdminOperateHttpClient implementation and every existing consumer/test-fake keep compiling
// unchanged; consumers migrate to a narrow interface only where the change is mechanical.

/// <summary>Data-connection lifecycle: list, create, test (draft + existing), and discover tables.</summary>
public interface IHonuaAdminConnectionsClient
{
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
}

/// <summary>File and external-service import: formats, file upload, discovery, GeoServices import jobs.</summary>
public interface IHonuaAdminImportClient
{
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
}

/// <summary>Per-layer field / display / editing / spatial metadata read + write.</summary>
public interface IHonuaAdminLayerMetadataClient
{
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
}

/// <summary>Layer publishing / enablement + service enumeration.</summary>
public interface IHonuaAdminLayerPublishingClient
{
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
}

/// <summary>Service-level settings: protocols, access policy, MapServer, time-info, and settings caps.</summary>
public interface IHonuaAdminServiceSettingsClient
{
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
}

/// <summary>Layer 3D extrusion + lifecycle status (default-implemented; server build may not expose these).</summary>
public interface IHonuaAdminLayer3DAndLifecycleClient
{
    /// <summary>
    /// Reads a layer's persisted 3D extrusion + 3D symbology metadata
    /// (<c>GET /api/v1/admin/metadata/layers/{layerId}/extrusion</c>). <paramref name="layerId"/> is the global
    /// id. Returns the extrusion (height/base-height field, unit, default height, material hint) and the 3D
    /// symbology (default RGB color + opacity and the attribute-comparison rules) the server has persisted.
    /// </summary>
    /// <remarks>
    /// Default-implemented so existing implementors (e.g. test fakes) compile without change; the real
    /// <see cref="HonuaAdminOperateHttpClient"/> overrides it to call the server.
    /// </remarks>
    Task<HonuaAdminEndpointResult<HonuaAdminLayerExtrusion>> GetLayerExtrusionAsync(
        int layerId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "This IHonuaAdminOperateClient implementation does not provide GetLayerExtrusionAsync.");

    /// <summary>
    /// Updates a layer's 3D extrusion + 3D symbology metadata
    /// (<c>PUT /api/v1/admin/metadata/layers/{layerId}/extrusion</c>). A null section leaves it unchanged; the
    /// matching clear flag removes it. heightField / baseHeightField / rule attributes are validated server-side
    /// against the layer schema. The server re-reads and returns the persisted projection.
    /// </summary>
    /// <remarks>
    /// Default-implemented so existing implementors (e.g. test fakes) compile without change; the real
    /// <see cref="HonuaAdminOperateHttpClient"/> overrides it to call the server.
    /// </remarks>
    Task<HonuaAdminEndpointResult<HonuaAdminLayerExtrusion>> UpdateLayerExtrusionAsync(
        int layerId,
        HonuaAdminLayerExtrusionUpdate request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "This IHonuaAdminOperateClient implementation does not provide UpdateLayerExtrusionAsync.");

    /// <summary>
    /// Reads a layer's persisted lifecycle status — lifecycle stage + operational state
    /// (<c>GET /api/v1/admin/metadata/layers/{layerId}/status</c>). <paramref name="layerId"/> is the global id.
    /// </summary>
    /// <remarks>
    /// Default-implemented so existing implementors (e.g. test fakes) compile without change; the real
    /// <see cref="HonuaAdminOperateHttpClient"/> overrides it to call the server.
    /// </remarks>
    Task<HonuaAdminEndpointResult<HonuaAdminLayerStatus>> GetLayerStatusAsync(
        int layerId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "This IHonuaAdminOperateClient implementation does not provide GetLayerStatusAsync.");

    /// <summary>
    /// Updates a layer's lifecycle status (<c>PUT /api/v1/admin/metadata/layers/{layerId}/status</c>). At least
    /// one of lifecycle/state is required; a null field leaves the other unchanged. The server re-reads and
    /// returns the persisted status.
    /// </summary>
    /// <remarks>
    /// Default-implemented so existing implementors (e.g. test fakes) compile without change; the real
    /// <see cref="HonuaAdminOperateHttpClient"/> overrides it to call the server.
    /// </remarks>
    Task<HonuaAdminEndpointResult<HonuaAdminLayerStatus>> UpdateLayerStatusAsync(
        int layerId,
        HonuaAdminLayerStatusUpdate request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "This IHonuaAdminOperateClient implementation does not provide UpdateLayerStatusAsync.");
}

/// <summary>Publication overrides read + write (default-implemented).</summary>
public interface IHonuaAdminPublicationOverridesClient
{
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
}

/// <summary>Layer schema authoring: relationships, subtypes, attribute rules, and the permanent filter.</summary>
public interface IHonuaAdminLayerSchemaClient
{
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
    /// Reads a layer's persisted subtype set — subtype field, default subtype code, per-subtype field
    /// default/domain overrides (<c>GET /api/v1/admin/metadata/layers/{layerId}/subtypes</c>).
    /// <paramref name="layerId"/> is the global id.
    /// </summary>
    /// <remarks>
    /// Default-implemented so existing implementors (e.g. test fakes) compile without change; the real
    /// <see cref="HonuaAdminOperateHttpClient"/> overrides it to call the server.
    /// </remarks>
    Task<HonuaAdminEndpointResult<HonuaAdminLayerSubtypes>> GetLayerSubtypesAsync(
        int layerId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "This IHonuaAdminOperateClient implementation does not provide GetLayerSubtypesAsync.");

    /// <summary>
    /// Updates a layer's subtype set (<c>PUT /api/v1/admin/metadata/layers/{layerId}/subtypes</c>).
    /// <c>clear:true</c> removes the set; a null <c>subtypes</c> keeps the existing set; the subtype field /
    /// override keys are validated server-side against the schema. The server re-reads and returns the
    /// persisted subtype projection.
    /// </summary>
    /// <remarks>
    /// Default-implemented so existing implementors (e.g. test fakes) compile without change; the real
    /// <see cref="HonuaAdminOperateHttpClient"/> overrides it to call the server.
    /// </remarks>
    Task<HonuaAdminEndpointResult<HonuaAdminLayerSubtypes>> UpdateLayerSubtypesAsync(
        int layerId,
        HonuaAdminLayerSubtypesUpdate request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "This IHonuaAdminOperateClient implementation does not provide UpdateLayerSubtypesAsync.");

    /// <summary>
    /// Reads a layer's persisted attribute rules — calculation/constraint/validation rules with their
    /// triggering events (<c>GET /api/v1/admin/metadata/layers/{layerId}/attribute-rules</c>).
    /// <paramref name="layerId"/> is the global id.
    /// </summary>
    /// <remarks>
    /// Default-implemented so existing implementors (e.g. test fakes) compile without change; the real
    /// <see cref="HonuaAdminOperateHttpClient"/> overrides it to call the server.
    /// </remarks>
    Task<HonuaAdminEndpointResult<HonuaAdminLayerAttributeRules>> GetLayerAttributeRulesAsync(
        int layerId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "This IHonuaAdminOperateClient implementation does not provide GetLayerAttributeRulesAsync.");

    /// <summary>
    /// Replaces a layer's attribute rules (<c>PUT /api/v1/admin/metadata/layers/{layerId}/attribute-rules</c>).
    /// An empty <c>rules</c> clears the set; duplicate rule names are rejected server-side. The server re-reads
    /// and returns the persisted rule set.
    /// </summary>
    /// <remarks>
    /// Default-implemented so existing implementors (e.g. test fakes) compile without change; the real
    /// <see cref="HonuaAdminOperateHttpClient"/> overrides it to call the server.
    /// </remarks>
    Task<HonuaAdminEndpointResult<HonuaAdminLayerAttributeRules>> UpdateLayerAttributeRulesAsync(
        int layerId,
        HonuaAdminLayerAttributeRulesUpdate request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "This IHonuaAdminOperateClient implementation does not provide UpdateLayerAttributeRulesAsync.");

    /// <summary>
    /// Reads a layer's persisted permanent filter — the server-enforced query filter applied to every read of
    /// the layer (<c>GET /api/v1/admin/metadata/layers/{layerId}/filter</c>). <paramref name="layerId"/> is the
    /// global id. The projection's <c>PermanentFilter</c> is null when no filter is saved.
    /// </summary>
    /// <remarks>
    /// Default-implemented so existing implementors (e.g. test fakes that hand-roll the full interface) compile
    /// without change; the real <see cref="HonuaAdminOperateHttpClient"/> overrides it to call the server.
    /// </remarks>
    Task<HonuaAdminEndpointResult<HonuaAdminLayerFilter>> GetLayerFilterAsync(
        int layerId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "This IHonuaAdminOperateClient implementation does not provide GetLayerFilterAsync.");

    /// <summary>
    /// Authors a layer's permanent filter (<c>PUT /api/v1/admin/metadata/layers/{layerId}/filter</c>). The PUT
    /// body carries <c>{ permanentFilter: { expression, language } }</c>; send a request whose
    /// <see cref="HonuaAdminLayerFilterUpdate.PermanentFilter"/> is null to CLEAR the saved filter. The server
    /// validates the expression against the layer schema and answers <c>400</c> with a reason on a bad
    /// expression; the resulting issue carries that reason so the console surfaces it honestly. The server
    /// re-reads and returns the persisted filter projection.
    /// </summary>
    /// <remarks>
    /// Default-implemented so existing implementors (e.g. test fakes that hand-roll the full interface) compile
    /// without change; the real <see cref="HonuaAdminOperateHttpClient"/> overrides it to call the server.
    /// </remarks>
    Task<HonuaAdminEndpointResult<HonuaAdminLayerFilter>> UpdateLayerFilterAsync(
        int layerId,
        HonuaAdminLayerFilterUpdate request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "This IHonuaAdminOperateClient implementation does not provide UpdateLayerFilterAsync.");
}

/// <summary>Layer + service discovery / catalog metadata read + write.</summary>
public interface IHonuaAdminDiscoveryClient
{
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
}

/// <summary>Server info + auth introspection + endpoint reachability probe.</summary>
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

/// <summary>Per-layer presentation authoring: popup-info + drawing-info (renderer) round-trip.</summary>
public interface IHonuaAdminPresentationClient
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
