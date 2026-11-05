using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

public sealed record HonuaAdminOperateClientOptions(Uri BaseUri, string? ApiKey = null);

public partial interface IHonuaAdminOperateClient
    : IHonuaAdminConnectionsClient,
      IHonuaAdminImportClient,
      IHonuaAdminLayerMetadataClient,
      IHonuaAdminLayerPublishingClient,
      IHonuaAdminServiceSettingsClient,
      IHonuaAdminLayerSchemaClient,
      IHonuaAdminServerInfoClient,
      IHonuaAdminPresentationClient
{
    Uri BaseUri { get; }
}

public sealed partial class HonuaAdminOperateHttpClient : IHonuaAdminOperateClient, IDisposable
{
    private const string MissingData = "The Honua server response did not include data.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly AdminBodyFormat ReadEnvelope = new(
        JsonOptions,
        AdminBodyRead.Streamed,
        "The Honua server response did not match the expected admin API shape");

    private static readonly AdminBodyFormat WriteEnvelope = ReadEnvelope with { Read = AdminBodyRead.Buffered };

    private readonly HttpClient _httpClient;
    private readonly AdminEndpointTransport _transport;

    public HonuaAdminOperateHttpClient(HttpClient httpClient, HonuaAdminOperateClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        BaseUri = options.BaseUri;
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= BaseUri;
        _transport = new AdminEndpointTransport(
            httpClient,
            options.ApiKey,
            AdminTransportFailure.UnreachableOrCancelled("The Honua server endpoint"));
    }

    public Uri BaseUri { get; }

    public Task<HonuaAdminEndpointResult<HonuaAdminConnectionSummary[]>> ListConnectionsAsync(
        CancellationToken cancellationToken = default) =>
        GetEnvelopeAsync<HonuaAdminConnectionSummary[]>(
            "/api/v1/admin/connections/",
            "GET /api/v1/admin/connections",
            cancellationToken);

    public Task<HonuaAdminEndpointResult<HonuaAdminConnectionSummary>> CreateConnectionAsync(
        HonuaAdminCreateConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return SendEnvelopeAsync<HonuaAdminConnectionSummary>(
            () => JsonRequest(HttpMethod.Post, "/api/v1/admin/connections/", request),
            "POST /api/v1/admin/connections",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaAdminConnectionTestResult>> TestDraftConnectionAsync(
        HonuaAdminCreateConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return SendEnvelopeAsync<HonuaAdminConnectionTestResult>(
            () => JsonRequest(HttpMethod.Post, "/api/v1/admin/connections/test", request),
            "POST /api/v1/admin/connections/test",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaAdminConnectionTestResult>> TestConnectionAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);

        return SendEnvelopeAsync<HonuaAdminConnectionTestResult>(
            () => Request(HttpMethod.Post, $"/api/v1/admin/connections/{Uri.EscapeDataString(connectionId)}/test"),
            "POST /api/v1/admin/connections/{id}/test",
            cancellationToken);
    }

    public async Task<HonuaAdminEndpointResult<HonuaAdminTableInfo[]>> ListConnectionTablesAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);

        var call = new AdminEndpointCall<HonuaAdminTableInfo[]>(
            () => Request(HttpMethod.Get, $"/api/v1/admin/connections/{Uri.EscapeDataString(connectionId)}/tables"),
            "GET /api/v1/admin/connections/{id}/tables",
            new(
                AdminFailureMappings.StatusDetail,
                AdminResponseBodies.Optional<HonuaAdminTableDiscoveryBody, HonuaAdminTableInfo[]>(
                BareBody(AdminBodyRead.Streamed, "table-discovery"),
                body => body?.Tables ?? [])))
        {
            Completion = HttpCompletionOption.ResponseHeadersRead
        };
        return await _transport.SendAsync(call, cancellationToken).ConfigureAwait(false);
    }

    public async Task<HonuaAdminEndpointResult<HonuaAdminImportFormats>> GetImportFormatsAsync(
        CancellationToken cancellationToken = default)
    {
        var call = new AdminEndpointCall<HonuaAdminImportFormats>(
            () => Request(HttpMethod.Get, "/api/v1/admin/import/formats"),
            "GET /api/v1/admin/import/formats",
            new(
                AdminFailureMappings.StatusDetail,
                AdminResponseBodies.Optional<HonuaAdminImportFormats, HonuaAdminImportFormats>(
                BareBody(AdminBodyRead.Streamed, "formats"),
                formats => formats ?? new HonuaAdminImportFormats())))
        {
            Completion = HttpCompletionOption.ResponseHeadersRead
        };
        return await _transport.SendAsync(call, cancellationToken).ConfigureAwait(false);
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

        var call = new AdminEndpointCall<HonuaAdminImportResult>(
            () => Request(
                HttpMethod.Post,
                "/api/v1/admin/import/upload",
                ImportUploadForm(new ImportUpload(fileContent, fileName, tableName, targetSchema))),
            "POST /api/v1/admin/import/upload",
            new(
                AdminFailureMappings.AnyNonEmptyServerMessage,
                AdminResponseBodies.Optional<HonuaAdminImportResult, HonuaAdminImportResult>(
                BareBody(AdminBodyRead.Buffered, "import-result"),
                result => result ?? new HonuaAdminImportResult())));
        return await _transport.SendAsync(call, cancellationToken).ConfigureAwait(false);
    }

    public async Task<HonuaAdminEndpointResult<HonuaAdminExternalServiceDiscovery>> DiscoverExternalServiceAsync(
        string url,
        HonuaAdminExternalServiceCredentials? credentials = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        var call = new AdminEndpointCall<HonuaAdminExternalServiceDiscovery>(
            () => JsonRequest(HttpMethod.Post, "/api/v1/admin/external-services/discover", new ExternalServiceDiscoverBody(url, credentials)),
            "POST /api/v1/admin/external-services/discover",
            new(
                AdminFailureMappings.ServerMessage,
                AdminResponseBodies.Optional<HonuaAdminExternalServiceDiscovery, HonuaAdminExternalServiceDiscovery>(
                BareBody(AdminBodyRead.Buffered, "discovery"),
                discovery => discovery ?? new HonuaAdminExternalServiceDiscovery())));
        return await _transport.SendAsync(call, cancellationToken).ConfigureAwait(false);
    }

    public async Task<HonuaAdminEndpointResult<HonuaAdminGeoservicesImportJob>> StartGeoservicesImportAsync(
        HonuaAdminGeoservicesImportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var call = new AdminEndpointCall<HonuaAdminGeoservicesImportJob>(
            () => JsonRequest(HttpMethod.Post, "/api/v1/admin/import/geoservices/start", request),
            "POST /api/v1/admin/import/geoservices/start",
            new(
                AdminFailureMappings.ServerMessage,
                AdminResponseBodies.Json<HonuaAdminGeoservicesImportJob, HonuaAdminGeoservicesImportJob>(
                BareBody(AdminBodyRead.Buffered, "import-job"),
                (job, context) => job is null || string.IsNullOrWhiteSpace(job.JobId)
                    ? context.Unsupported<HonuaAdminGeoservicesImportJob>("The Honua server did not return an import job id.")
                    : HonuaAdminEndpointResult<HonuaAdminGeoservicesImportJob>.FromData(job))));
        return await _transport.SendAsync(call, cancellationToken).ConfigureAwait(false);
    }

    public async Task<HonuaAdminEndpointResult<HonuaAdminGeoservicesImportProgress>> GetGeoservicesImportJobAsync(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        var call = new AdminEndpointCall<HonuaAdminGeoservicesImportProgress>(
            () => Request(HttpMethod.Get, $"/api/v1/admin/import/geoservices/jobs/{Uri.EscapeDataString(jobId)}"),
            "GET /api/v1/admin/import/geoservices/jobs/{jobId}",
            new(
                AdminFailureMappings.ServerMessage,
                AdminResponseBodies.Optional<HonuaAdminGeoservicesImportProgress, HonuaAdminGeoservicesImportProgress>(
                BareBody(AdminBodyRead.Buffered, "import-progress"),
                progress => progress ?? new HonuaAdminGeoservicesImportProgress())));
        return await _transport.SendAsync(call, cancellationToken).ConfigureAwait(false);
    }

    public Task<HonuaAdminEndpointResult<HonuaAdminPublishedLayerSummary[]>> ListConnectionLayersAsync(
        string connectionId,
        string? serviceName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);

        return GetEnvelopeAsync<HonuaAdminPublishedLayerSummary[]>(
            WithServiceName($"/api/v1/admin/connections/{Uri.EscapeDataString(connectionId)}/layers/", serviceName),
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

        return SendEnvelopeAsync<HonuaAdminPublishedLayerSummary>(
            () => JsonRequest(HttpMethod.Post, $"/api/v1/admin/connections/{Uri.EscapeDataString(connectionId)}/layers/", request),
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

        var path = WithServiceName(
            $"/api/v1/admin/connections/{Uri.EscapeDataString(connectionId)}/layers/{layerId.ToString(CultureInfo.InvariantCulture)}/enabled",
            serviceName);
        return SendEnvelopeAsync<HonuaAdminPublishedLayerSummary>(
            () => JsonRequest(HttpMethod.Put, path, new LayerEnabledBody(enabled)),
            "PUT /api/v1/admin/connections/{id}/layers/{layerId}/enabled",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaAdminServiceSummary[]>> ListServicesAsync(
        CancellationToken cancellationToken = default) =>
        GetEnvelopeAsync<HonuaAdminServiceSummary[]>(
            "/api/v1/admin/services/",
            "GET /api/v1/admin/services",
            cancellationToken);

    public Task<HonuaAdminEndpointResult<HonuaAdminServiceSettingsResponse>> GetServiceSettingsAsync(
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        return GetEnvelopeAsync<HonuaAdminServiceSettingsResponse>(
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

        return SendEnvelopeAsync<HonuaAdminServiceSettingsResponse>(
            () => JsonRequest(
                HttpMethod.Put,
                $"/api/v1/admin/services/{Uri.EscapeDataString(serviceName)}/protocols",
                new UpdateProtocolsBody(enabledProtocols)),
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

        return SendEnvelopeAsync<HonuaAdminServiceSettingsResponse>(
            () => JsonRequest(HttpMethod.Put, $"/api/v1/admin/services/{Uri.EscapeDataString(serviceName)}/access-policy", request),
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

        return SendEnvelopeAsync<HonuaAdminServiceSettingsResponse>(
            () => JsonRequest(HttpMethod.Put, $"/api/v1/admin/services/{Uri.EscapeDataString(serviceName)}/mapserver", request),
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

        return SendEnvelopeAsync<HonuaAdminServiceSettingsResponse>(
            () => JsonRequest(HttpMethod.Put, $"/api/v1/admin/services/{Uri.EscapeDataString(serviceName)}/timeinfo", request),
            "PUT /api/v1/admin/services/{serviceName}/timeinfo",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaAdminLayerMetadataResponse>> UpdateServiceLayerMetadataAsync(
        HonuaAdminServiceLayer layer,
        HonuaAdminUpdateLayerMetadataRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentException.ThrowIfNullOrWhiteSpace(layer.ServiceName);
        ArgumentNullException.ThrowIfNull(request);

        var path =
            $"/api/v1/admin/services/{Uri.EscapeDataString(layer.ServiceName)}/layers/{layer.LayerId.ToString(CultureInfo.InvariantCulture)}/metadata";
        return SendEnvelopeAsync<HonuaAdminLayerMetadataResponse>(
            () => JsonRequest(HttpMethod.Put, path, request),
            "PUT /api/v1/admin/services/{serviceName}/layers/{layerId}/metadata",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaAdminLayerRelationships>> GetLayerRelationshipsAsync(
        int layerId,
        CancellationToken cancellationToken = default) =>
        GetEnvelopeAsync<HonuaAdminLayerRelationships>(
            $"/api/v1/admin/metadata/layers/{layerId}/relationships",
            "GET /api/v1/admin/metadata/layers/{layerId}/relationships",
            cancellationToken);

    public Task<HonuaAdminEndpointResult<HonuaAdminLayerRelationships>> UpdateLayerRelationshipsAsync(
        int layerId,
        HonuaAdminLayerRelationshipsUpdate request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return SendEnvelopeAsync<HonuaAdminLayerRelationships>(
            () => JsonRequest(HttpMethod.Put, $"/api/v1/admin/metadata/layers/{layerId}/relationships", request),
            "PUT /api/v1/admin/metadata/layers/{layerId}/relationships",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaAdminLayerFilter>> GetLayerFilterAsync(
        int layerId,
        CancellationToken cancellationToken = default) =>
        GetEnvelopeAsync<HonuaAdminLayerFilter>(
            $"/api/v1/admin/metadata/layers/{layerId}/filter",
            "GET /api/v1/admin/metadata/layers/{layerId}/filter",
            cancellationToken);

    public Task<HonuaAdminEndpointResult<HonuaAdminLayerFilter>> UpdateLayerFilterAsync(
        int layerId,
        HonuaAdminLayerFilterUpdate request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return SendEnvelopeAsync<HonuaAdminLayerFilter>(
            () => JsonRequest(HttpMethod.Put, $"/api/v1/admin/metadata/layers/{layerId}/filter", request),
            "PUT /api/v1/admin/metadata/layers/{layerId}/filter",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaAdminLayerFields>> GetLayerFieldsAsync(
        int layerId,
        CancellationToken cancellationToken = default) =>
        GetEnvelopeAsync<HonuaAdminLayerFields>(
            $"/api/v1/admin/metadata/layers/{layerId}/fields",
            "GET /api/v1/admin/metadata/layers/{layerId}/fields",
            cancellationToken);

    public Task<HonuaAdminEndpointResult<HonuaAdminLayerFields>> UpdateLayerFieldsAsync(
        int layerId,
        HonuaAdminLayerFieldsUpdate request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return SendEnvelopeAsync<HonuaAdminLayerFields>(
            () => JsonRequest(HttpMethod.Put, $"/api/v1/admin/metadata/layers/{layerId}/fields", request),
            "PUT /api/v1/admin/metadata/layers/{layerId}/fields",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaAdminVersionResponse>> GetVersionAsync(
        CancellationToken cancellationToken = default) =>
        GetEnvelopeAsync<HonuaAdminVersionResponse>(
            "/api/v1/admin/version",
            "GET /api/v1/admin/version",
            cancellationToken);

    public Task<HonuaAdminEndpointResult<HonuaAdminCapabilitiesResponse>> GetCapabilitiesAsync(
        CancellationToken cancellationToken = default) =>
        GetEnvelopeAsync<HonuaAdminCapabilitiesResponse>(
            "/api/v1/admin/capabilities",
            "GET /api/v1/admin/capabilities",
            cancellationToken);

    public Task<HonuaAdminEndpointResult<HonuaAdminLicenseStatusResponse>> GetLicenseStatusAsync(
        CancellationToken cancellationToken = default) =>
        GetEnvelopeAsync<HonuaAdminLicenseStatusResponse>(
            "/api/v1/admin/license/",
            "GET /api/v1/admin/license",
            cancellationToken);

    public Task<HonuaAdminEndpointResult<HonuaAdminApiKeyResponse[]>> ListApiKeysAsync(
        CancellationToken cancellationToken = default) =>
        GetEnvelopeAsync<HonuaAdminApiKeyResponse[]>(
            "/api/v1/admin/api-keys/",
            "GET /api/v1/admin/api-keys",
            cancellationToken);

    public Task<HonuaAdminEndpointResult<HonuaAdminOidcProviderResponse[]>> ListOidcProvidersAsync(
        CancellationToken cancellationToken = default) =>
        GetEnvelopeAsync<HonuaAdminOidcProviderResponse[]>(
            "/api/v1/admin/oidc/providers/",
            "GET /api/v1/admin/oidc/providers",
            cancellationToken);

    public void Dispose() => _httpClient.Dispose();

    private static AdminBodyFormat BareBody(AdminBodyRead read, string shape) =>
        new(JsonOptions, read, $"The Honua server response did not match the expected {shape} shape");

    private static string WithServiceName(string path, string? serviceName) =>
        string.IsNullOrWhiteSpace(serviceName) ? path : $"{path}?serviceName={Uri.EscapeDataString(serviceName)}";

    private sealed record ImportUpload(byte[] FileContent, string FileName, string TableName, string? TargetSchema);

    private static MultipartFormDataContent ImportUploadForm(ImportUpload upload)
    {
        var form = new MultipartFormDataContent();
        var fileEntry = new ByteArrayContent(upload.FileContent);
        fileEntry.Headers.TryAddWithoutValidation("Content-Type", "application/octet-stream");
        form.Add(fileEntry, "file", upload.FileName);
        form.Add(new StringContent(upload.TableName), "TableName");
        if (!string.IsNullOrWhiteSpace(upload.TargetSchema))
        {
            form.Add(new StringContent(upload.TargetSchema), "TargetSchema");
        }

        return form;
    }

    private HttpRequestMessage Request(HttpMethod method, string path, HttpContent? content = null) =>
        new(method, AdminEndpointPaths.Resolve(BaseUri, path)) { Content = content };

    private HttpRequestMessage JsonRequest<TBody>(HttpMethod method, string path, TBody body) =>
        Request(method, path, JsonContent.Create(body, options: JsonOptions));

    private Task<HonuaAdminEndpointResult<T>> GetEnvelopeAsync<T>(
        string path,
        string contract,
        CancellationToken cancellationToken) =>
        _transport.SendAsync(
            new AdminEndpointCall<T>(
                () => Request(HttpMethod.Get, path),
                contract,
                new(
                    AdminFailureMappings.StatusDetail,
                    AdminResponseBodies.Envelope<T>(ReadEnvelope, MissingData)))
            {
                Completion = HttpCompletionOption.ResponseHeadersRead
            },
            cancellationToken);

    private Task<HonuaAdminEndpointResult<T>> SendEnvelopeAsync<T>(
        Func<HttpRequestMessage> createRequest,
        string contract,
        CancellationToken cancellationToken) =>
        _transport.SendAsync(
            new AdminEndpointCall<T>(
                createRequest,
                contract,
                new(
                    AdminFailureMappings.ServerMessageAndFieldErrors,
                    AdminResponseBodies.Envelope<T>(WriteEnvelope, MissingData))),
            cancellationToken);
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
    public TerminalFailureReceipt? Receipt { get; init; }

    // Filled from an RFC 7807 errors list; empty when the rejection named no fields.
    public IReadOnlyList<HonuaFieldValidationError> FieldErrors { get; init; } = [];
}

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
    DateTimeOffset? Timestamp) : IAdminResponseEnvelope<T>;

public sealed record HonuaAdminCreateConnectionRequest
{
    public required string Name { get; init; }

    public string? Description { get; init; }

    // Host is optional beside a secret reference and required when the password is sent inline.
    public string? Host { get; init; }

    // Port is left unset for a secret reference; an inline password defaults to 5432 on the server.
    public int? Port { get; init; }

    public string? DatabaseName { get; init; }

    public string? Username { get; init; }

    // Sent to the server secret store, never returned, and mutually exclusive with SecretReference.
    public string? Password { get; init; }

    // The referenced secret holds the full connection string, not only a password.
    public string? SecretReference { get; init; }

    // SecretType is required when SecretReference is set.
    public string? SecretType { get; init; }

    public string Provider { get; init; } = "postgis";

    public bool SslRequired { get; init; }

    public string SslMode { get; init; } = "Disable";
}

public sealed record HonuaAdminConnectionTestResult
{
    // ConnectionId is empty when the test was a draft and nothing was saved.
    public Guid ConnectionId { get; init; }

    public string? ConnectionName { get; init; }

    public bool IsHealthy { get; init; }

    public DateTimeOffset? TestedAt { get; init; }

    public string? Message { get; init; }
}

internal sealed record HonuaAdminTableDiscoveryBody
{
    public HonuaAdminTableInfo[]? Tables { get; init; }
}

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

public sealed record HonuaAdminColumnInfo
{
    public string? Name { get; init; }

    public string? DataType { get; init; }
}

public sealed record HonuaAdminImportFormats
{
    public IReadOnlyList<string> SupportedExtensions { get; init; } = [];

    public IReadOnlyDictionary<string, string> FormatDescriptions { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

internal sealed record ExternalServiceDiscoverBody(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("credentials")] HonuaAdminExternalServiceCredentials? Credentials = null);

// Used for this discovery call only; the console does not store these secrets.
public sealed record HonuaAdminExternalServiceCredentials
{
    // Mode is arcgis-token, token, basic, or oauth.
    public string? Mode { get; init; }

    public string? Username { get; init; }

    public string? Password { get; init; }

    public string? Token { get; init; }

    public string? TokenUrl { get; init; }

    public string? ClientId { get; init; }

    public string? ClientSecret { get; init; }

    public string? Referer { get; init; }
}

public sealed record HonuaAdminExternalServiceDiscovery
{
    public string? SourceUrl { get; init; }

    public string? NormalizedUrl { get; init; }

    public string? SourceKind { get; init; }

    public string? ServiceType { get; init; }

    public string? ServiceName { get; init; }

    public string? Description { get; init; }

    public int? Srid { get; init; }

    public bool IsCatalog { get; init; }

    public IReadOnlyList<HonuaAdminExternalLayerCandidate> Candidates { get; init; } = [];

    public IReadOnlyList<HonuaAdminExternalServiceSummary> Services { get; init; } = [];
}

public sealed record HonuaAdminLayerFields
{
    public int LayerId { get; init; }

    public IReadOnlyList<HonuaAdminLayerField> Fields { get; init; } = [];
}

public sealed record HonuaAdminLayerField
{
    public string? Name { get; init; }

    public string? Type { get; init; }

    public string? Alias { get; init; }

    public HonuaAdminFieldDomain? Domain { get; init; }

    public bool Hidden { get; init; }

    // The persisted default is a JSON scalar, or null when the field has none.
    public JsonElement? DefaultValue { get; init; }
}

public sealed record HonuaAdminFieldDomain
{
    public string? Name { get; init; }

    // Type is codedValue, which carries CodedValues, or range, which carries Range.
    public string? Type { get; init; }

    public IReadOnlyList<HonuaAdminCodedValue> CodedValues { get; init; } = [];

    // A two-element min and max, with min at most max, and null for a coded-value domain.
    public IReadOnlyList<double>? Range { get; init; }

    // An Esri merge-policy token, and null leaves the server default.
    public string? MergePolicy { get; init; }

    // An Esri split-policy token, and null leaves the server default.
    public string? SplitPolicy { get; init; }
}

public sealed record HonuaAdminCodedValue
{
    // Keeps the JSON type the field requires.
    public JsonElement Code { get; init; }

    public string? Name { get; init; }
}

public sealed record HonuaAdminLayerFieldsUpdate
{
    public IReadOnlyList<HonuaAdminLayerFieldUpdate> Fields { get; init; } = [];
}

public sealed record HonuaAdminLayerFieldUpdate
{
    public required string Name { get; init; }

    public string? Alias { get; init; }

    // Null clears the domain; a caller that wants to keep the current domain must send it again.
    public HonuaAdminFieldDomain? Domain { get; init; }

    public bool? Hidden { get; init; }

    // A non-null value must be a JSON scalar, a JSON null clears the stored default, and omitting the property leaves it unchanged.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? DefaultValue { get; init; }
}

public sealed record HonuaAdminLayerRelationships
{
    public int LayerId { get; init; }

    public IReadOnlyList<HonuaAdminLayerRelationship> Relationships { get; init; } = [];
}

public sealed record HonuaAdminLayerRelationship
{
    public string? Id { get; init; }

    public string? Name { get; init; }

    public int? RelatedLayerId { get; init; }

    // Role is origin or destination.
    public string? Role { get; init; }

    public string? Cardinality { get; init; }

    public string? OriginField { get; init; }

    public string? DestinationField { get; init; }

    public int? EsriRelationshipId { get; init; }
}

public sealed record HonuaAdminLayerRelationshipsUpdate
{
    public IReadOnlyList<HonuaAdminLayerRelationship> Relationships { get; init; } = [];
}

public sealed record HonuaAdminLayerFilter
{
    public int LayerId { get; init; }

    public HonuaAdminPermanentFilter? PermanentFilter { get; init; }
}

// Language is arcgis-sql, cql2-text, or cql2-json, and the expression is capped at 4096 characters.
public sealed record HonuaAdminPermanentFilter
{
    public string Expression { get; init; } = string.Empty;

    public string? Language { get; init; }
}

// Null is serialized on purpose so the server clears the saved filter.
public sealed record HonuaAdminLayerFilterUpdate
{
    public HonuaAdminPermanentFilter? PermanentFilter { get; init; }
}

public sealed record HonuaAdminUpdateLayerMetadataRequest
{
    public HonuaAdminLayerEditingPatch? Editing { get; init; }
}

// A null member keeps the stored value, and an empty GlobalIdField clears the binding.
public sealed record HonuaAdminLayerEditingPatch
{
    public string? GlobalIdField { get; init; }

    public bool? SupportsAttachments { get; init; }

    public bool? Create { get; init; }

    public bool? Update { get; init; }

    public bool? Delete { get; init; }
}

public sealed record HonuaAdminServiceLayer(string ServiceName, int LayerId);

public sealed record HonuaAdminLayerMetadataResponse
{
    public int LayerId { get; init; }

    public string? LayerName { get; init; }
}

// A null member clears that time field on the server.
public sealed record HonuaAdminUpdateTimeInfoRequest
{
    public string? StartTimeField { get; init; }

    public string? EndTimeField { get; init; }

    public string? TrackIdField { get; init; }
}

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

public sealed record HonuaAdminGeoservicesImportCredentials
{
    public string? Mode { get; init; }

    public string? AccessToken { get; init; }

    public string? Username { get; init; }

    public string? Password { get; init; }
}

public sealed record HonuaAdminGeoservicesImportJob
{
    public string? JobId { get; init; }

    public string? Message { get; init; }

    public string? StatusUrl { get; init; }

    public string? CancelUrl { get; init; }
}

public sealed record HonuaAdminGeoservicesImportProgress
{
    public string? JobId { get; init; }

    // Server import status ordinal: 0 Queued, 1 Discovering, 2 RetrievingFeatures, 3 CreatingTable, 4 InsertingFeatures, 5 Publishing, 6 Completed, 7 Failed, 8 Cancelled.
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

public sealed record HonuaAdminExternalLayerCandidate
{
    public int? LayerId { get; init; }

    public string? Name { get; init; }

    public string? GeometryType { get; init; }

    public int? FeatureCount { get; init; }

    public string? Description { get; init; }

    public string? ServiceUrl { get; init; }
}

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

internal sealed record LayerEnabledBody(
    [property: JsonPropertyName("enabled")] bool Enabled);

internal sealed record UpdateProtocolsBody(
    [property: JsonPropertyName("enabledProtocols")] IReadOnlyList<string> EnabledProtocols);

// A null member leaves the stored policy unchanged.
public sealed record HonuaAdminUpdateAccessPolicyRequest
{
    public bool? AllowAnonymous { get; init; }

    public bool? AllowAnonymousWrite { get; init; }

    public IReadOnlyList<string>? AllowedRoles { get; init; }

    public IReadOnlyList<string>? AllowedWriteRoles { get; init; }
}

// A null member leaves the stored setting unchanged.
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

    // Stored extent in EPSG:4326, or null when the layer has none.
    public HonuaAdminLayerExtent? Extent { get; init; }
}

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

    public string? FolderId { get; init; }

    public string? FolderName { get; init; }

    public string? PublicName { get; init; }

    public string? Title { get; init; }

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
