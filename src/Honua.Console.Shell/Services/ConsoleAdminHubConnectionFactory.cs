using Honua.Console.Shell.Security;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Shell.Services;

// Hub connections require the operator's forwardable bearer and never fall back to a shared API key; null means no environment is bound, not a connection failure.
internal static class ConsoleAdminHubConnectionFactory
{
    public static async Task<HubConnection?> CreateAsync(
        IConsoleEnvironmentProfileStore profileStore,
        IConsoleAccountSessionStore sessions,
        string? adminApiKey,
        string hubPath,
        CancellationToken cancellationToken = default,
        IConsoleManagerEndpoint? managerEndpoint = null)
    {
        ArgumentNullException.ThrowIfNull(profileStore);
        ArgumentNullException.ThrowIfNull(sessions);

        var profile = await profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return null;
        }

        var bearer = await ConsoleServerHttp
            .ResolveForwardableBearerAsync(sessions, profile, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(bearer))
        {
            return null;
        }

        var target = managerEndpoint is null ? profile.ServerBaseUri : managerEndpoint.Resolve(profile.ServerBaseUri);
        if (target is null) return null;
        var hubUri = ConsoleServerHttp.BuildUri(target, hubPath);

        return new HubConnectionBuilder()
            .WithUrl(hubUri, options =>
            {
                options.AccessTokenProvider = async () =>
                {
                    var current = await profileStore.GetActiveProfileAsync();
                    var token = current?.Id == profile.Id && current.ServerBaseUri == profile.ServerBaseUri
                        ? await ConsoleServerHttp.ResolveForwardableBearerAsync(sessions, current, CancellationToken.None)
                        : null;
                    return !string.IsNullOrWhiteSpace(token)
                        ? token
                        : throw new HttpRequestException("Sign in to honua-server again.", null, System.Net.HttpStatusCode.Unauthorized);
                };
            })
            .WithAutomaticReconnect()
            .AddJsonProtocol(o => o.PayloadSerializerOptions.PropertyNameCaseInsensitive = true)
            .Build();
    }
}
