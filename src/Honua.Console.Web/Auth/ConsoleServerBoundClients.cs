using System.Net.Http;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Honua.Console.Web.Auth;

/// <summary>
/// Registers the browser Web host's implementation of the Family-A server-bound client seam
/// (honua-console#254): two <see cref="IHttpClientFactory"/> named clients — one PRIVILEGED (fail-closed
/// for an unresolved operator) and one legitimately-ANONYMOUS-capable — both over a shared, managed
/// connection pool, and an <see cref="IHonuaServerBoundClientFactory"/> that hands them to the Family-A
/// typed-client registrations in place of the self-contained per-client pooled handler.
///
/// Connection-pool lifecycle: each named client uses <see cref="HttpClientFactoryBuilderExtensions"/>'s
/// <c>ConfigurePrimaryHttpMessageHandler</c> with a bounded <see cref="SocketsHttpHandler"/> pool that
/// IHttpClientFactory manages/rotates, so the whole multi-operator host shares ONE pool per named client
/// (instead of one <see cref="SocketsHttpHandler"/> per singleton typed client) while a long-lived client
/// never pins stale DNS for the active environment's server.
/// </summary>
public static class ConsoleServerBoundClients
{
    /// <summary>Named client for the PRIVILEGED server-bound surface (fail-closed for an unresolved operator).</summary>
    public const string ServerBoundClientName = "honua-server-bound";

    /// <summary>Named client for the legitimately-ANONYMOUS-capable surface (/public, /ogc/styles).</summary>
    public const string PublicClientName = "honua-server-public";

    public static IServiceCollection AddConsoleServerBoundClients(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The privileged chain: operator guard (fail-closed) OUTERMOST, then the shared profile/session
        // binding (retarget + operator bearer / admin-key), then the pooled primary handler. The guard
        // ensures an unresolved operator can never reach the binding step.
        services.AddHttpClient(ServerBoundClientName)
            .ConfigurePrimaryHttpMessageHandler(CreatePooledPrimaryHandler)
            .AddHttpMessageHandler(serviceProvider =>
                new ConsoleServerBoundOperatorGuardHandler(
                    serviceProvider.GetRequiredService<IConsoleOperatorContext>()))
            .AddHttpMessageHandler(serviceProvider =>
                new HonuaServerBindingHandler(
                    serviceProvider.GetRequiredService<IConsoleEnvironmentProfileStore>(),
                    serviceProvider.GetRequiredService<IConsoleAccountSessionStore>()));

        // The anonymous-capable chain: same binding (forwards the operator bearer WHEN resolved) but NO
        // fail-closed guard, so /public open-data + the public /ogc/styles list keep rendering for
        // anonymous visitors (documented admin-key / anonymous fallback), by explicit design.
        services.AddHttpClient(PublicClientName)
            .ConfigurePrimaryHttpMessageHandler(CreatePooledPrimaryHandler)
            .AddHttpMessageHandler(serviceProvider =>
                new HonuaServerBindingHandler(
                    serviceProvider.GetRequiredService<IConsoleEnvironmentProfileStore>(),
                    serviceProvider.GetRequiredService<IConsoleAccountSessionStore>()));

        services.TryAddSingleton<IHonuaServerBoundClientFactory, HttpClientFactoryServerBoundClientFactory>();

        return services;
    }

    private static SocketsHttpHandler CreatePooledPrimaryHandler() =>
        new()
        {
            // Refresh pooled connections so a long-lived client does not pin stale DNS for the active
            // environment's server.
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };

    private sealed class HttpClientFactoryServerBoundClientFactory : IHonuaServerBoundClientFactory
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public HttpClientFactoryServerBoundClientFactory(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        }

        public HttpClient CreateServerBoundClient(Uri baseUri, TimeSpan? timeout = null) =>
            Configure(_httpClientFactory.CreateClient(ServerBoundClientName), baseUri, timeout);

        public HttpClient CreatePublicClient(Uri baseUri, TimeSpan? timeout = null) =>
            Configure(_httpClientFactory.CreateClient(PublicClientName), baseUri, timeout);

        private static HttpClient Configure(HttpClient client, Uri baseUri, TimeSpan? timeout)
        {
            client.BaseAddress = baseUri;
            if (timeout is { } configuredTimeout)
            {
                client.Timeout = configuredTimeout;
            }

            return client;
        }
    }
}
