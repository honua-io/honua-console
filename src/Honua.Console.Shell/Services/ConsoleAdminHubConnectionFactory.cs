using Honua.Console.Shell.Security;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Builds a <see cref="HubConnection"/> to a honua-server admin realtime hub for the active
/// environment profile (console#293 shared seam, extracted from
/// <c>SignalRConsoleProposalRealtimeClient</c>, honua-server #1695).
///
/// Every admin-hub-backed realtime client connects the same way: resolve the active environment
/// profile, resolve the operator's forwardable bearer (falling back to the shared admin
/// <c>X-API-Key</c>), and build the connection with automatic reconnect and case-insensitive
/// JSON. Centralizing this here means the next admin-hub client (deploy operations, health) does
/// not re-derive the auth decision — the same rule <see cref="ConsoleServerHttp"/> centralizes
/// for the Family-B REST clients applies to hub connections too.
/// </summary>
internal static class ConsoleAdminHubConnectionFactory
{
    /// <summary>
    /// Resolves the active profile and builds a hub connection to <paramref name="hubPath"/> on
    /// its server. Returns <see langword="null"/> when no environment profile is bound — the
    /// caller should treat this as <see cref="ConsoleRealtimeConnectionState.NotConfigured"/>,
    /// not a failure; there is nothing to connect to.
    /// </summary>
    public static async Task<HubConnection?> CreateAsync(
        IConsoleEnvironmentProfileStore profileStore,
        IConsoleAccountSessionStore sessions,
        string? adminApiKey,
        string hubPath,
        CancellationToken cancellationToken = default)
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

        var hubUri = ConsoleServerHttp.BuildUri(profile.ServerBaseUri, hubPath);

        return new HubConnectionBuilder()
            .WithUrl(hubUri, options =>
            {
                if (!string.IsNullOrWhiteSpace(adminApiKey))
                {
                    options.Headers["X-API-Key"] = adminApiKey;
                }

                if (!string.IsNullOrWhiteSpace(bearer) && !ConsoleAuthConstants.IsSessionSentinel(bearer))
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(bearer);
                }
            })
            .WithAutomaticReconnect()
            .AddJsonProtocol(o => o.PayloadSerializerOptions.PropertyNameCaseInsensitive = true)
            .Build();
    }
}
