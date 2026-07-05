using System.Net;
using System.Net.Http;
using System.Text.Json;
using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Binds the Copilot Findings surface to a real honua-server through the deterministic
/// ops-findings endpoints (group <c>/api/v1/admin/observability</c>, admin-authorized,
/// bare JSON — NO ApiResponse envelope). Base address comes from the active
/// <see cref="ConsoleEnvironmentProfile"/>; the admin API key is sent as
/// <c>X-API-Key</c>. Deserialization is source-generated for trim/AOT safety.
///
/// Charter section 11 (no standing mock for server-owned data) is preserved: this client
/// never returns seeded findings. When no environment profile is bound, every call
/// returns a missing-binding (<see cref="OperateSectionStatus.Unavailable"/>) result.
/// Mirrors <see cref="HttpConsoleMonitoringMetricsClient"/> and
/// <see cref="HttpConsoleDeployApprovalClient"/>.
/// </summary>
public sealed class HttpConsoleOpsFindingsClient : IConsoleOpsFindingsClient
{
    private const string NoProfileMessage =
        "No active environment profile is selected. Connect an environment to load ops findings.";

    private readonly HttpClient _http;
    private readonly IConsoleEnvironmentProfileStore _profileStore;
    private readonly string? _adminApiKey;

    /// <summary>Initializes a new instance of the <see cref="HttpConsoleOpsFindingsClient"/> class.</summary>
    /// <param name="http">The shared HTTP client.</param>
    /// <param name="profileStore">The active-environment profile store.</param>
    /// <param name="adminApiKey">The admin API key sent as <c>X-API-Key</c>, when configured.</param>
    public HttpConsoleOpsFindingsClient(
        HttpClient http,
        IConsoleEnvironmentProfileStore profileStore,
        string? adminApiKey = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        _adminApiKey = string.IsNullOrWhiteSpace(adminApiKey) ? null : adminApiKey;
    }

    /// <inheritdoc />
    public async Task<OperateSectionResult<OpsFindingsListResponse>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return OperateSectionResult<OpsFindingsListResponse>.Denied(
                OperateSectionStatus.Unavailable,
                NoProfileMessage);
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            ConsoleServerHttp.BuildUri(profile.ServerBaseUri, OpsFindingsRoutes.List));
        AttachAdminKey(request);

        try
        {
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return OperateSectionResult<OpsFindingsListResponse>.Denied(
                    MapStatus(response.StatusCode),
                    $"The honua-server ops-findings API returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var value = await JsonSerializer.DeserializeAsync(
                stream,
                OpsFindingsJsonContext.Default.OpsFindingsListResponse,
                cancellationToken).ConfigureAwait(false);

            return value is null
                ? OperateSectionResult<OpsFindingsListResponse>.Denied(
                    OperateSectionStatus.Unavailable,
                    "The honua-server ops-findings API returned an empty response.")
                : OperateSectionResult<OpsFindingsListResponse>.Allowed(value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            return OperateSectionResult<OpsFindingsListResponse>.Denied(
                OperateSectionStatus.Unavailable,
                "The honua-server ops-findings API is unreachable or returned an unreadable response.");
        }
    }

    /// <inheritdoc />
    public async Task<OperateSectionResult<OpsFindingProposeResponse>> ProposeAsync(
        string findingId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(findingId);

        var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return OperateSectionResult<OpsFindingProposeResponse>.Denied(
                OperateSectionStatus.Unavailable,
                NoProfileMessage);
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            ConsoleServerHttp.BuildUri(profile.ServerBaseUri, OpsFindingsRoutes.Propose(findingId)));
        AttachAdminKey(request);

        try
        {
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                // A 404 means the finding's condition cleared (or it never carried an
                // action) between listing and proposing — the page treats this as a
                // stale-finding refresh cue rather than a hard failure.
                var message = response.StatusCode == HttpStatusCode.NotFound
                    ? "This finding's condition has cleared or it no longer has a recommended action. The list has been refreshed."
                    : $"The honua-server ops-findings API returned {(int)response.StatusCode} {response.ReasonPhrase}.";
                return OperateSectionResult<OpsFindingProposeResponse>.Denied(MapStatus(response.StatusCode), message);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var value = await JsonSerializer.DeserializeAsync(
                stream,
                OpsFindingsJsonContext.Default.OpsFindingProposeResponse,
                cancellationToken).ConfigureAwait(false);

            return value is null
                ? OperateSectionResult<OpsFindingProposeResponse>.Denied(
                    OperateSectionStatus.Unavailable,
                    "The honua-server ops-findings API returned an empty propose response.")
                : OperateSectionResult<OpsFindingProposeResponse>.Allowed(value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            return OperateSectionResult<OpsFindingProposeResponse>.Denied(
                OperateSectionStatus.Unavailable,
                "The honua-server ops-findings API is unreachable or returned an unreadable response.");
        }
    }

    private void AttachAdminKey(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_adminApiKey))
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", _adminApiKey);
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
