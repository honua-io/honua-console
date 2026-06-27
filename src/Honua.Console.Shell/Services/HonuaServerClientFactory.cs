using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Builds the typed honua-server HttpClients used by the Family-A bindings so they share ONE
/// profile/session-aware request-time binding (honua-console#234) via <see cref="HonuaServerBindingHandler"/>.
/// Each client gets its own handler instance (a <see cref="DelegatingHandler"/> cannot be shared)
/// over a pooled <see cref="SocketsHttpHandler"/> whose connection lifetime is bounded so a
/// long-lived singleton client does not pin stale DNS for the active environment's server.
/// </summary>
public static class HonuaServerClientFactory
{
    public static HttpClient Create(IServiceProvider serviceProvider, Uri baseUri, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(baseUri);

        var bindingHandler = new HonuaServerBindingHandler(
            serviceProvider.GetRequiredService<IConsoleEnvironmentProfileStore>(),
            serviceProvider.GetRequiredService<IConsoleAccountSessionStore>())
        {
            InnerHandler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            }
        };

        var client = new HttpClient(bindingHandler)
        {
            BaseAddress = baseUri
        };

        if (timeout is { } configuredTimeout)
        {
            client.Timeout = configuredTimeout;
        }

        return client;
    }
}
