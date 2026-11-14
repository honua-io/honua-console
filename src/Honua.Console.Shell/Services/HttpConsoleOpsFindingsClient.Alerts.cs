using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

public sealed partial class HttpConsoleOpsFindingsClient
{
    public async Task<OperateSectionResult<ConsoleAlertInboxSnapshot>> ListAlertsAsync(
        CancellationToken cancellationToken = default, string? cursor = null, ConsoleAlertAuthority? expectedAuthority = null)
    {
        var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return OperateSectionResult<ConsoleAlertInboxSnapshot>.Denied(
                OperateSectionStatus.Unavailable, NoProfileMessage);
        }

        var authority = ConsoleAlertAuthority.Capture(profile,
            await _sessionStore.GetSessionAsync(profile.Id, cancellationToken).ConfigureAwait(false));
        if (expectedAuthority is not null && authority != expectedAuthority)
        {
            return OperateSectionResult<ConsoleAlertInboxSnapshot>.Denied(OperateSectionStatus.Conflict,
                "The connected environment changed. Refresh alerts before continuing.");
        }

        var path = $"{OperateAdminRoutes.Alerts}?pageSize=50";
        if (!string.IsNullOrWhiteSpace(cursor)) { path += $"&cursor={Uri.EscapeDataString(cursor)}"; }
        using var request = new HttpRequestMessage(HttpMethod.Get,
            ConsoleServerHttp.BuildUri(profile.ServerBaseUri, path));
        await ConsoleServerHttp.AttachAuthenticationAsync(request, _sessionStore, profile, _adminApiKey,
            cancellationToken).ConfigureAwait(false);
        var result = await SendAlertRequestAsync(request,
            OperateObservabilityJsonContext.Default.ObservabilityAlertEventPageResponse,
            cancellationToken).ConfigureAwait(false);
        if (!await IsAlertAuthorityCurrentAsync(authority, cancellationToken).ConfigureAwait(false))
        {
            return OperateSectionResult<ConsoleAlertInboxSnapshot>.Denied(OperateSectionStatus.Conflict,
                "The connected environment changed. Refresh alerts before continuing.");
        }

        return result.IsAllowed && result.Value is { } page
            ? OperateSectionResult<ConsoleAlertInboxSnapshot>.Allowed(new(authority, page))
            : OperateSectionResult<ConsoleAlertInboxSnapshot>.Denied(result.Status, result.Message);
    }

    public async Task<OperateSectionResult<ObservabilityAlertEventResponse>> AcknowledgeAlertAsync(
        ConsoleAlertAcknowledgement acknowledgement, CancellationToken cancellationToken = default)
    {
        var (eventId, authority, requestId) = acknowledgement;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(eventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return OperateSectionResult<ObservabilityAlertEventResponse>.Denied(
                OperateSectionStatus.Unavailable, NoProfileMessage);
        }

        if (authority != ConsoleAlertAuthority.Capture(profile,
            await _sessionStore.GetSessionAsync(profile.Id, cancellationToken).ConfigureAwait(false)))
        {
            return OperateSectionResult<ObservabilityAlertEventResponse>.Denied(OperateSectionStatus.Conflict,
                "The connected environment changed. Refresh alerts before acknowledging one.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post,
            ConsoleServerHttp.BuildUri(profile.ServerBaseUri, OperateAdminRoutes.AlertAcknowledge(eventId)));
        request.Headers.Add("Idempotency-Key", requestId);
        request.Content = JsonContent.Create(new ObservabilityAlertAcknowledgeRequest(),
            OperateObservabilityJsonContext.Default.ObservabilityAlertAcknowledgeRequest);
        var authentication = await ConsoleServerHttp.AttachMutationAuthenticationAsync(request,
            _operatorBearerProvider, profile, _adminApiKey, _credentialMode, cancellationToken).ConfigureAwait(false);
        if (!authentication.IsAuthenticated)
        {
            return OperateSectionResult<ObservabilityAlertEventResponse>.Denied(
                OperateSectionStatus.Forbidden, authentication.Message);
        }

        var result = await SendAlertRequestAsync(request,
            OperateObservabilityJsonContext.Default.ObservabilityAlertEventResponse,
            cancellationToken).ConfigureAwait(false);
        if (!await IsAlertAuthorityCurrentAsync(authority, cancellationToken).ConfigureAwait(false))
        {
            return OperateSectionResult<ObservabilityAlertEventResponse>.Denied(OperateSectionStatus.Conflict,
                "The connected environment changed. Refresh alerts before continuing.");
        }

        return result.IsAllowed && (result.Value is not { } alert || alert.EventId != eventId || alert.AcknowledgedAt is null)
            ? OperateSectionResult<ObservabilityAlertEventResponse>.Denied(OperateSectionStatus.Unavailable,
                "Honua has not confirmed the acknowledgement. Refresh the alerts before trying again.")
            : result;
    }

    private async Task<bool> IsAlertAuthorityCurrentAsync(ConsoleAlertAuthority authority, CancellationToken cancellationToken)
    {
        var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        return profile is not null && authority == ConsoleAlertAuthority.Capture(profile,
            await _sessionStore.GetSessionAsync(profile.Id, cancellationToken).ConfigureAwait(false));
    }

    private async Task<OperateSectionResult<T>> SendAlertRequestAsync<T>(HttpRequestMessage request,
        JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead,
                budget.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return OperateSectionResult<T>.Denied(MapStatus(response.StatusCode),
                    $"Honua could not complete the alert request (HTTP {(int)response.StatusCode}).");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(budget.Token).ConfigureAwait(false);
            var value = await JsonSerializer.DeserializeAsync(stream, typeInfo, budget.Token).ConfigureAwait(false);
            return value is null
                ? OperateSectionResult<T>.Denied(OperateSectionStatus.Unavailable, "Honua returned an empty alert response.")
                : OperateSectionResult<T>.Allowed(value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or JsonException)
        {
            return OperateSectionResult<T>.Denied(OperateSectionStatus.Unavailable,
                "The alert request could not be confirmed. Refresh before trying again.");
        }
    }
}
