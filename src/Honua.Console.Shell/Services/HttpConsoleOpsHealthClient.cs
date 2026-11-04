using System.Net;
using System.Net.Http;
using System.Text.Json;
using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

// The ops-health snapshot is bare JSON with no ApiResponse envelope.
public sealed class HttpConsoleOpsHealthClient : IConsoleOpsHealthClient
{
    private const string NoProfileMessage =
        "No active environment profile is selected. Connect an environment to load the ops-health snapshot.";

    private readonly HttpClient _http;
    private readonly IConsoleEnvironmentProfileStore _profileStore;
    private readonly string? _adminApiKey;

    public HttpConsoleOpsHealthClient(
        HttpClient http,
        IConsoleEnvironmentProfileStore profileStore,
        string? adminApiKey = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        _adminApiKey = string.IsNullOrWhiteSpace(adminApiKey) ? null : adminApiKey;
    }

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

    public async Task<OperateSectionResult<OpsHealthHistoryResponse>> GetHistoryAsync(
        ConsoleOpsHealthHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return OperateSectionResult<OpsHealthHistoryResponse>.Denied(
                OperateSectionStatus.Unavailable,
                NoProfileMessage);
        }

        var route =
            $"{OpsHealthRoutes.History}?window={Uri.EscapeDataString(query.Window)}" +
            $"&resolution={Uri.EscapeDataString(query.Resolution)}" +
            $"&perReplica={(query.PerReplica ? "true" : "false")}";

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            ConsoleServerHttp.BuildUri(profile.ServerBaseUri, route));
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
                return OperateSectionResult<OpsHealthHistoryResponse>.Denied(
                    MapStatus(response.StatusCode),
                    $"The honua-server ops-health history API returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var value = await JsonSerializer.DeserializeAsync(
                stream,
                OpsHealthJsonContext.Default.OpsHealthHistoryResponse,
                cancellationToken).ConfigureAwait(false);

            return value is null
                ? OperateSectionResult<OpsHealthHistoryResponse>.Denied(
                    OperateSectionStatus.Unavailable,
                    "The honua-server ops-health history API returned an empty response.")
                : OperateSectionResult<OpsHealthHistoryResponse>.Allowed(value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            return OperateSectionResult<OpsHealthHistoryResponse>.Denied(
                OperateSectionStatus.Unavailable,
                "The honua-server ops-health history API is unreachable or returned an unreadable response.");
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
