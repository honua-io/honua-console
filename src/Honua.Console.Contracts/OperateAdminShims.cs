using System.Globalization;
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

/// <summary>
/// Aggregate honua-server admin operate client. Composed of the role interfaces (see
/// OperateAdminRoleInterfaces.cs) so a consumer can depend on the narrow slice it uses (ISP,
/// honua-console#279 PA-242); the aggregate is retained so the single <see cref="HonuaAdminOperateHttpClient"/>
/// implementation and every existing consumer/test-fake that references this interface keep compiling
/// unchanged.
/// </summary>
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
            AdminFailureMappings.StatusDetail,
            AdminResponseBodies.Optional<HonuaAdminTableDiscoveryBody, HonuaAdminTableInfo[]>(
                BareBody(AdminBodyRead.Streamed, "table-discovery"),
                body => body?.Tables ?? []))
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
            AdminFailureMappings.StatusDetail,
            AdminResponseBodies.Optional<HonuaAdminImportFormats, HonuaAdminImportFormats>(
                BareBody(AdminBodyRead.Streamed, "formats"),
                formats => formats ?? new HonuaAdminImportFormats()))
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
            () => Request(HttpMethod.Post, "/api/v1/admin/import/upload", ImportUploadForm(fileContent, fileName, tableName, targetSchema)),
            "POST /api/v1/admin/import/upload",
            AdminFailureMappings.ServerMessage,
            AdminResponseBodies.Optional<HonuaAdminImportResult, HonuaAdminImportResult>(
                BareBody(AdminBodyRead.Buffered, "import-result"),
                result => result ?? new HonuaAdminImportResult()));
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
            AdminFailureMappings.ServerMessage,
            AdminResponseBodies.Optional<HonuaAdminExternalServiceDiscovery, HonuaAdminExternalServiceDiscovery>(
                BareBody(AdminBodyRead.Buffered, "discovery"),
                discovery => discovery ?? new HonuaAdminExternalServiceDiscovery()));
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
            AdminFailureMappings.ServerMessage,
            AdminResponseBodies.Json<HonuaAdminGeoservicesImportJob, HonuaAdminGeoservicesImportJob>(
                BareBody(AdminBodyRead.Buffered, "import-job"),
                (job, context) => job is null || string.IsNullOrWhiteSpace(job.JobId)
                    ? context.Unsupported<HonuaAdminGeoservicesImportJob>("The Honua server did not return an import job id.")
                    : HonuaAdminEndpointResult<HonuaAdminGeoservicesImportJob>.FromData(job)));
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
            AdminFailureMappings.ServerMessage,
            AdminResponseBodies.Optional<HonuaAdminGeoservicesImportProgress, HonuaAdminGeoservicesImportProgress>(
                BareBody(AdminBodyRead.Buffered, "import-progress"),
                progress => progress ?? new HonuaAdminGeoservicesImportProgress()));
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

    private static MultipartFormDataContent ImportUploadForm(
        byte[] fileContent,
        string fileName,
        string tableName,
        string? targetSchema)
    {
        var form = new MultipartFormDataContent();
        var fileEntry = new ByteArrayContent(fileContent);
        fileEntry.Headers.TryAddWithoutValidation("Content-Type", "application/octet-stream");
        form.Add(fileEntry, "file", fileName);
        form.Add(new StringContent(tableName), "TableName");
        if (!string.IsNullOrWhiteSpace(targetSchema))
        {
            form.Add(new StringContent(targetSchema), "TargetSchema");
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
                AdminFailureMappings.StatusDetail,
                AdminResponseBodies.Envelope<T>(ReadEnvelope, MissingData))
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
                AdminFailureMappings.ServerMessageAndFieldErrors,
                AdminResponseBodies.Envelope<T>(WriteEnvelope, MissingData)),
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
    DateTimeOffset? Timestamp) : IAdminResponseEnvelope<T>;

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

    /// <summary>Omit for a full secret reference; inline credentials default to 5432 on the server.</summary>
    public int? Port { get; init; }

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
    /// <summary>The scalar code, preserving the JSON type required by the field.</summary>
    public JsonElement Code { get; init; }

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

    /// <summary>
    /// Replacement domain value (coded-value, range, or null to clear). Callers preserving a domain must
    /// read its current value and include it in the update.
    /// </summary>
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
/// A layer's persisted permanent filter projection (<c>GET/PUT /api/v1/admin/metadata/layers/{id}/filter</c>):
/// the global layer id and the server-enforced query filter. <see cref="PermanentFilter"/> is null when no
/// filter is saved on the layer.
/// </summary>
public sealed record HonuaAdminLayerFilter
{
    public int LayerId { get; init; }

    public HonuaAdminPermanentFilter? PermanentFilter { get; init; }
}

/// <summary>
/// A server-enforced query filter expression and the language it is written in (<c>arcgis-sql</c> /
/// <c>cql2-text</c> / <c>cql2-json</c>). The expression is validated server-side against the layer schema
/// (max 4096 chars).
/// </summary>
public sealed record HonuaAdminPermanentFilter
{
    public string Expression { get; init; } = string.Empty;

    /// <summary>"arcgis-sql" (default), "cql2-text", or "cql2-json".</summary>
    public string? Language { get; init; }
}

/// <summary>
/// Request body for <c>PUT /api/v1/admin/metadata/layers/{id}/filter</c>. Carries the permanent filter to
/// author; set <see cref="PermanentFilter"/> to null to CLEAR the saved filter (the property is always
/// serialized so the server receives <c>{ "permanentFilter": null }</c>).
/// </summary>
public sealed record HonuaAdminLayerFilterUpdate
{
    public HonuaAdminPermanentFilter? PermanentFilter { get; init; }
}

/// <summary>
/// Request body for <c>PUT /api/v1/admin/services/{serviceName}/layers/{layerId}/metadata</c>, mirroring the
/// server's <c>UpdateLayerMetadataRequest</c>. Only the editing patch is authored from the Console.
/// </summary>
public sealed record HonuaAdminUpdateLayerMetadataRequest
{
    public HonuaAdminLayerEditingPatch? Editing { get; init; }
}

/// <summary>
/// The server's <c>MetadataV2EditingPatch</c>: a null field keeps the stored value, and an empty
/// <see cref="GlobalIdField"/> clears the declared global-id binding.
/// </summary>
public sealed record HonuaAdminLayerEditingPatch
{
    public string? GlobalIdField { get; init; }

    public bool? SupportsAttachments { get; init; }

    public bool? Create { get; init; }

    public bool? Update { get; init; }

    public bool? Delete { get; init; }
}

/// <summary>One layer in one service: the key of the service-scoped layer metadata route.</summary>
public sealed record HonuaAdminServiceLayer(string ServiceName, int LayerId);

/// <summary>The server's <c>LayerMetadataResponse</c>, limited to the fields the Console reads.</summary>
public sealed record HonuaAdminLayerMetadataResponse
{
    public int LayerId { get; init; }

    public string? LayerName { get; init; }
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
