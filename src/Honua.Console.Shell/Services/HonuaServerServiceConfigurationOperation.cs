using System.Net;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Live implementation of the service-configuration operations. Drives honua-server's admin layer
/// enable/disable and service-settings endpoints through <see cref="IHonuaAdminOperateClient"/> and maps the
/// server result (or rejection) into a <see cref="ServiceConfigurationResult"/>. It never fabricates
/// success — every result reflects what the server read back (Wave 5).
/// </summary>
public sealed class HonuaServerServiceConfigurationOperation : IServiceConfigurationOperation
{
    private readonly IHonuaAdminOperateClient _client;

    public HonuaServerServiceConfigurationOperation(IHonuaAdminOperateClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<ServiceSettingsView> GetSettingsAsync(
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        var result = await _client.GetServiceSettingsAsync(serviceName, cancellationToken).ConfigureAwait(false);
        if (result.Data is { } settings)
        {
            return new ServiceSettingsView
            {
                Bound = true,
                ServiceName = settings.ServiceName ?? serviceName,
                EnabledProtocols = settings.EnabledProtocols ?? [],
                AvailableProtocols = settings.AvailableProtocols ?? [],
                AllowAnonymous = settings.AccessPolicy?.AllowAnonymous ?? false,
                AllowAnonymousWrite = settings.AccessPolicy?.AllowAnonymousWrite ?? false,
                AllowedRoles = settings.AccessPolicy?.AllowedRoles ?? [],
                AllowedWriteRoles = settings.AccessPolicy?.AllowedWriteRoles ?? [],
                MapServer = settings.MapServer is { } map
                    ? new ServiceMapServerSettingsView
                    {
                        MaxImageWidth = map.MaxImageWidth,
                        MaxImageHeight = map.MaxImageHeight,
                        DefaultImageWidth = map.DefaultImageWidth,
                        DefaultImageHeight = map.DefaultImageHeight,
                        DefaultDpi = map.DefaultDpi,
                        DefaultFormat = map.DefaultFormat,
                        DefaultTransparent = map.DefaultTransparent,
                        MaxFeaturesPerLayer = map.MaxFeaturesPerLayer
                    }
                    : null
            };
        }

        return ServiceSettingsView.Unbound(
            serviceName,
            result.Issue?.Detail ?? "The Honua server did not return settings for this service.");
    }

    public async Task<ServiceConfigurationResult> SetLayerEnabledAsync(
        ServiceLayerEnableCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await _client
            .SetLayerEnabledAsync(command.ConnectionId, command.LayerId, command.Enabled, command.ServiceName, cancellationToken)
            .ConfigureAwait(false);

        if (result.Data is { } layer)
        {
            var enabled = layer.Enabled ?? command.Enabled;
            return new ServiceConfigurationResult
            {
                Succeeded = true,
                State = enabled ? "Enabled" : "Disabled",
                Detail = enabled
                    ? "The layer is enabled and queryable through its FeatureServer slot."
                    : "The layer is disabled and no longer exposed through its FeatureServer slot.",
                LayerId = layer.LayerId,
                ServiceName = layer.ServiceName ?? command.ServiceName,
                Enabled = enabled
            };
        }

        return Failure(result.Issue);
    }

    public async Task<ServiceConfigurationResult> UpdateProtocolsAsync(
        ServiceProtocolsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await _client
            .UpdateServiceProtocolsAsync(command.ServiceName, command.EnabledProtocols, cancellationToken)
            .ConfigureAwait(false);

        if (result.Data is { } settings)
        {
            return new ServiceConfigurationResult
            {
                Succeeded = true,
                State = "Updated",
                Detail = "The service's enabled protocols were updated on honua-server.",
                ServiceName = settings.ServiceName ?? command.ServiceName,
                EnabledProtocols = settings.EnabledProtocols ?? []
            };
        }

        return Failure(result.Issue);
    }

    public async Task<ServiceConfigurationResult> UpdateAccessPolicyAsync(
        ServiceAccessPolicyCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = new HonuaAdminUpdateAccessPolicyRequest
        {
            AllowAnonymous = command.AllowAnonymous,
            AllowAnonymousWrite = command.AllowAnonymousWrite,
            AllowedRoles = command.AllowedRoles,
            AllowedWriteRoles = command.AllowedWriteRoles
        };

        var result = await _client
            .UpdateServiceAccessPolicyAsync(command.ServiceName, request, cancellationToken)
            .ConfigureAwait(false);

        if (result.Data is { } settings)
        {
            return new ServiceConfigurationResult
            {
                Succeeded = true,
                State = "Updated",
                Detail = "The service's access policy was updated on honua-server.",
                ServiceName = settings.ServiceName ?? command.ServiceName,
                EnabledProtocols = settings.EnabledProtocols ?? []
            };
        }

        return Failure(result.Issue);
    }

    public async Task<ServiceConfigurationResult> UpdateMapServerSettingsAsync(
        ServiceMapServerSettingsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = new HonuaAdminUpdateMapServerSettingsRequest
        {
            MaxImageWidth = command.MaxImageWidth,
            MaxImageHeight = command.MaxImageHeight,
            DefaultImageWidth = command.DefaultImageWidth,
            DefaultImageHeight = command.DefaultImageHeight,
            DefaultDpi = command.DefaultDpi,
            DefaultFormat = command.DefaultFormat,
            DefaultTransparent = command.DefaultTransparent,
            MaxFeaturesPerLayer = command.MaxFeaturesPerLayer
        };

        var result = await _client
            .UpdateServiceMapServerSettingsAsync(command.ServiceName, request, cancellationToken)
            .ConfigureAwait(false);

        if (result.Data is { } settings)
        {
            return new ServiceConfigurationResult
            {
                Succeeded = true,
                State = "Updated",
                Detail = "The service's MapServer render settings were updated on honua-server.",
                ServiceName = settings.ServiceName ?? command.ServiceName,
                EnabledProtocols = settings.EnabledProtocols ?? []
            };
        }

        // The server PUT may answer 501 Not Implemented on a build that has not landed the MapServer-settings
        // write path (a known V2 gap). The client maps 501 -> "Unsupported"; surface that honestly with a
        // MapServer-specific message instead of fabricating a success (Console Patterns Charter section 11).
        if (result.Issue is { } issue
            && (issue.StatusCode == (int)HttpStatusCode.NotImplemented
                || string.Equals(issue.State, "Unsupported", StringComparison.OrdinalIgnoreCase)))
        {
            return new ServiceConfigurationResult
            {
                Succeeded = false,
                State = "Unsupported",
                Detail = "MapServer settings are not supported on this server "
                    + $"(it answered: {issue.Detail}).",
                ServiceName = command.ServiceName,
                FieldErrors = MapFieldErrors(issue)
            };
        }

        return Failure(result.Issue);
    }

    private static ServiceConfigurationResult Failure(HonuaAdminEndpointIssue? issue) => new()
    {
        Succeeded = false,
        State = issue?.State ?? "Unavailable",
        Detail = issue?.Detail ?? "The Honua server did not accept the service-configuration request.",
        FieldErrors = MapFieldErrors(issue)
    };

    private static IReadOnlyList<ServiceConfigurationFieldError> MapFieldErrors(HonuaAdminEndpointIssue? issue) =>
        (issue?.FieldErrors ?? [])
            .Select(error => new ServiceConfigurationFieldError(
                error.Code,
                error.Message,
                error.Path,
                error.FieldId,
                error.Severity))
            .ToArray();
}
