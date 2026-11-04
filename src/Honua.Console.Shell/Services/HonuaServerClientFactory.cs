using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Shell.Services;

// The fallback handler bounds connection lifetime so a long-lived client does not pin stale DNS for the active environment.
public static class HonuaServerClientFactory
{
    // On the browser host an unresolved operator fails closed for this privileged client.
    public static HttpClient Create(IServiceProvider serviceProvider, Uri baseUri, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(baseUri);

        var factory = serviceProvider.GetService<IHonuaServerBoundClientFactory>();
        return factory is not null
            ? factory.CreateServerBoundClient(baseUri, timeout)
            : CreateSelfContained(serviceProvider, baseUri, timeout);
    }

    // Anonymous callers are accepted for the public catalog and OGC styles, and the operator bearer is still forwarded when it is present.
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
            serviceProvider.GetRequiredService<IConsoleAccountSessionStore>(),
            baseUri)
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
