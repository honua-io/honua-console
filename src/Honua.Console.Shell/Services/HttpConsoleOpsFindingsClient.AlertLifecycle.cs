using System.Net.Http.Json;
using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

public sealed partial class HttpConsoleOpsFindingsClient
{
    public async Task<OperateSectionResult<ObservabilityAlertEventResponse>> ChangeAlertLifecycleAsync(
        ConsoleAlertLifecycleCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(command.EventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.RequestId);
        if (!Enum.IsDefined(command.Action)) { throw new ArgumentOutOfRangeException(nameof(command)); }
        var now = DateTimeOffset.UtcNow;
        if (command.Action == ConsoleAlertLifecycleAction.Snooze &&
            (command.SuppressUntil is not { } until || until <= now || until > now.AddDays(7)))
        {
            return OperateSectionResult<ObservabilityAlertEventResponse>.Denied(OperateSectionStatus.Conflict,
                "Choose a snooze time in the next seven days.");
        }

        if (!await IsAlertAuthorityCurrentAsync(command.Authority, cancellationToken).ConfigureAwait(false))
        {
            return AlertLifecycleAuthorityChanged();
        }

        var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null) { return AlertLifecycleAuthorityChanged(); }
        // Read the current event before offering a lifecycle change; never snooze a critical alert.
        using var read = new HttpRequestMessage(HttpMethod.Get,
            ConsoleServerHttp.BuildUri(profile.ServerBaseUri, $"{OperateAdminRoutes.Alerts}/{command.EventId}"));
        await ConsoleServerHttp.AttachAuthenticationAsync(read, _sessionStore, profile, _adminApiKey,
            cancellationToken).ConfigureAwait(false);
        var current = await SendAlertRequestAsync(read,
            OperateObservabilityJsonContext.Default.ObservabilityAlertEventResponse, cancellationToken).ConfigureAwait(false);
        if (!current.IsAllowed) { return current; }
        if (current.Value is not { } alert || alert.EventId != command.EventId)
        {
            return OperateSectionResult<ObservabilityAlertEventResponse>.Denied(OperateSectionStatus.Unavailable,
                "Honua could not confirm the current alert. Refresh before continuing.");
        }
        if (command.Action == ConsoleAlertLifecycleAction.Snooze &&
            (string.IsNullOrWhiteSpace(alert.Severity) || string.Equals(alert.Severity, "critical", StringComparison.OrdinalIgnoreCase)))
        {
            return OperateSectionResult<ObservabilityAlertEventResponse>.Denied(OperateSectionStatus.Forbidden,
                "Critical alerts and alerts with unknown severity cannot be snoozed.");
        }

        var route = command.Action == ConsoleAlertLifecycleAction.Snooze
            ? OperateAdminRoutes.AlertSuppress(command.EventId) : OperateAdminRoutes.AlertResolve(command.EventId);
        using var request = new HttpRequestMessage(HttpMethod.Post, ConsoleServerHttp.BuildUri(profile.ServerBaseUri, route));
        request.Headers.Add("Idempotency-Key", command.RequestId);
        request.Content = command.Action == ConsoleAlertLifecycleAction.Snooze
            ? JsonContent.Create(new ObservabilityAlertSuppressRequest { SuppressUntil = command.SuppressUntil!.Value },
                OperateObservabilityJsonContext.Default.ObservabilityAlertSuppressRequest)
            : JsonContent.Create(new ObservabilityAlertResolveRequest(),
                OperateObservabilityJsonContext.Default.ObservabilityAlertResolveRequest);
        var authentication = await ConsoleServerHttp.AttachMutationAuthenticationAsync(request,
            _operatorBearerProvider, profile, _adminApiKey, _credentialMode, cancellationToken).ConfigureAwait(false);
        if (!await IsAlertAuthorityCurrentAsync(command.Authority, cancellationToken).ConfigureAwait(false))
        {
            return AlertLifecycleAuthorityChanged();
        }
        if (!authentication.IsAuthenticated)
        {
            return OperateSectionResult<ObservabilityAlertEventResponse>.Denied(OperateSectionStatus.Forbidden, authentication.Message);
        }

        var result = await SendAlertRequestAsync(request,
            OperateObservabilityJsonContext.Default.ObservabilityAlertEventResponse, cancellationToken).ConfigureAwait(false);
        if (!await IsAlertAuthorityCurrentAsync(command.Authority, cancellationToken).ConfigureAwait(false))
        {
            return AlertLifecycleAuthorityChanged();
        }
        if (!result.IsAllowed) { return result; }
        var confirmed = result.Value is { } updated && updated.EventId == command.EventId &&
            (command.Action == ConsoleAlertLifecycleAction.Snooze
                ? updated.SuppressedUntil == command.SuppressUntil
                : updated.ResolvedAt is not null);
        return confirmed ? result : OperateSectionResult<ObservabilityAlertEventResponse>.Denied(
            OperateSectionStatus.Unavailable, "Honua has not confirmed the alert change. Refresh before trying again.");
    }

    private static OperateSectionResult<ObservabilityAlertEventResponse> AlertLifecycleAuthorityChanged() =>
        OperateSectionResult<ObservabilityAlertEventResponse>.Denied(OperateSectionStatus.Conflict,
            "The connected environment changed. Refresh alerts before continuing.");
}
