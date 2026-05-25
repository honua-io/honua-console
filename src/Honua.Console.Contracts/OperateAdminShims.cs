using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

// SHIM(honua-sdk-dotnet#166): Operate/Admin projections are not yet available
// through honua-sdk-dotnet. Keep these wire records and the thin HTTP client in
// the Console contracts boundary until honua-console#7 swaps them for SDK types.
public sealed record HonuaAdminOperateClientOptions(Uri BaseUri, string? ApiKey = null);

public interface IHonuaAdminOperateClient
{
    Uri BaseUri { get; }

    Task<HonuaAdminEndpointResult<HonuaAdminConnectionSummary[]>> ListConnectionsAsync(
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaAdminPublishedLayerSummary[]>> ListConnectionLayersAsync(
        string connectionId,
        string? serviceName = null,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaAdminServiceSummary[]>> ListServicesAsync(
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaAdminServiceSettingsResponse>> GetServiceSettingsAsync(
        string serviceName,
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
}

public sealed class HonuaAdminOperateHttpClient : IHonuaAdminOperateClient, IDisposable
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
    int? StatusCode = null);

public sealed record HonuaAdminApiResponse<T>(
    bool Success,
    T? Data,
    string? Message,
    DateTimeOffset? Timestamp);

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
