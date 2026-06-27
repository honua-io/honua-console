using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Binds the Operate metrics surface to a real honua-server through the production-
/// monitoring endpoints (group <c>/monitoring</c>, admin-authorized, bare JSON — NO
/// ApiResponse envelope). Base address comes from the active
/// <see cref="ConsoleEnvironmentProfile"/>; the admin API key is sent as
/// <c>X-API-Key</c>. A single shared <see cref="HttpClient"/> is reused; each request
/// builds an absolute URI and attaches its own key. Deserialization is source-generated
/// for trim/AOT safety.
///
/// Charter section 11 (no standing mock for server-owned data) is preserved: this client
/// never returns seeded metrics. When no environment profile is bound, every read returns
/// a missing-binding (<see cref="OperateSectionStatus.Unavailable"/>) result. Mirrors
/// <see cref="HttpConsoleGitOpsReleaseClient"/>.
/// </summary>
public sealed class HttpConsoleMonitoringMetricsClient : IConsoleMonitoringMetricsClient
{
    private const string NoProfileMessage =
        "No active environment profile is selected. Connect an environment to load server metrics.";

    private readonly HttpClient _http;
    private readonly IConsoleEnvironmentProfileStore _profileStore;
    private readonly string? _adminApiKey;

    public HttpConsoleMonitoringMetricsClient(
        HttpClient http,
        IConsoleEnvironmentProfileStore profileStore,
        string? adminApiKey = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        _adminApiKey = string.IsNullOrWhiteSpace(adminApiKey) ? null : adminApiKey;
    }

    public Task<OperateSectionResult<ConnectionPoolMetricsResponse>> GetConnectionPoolMetricsAsync(
        CancellationToken cancellationToken = default) =>
        ReadAsync(
            MonitoringMetricsRoutes.ConnectionPool,
            MonitoringMetricsJsonContext.Default.ConnectionPoolMetricsResponse,
            cancellationToken);

    public Task<OperateSectionResult<CacheMetricsResponse>> GetCacheMetricsAsync(
        CancellationToken cancellationToken = default) =>
        ReadAsync(
            MonitoringMetricsRoutes.Cache,
            MonitoringMetricsJsonContext.Default.CacheMetricsResponse,
            cancellationToken);

    public Task<OperateSectionResult<ResourceMetricsResponse>> GetResourceMetricsAsync(
        CancellationToken cancellationToken = default) =>
        ReadAsync(
            MonitoringMetricsRoutes.Resources,
            MonitoringMetricsJsonContext.Default.ResourceMetricsResponse,
            cancellationToken);

    public Task<OperateSectionResult<UploadQueueMetricsResponse>> GetUploadQueueMetricsAsync(
        CancellationToken cancellationToken = default) =>
        ReadAsync(
            MonitoringMetricsRoutes.UploadQueue,
            MonitoringMetricsJsonContext.Default.UploadQueueMetricsResponse,
            cancellationToken);

    public Task<OperateSectionResult<DatabaseResilienceMetricsResponse>> GetDatabaseResilienceMetricsAsync(
        CancellationToken cancellationToken = default) =>
        ReadAsync(
            MonitoringMetricsRoutes.DatabaseResilience,
            MonitoringMetricsJsonContext.Default.DatabaseResilienceMetricsResponse,
            cancellationToken);

    public Task<OperateSectionResult<ComprehensiveHealthResponse>> GetComprehensiveHealthAsync(
        CancellationToken cancellationToken = default) =>
        ReadAsync(
            MonitoringMetricsRoutes.ComprehensiveHealth,
            MonitoringMetricsJsonContext.Default.ComprehensiveHealthResponse,
            cancellationToken);

    private async Task<OperateSectionResult<T>> ReadAsync<T>(
        string relativePath,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
        where T : class
    {
        var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return OperateSectionResult<T>.Denied(OperateSectionStatus.Unavailable, NoProfileMessage);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(profile.ServerBaseUri, relativePath));
        if (!string.IsNullOrWhiteSpace(_adminApiKey))
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", _adminApiKey);
        }

        try
        {
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return OperateSectionResult<T>.Denied(
                    MapStatus(response.StatusCode),
                    $"The honua-server monitoring API returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var value = await JsonSerializer.DeserializeAsync(stream, typeInfo, cancellationToken).ConfigureAwait(false);
            return value is null
                ? OperateSectionResult<T>.Denied(OperateSectionStatus.Unavailable, "The honua-server monitoring API returned an empty response.")
                : OperateSectionResult<T>.Allowed(value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            return OperateSectionResult<T>.Denied(
                OperateSectionStatus.Unavailable,
                "The honua-server monitoring API is unreachable or returned an unreadable response.");
        }
    }

    private static Uri BuildUri(Uri baseUri, string relativePath) =>
        ConsoleServerHttp.BuildUri(baseUri, relativePath);

    private static OperateSectionStatus MapStatus(HttpStatusCode code) => code switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => OperateSectionStatus.Forbidden,
        HttpStatusCode.NotFound => OperateSectionStatus.Missing,
        HttpStatusCode.NotImplemented => OperateSectionStatus.Unsupported,
        _ => OperateSectionStatus.Unavailable
    };
}
