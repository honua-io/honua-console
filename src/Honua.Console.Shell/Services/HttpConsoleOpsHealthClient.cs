using System.Net;
using System.Net.Http;
using System.Text.Json;
using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Binds the Ops Health surface to a real honua-server through the consolidated
/// ops-health snapshot endpoint (group <c>/api/v1/admin/observability</c>,
/// admin-authorized, bare JSON — NO ApiResponse envelope). Base address comes from the
/// active <see cref="ConsoleEnvironmentProfile"/>; the admin API key is sent as
/// <c>X-API-Key</c>. Deserialization is source-generated for trim/AOT safety.
///
/// Charter section 11 (no standing mock for server-owned data) is preserved: this client
/// never returns seeded data. When no environment profile is bound, the read returns a
/// missing-binding (<see cref="OperateSectionStatus.Unavailable"/>) result. Mirrors
/// <see cref="HttpConsoleMonitoringMetricsClient"/>.
/// </summary>
public sealed class HttpConsoleOpsHealthClient : IConsoleOpsHealthClient
{
    private const string NoProfileMessage =
        "No active environment profile is selected. Connect an environment to load the ops-health snapshot.";

    private readonly HttpClient _http;
    private readonly IConsoleEnvironmentProfileStore _profileStore;
    private readonly string? _adminApiKey;

    /// <summary>Initializes a new instance of the <see cref="HttpConsoleOpsHealthClient"/> class.</summary>
    /// <param name="http">The shared HTTP client.</param>
    /// <param name="profileStore">The active-environment profile store.</param>
    /// <param name="adminApiKey">The admin API key sent as <c>X-API-Key</c>, when configured.</param>
    public HttpConsoleOpsHealthClient(
        HttpClient http,
        IConsoleEnvironmentProfileStore profileStore,
        string? adminApiKey = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        _adminApiKey = string.IsNullOrWhiteSpace(adminApiKey) ? null : adminApiKey;
    }

    /// <inheritdoc />
    public async Task<OperateSectionResult<OpsHealthSnapshotResponse>> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return OperateSectionResult<OpsHealthSnapshotResponse>.Denied(
                OperateSectionStatus.Unavailable,
                NoProfileMessage);
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            ConsoleServerHttp.BuildUri(profile.ServerBaseUri, OpsHealthRoutes.Snapshot));
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
                return OperateSectionResult<OpsHealthSnapshotResponse>.Denied(
                    MapStatus(response.StatusCode),
                    $"The honua-server ops-health API returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var value = await JsonSerializer.DeserializeAsync(
                stream,
                OpsHealthJsonContext.Default.OpsHealthSnapshotResponse,
                cancellationToken).ConfigureAwait(false);

            return value is null
                ? OperateSectionResult<OpsHealthSnapshotResponse>.Denied(
                    OperateSectionStatus.Unavailable,
                    "The honua-server ops-health API returned an empty response.")
                : OperateSectionResult<OpsHealthSnapshotResponse>.Allowed(value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            return OperateSectionResult<OpsHealthSnapshotResponse>.Denied(
                OperateSectionStatus.Unavailable,
                "The honua-server ops-health API is unreachable or returned an unreadable response.");
        }
    }

    private static OperateSectionStatus MapStatus(HttpStatusCode code) => code switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => OperateSectionStatus.Forbidden,
        HttpStatusCode.NotFound => OperateSectionStatus.Missing,
        HttpStatusCode.NotImplemented => OperateSectionStatus.Unsupported,
        _ => OperateSectionStatus.Unavailable
    };
}
