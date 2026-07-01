using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Builds the typed honua-server HttpClients used by the Family-A bindings so they share ONE
/// profile/session-aware request-time binding (honua-console#234) via <see cref="HonuaServerBindingHandler"/>.
///
/// When the host registers an <see cref="IHonuaServerBoundClientFactory"/> (the multi-operator browser Web
/// host does, honua-console#254), the clients are obtained from it: <see cref="System.Net.Http.IHttpClientFactory"/>
/// named clients over a shared, managed connection pool, with a handler chain that fails closed for an
/// unresolved operator on the privileged path. When no such factory is registered (the native
/// single-operator host, host-independent tests) this falls back to a self-contained client: its own
/// <see cref="HonuaServerBindingHandler"/> over a pooled <see cref="SocketsHttpHandler"/> whose connection
/// lifetime is bounded so a long-lived singleton client does not pin stale DNS for the active
/// environment's server.
/// </summary>
public static class HonuaServerClientFactory
{
    /// <summary>
    /// Builds a client for a PRIVILEGED server-bound surface (acts with honua-server privileges). On the
    /// browser Web host the resulting handler chain fails closed for an unresolved operator.
    /// </summary>
    public static HttpClient Create(IServiceProvider serviceProvider, Uri baseUri, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(baseUri);

        var factory = serviceProvider.GetService<IHonuaServerBoundClientFactory>();
        return factory is not null
            ? factory.CreateServerBoundClient(baseUri, timeout)
            : CreateSelfContained(serviceProvider, baseUri, timeout);
    }

    /// <summary>
    /// Builds a client for a legitimately-ANONYMOUS-capable surface (the <c>/public</c> open-data catalog
    /// reads and the public OGC <c>/ogc/styles</c> list): the operator bearer is forwarded when resolved,
    /// but an anonymous caller is tolerated by design (the documented admin-key / anonymous fallback).
    /// </summary>
    public static HttpClient CreatePublic(IServiceProvider serviceProvider, Uri baseUri, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(baseUri);

        var factory = serviceProvider.GetService<IHonuaServerBoundClientFactory>();
        return factory is not null
            ? factory.CreatePublicClient(baseUri, timeout)
            : CreateSelfContained(serviceProvider, baseUri, timeout);
    }

    // Native host / host-independent fallback: one binding handler over a bounded-lifetime pooled handler.
    private static HttpClient CreateSelfContained(IServiceProvider serviceProvider, Uri baseUri, TimeSpan? timeout)
    {
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
