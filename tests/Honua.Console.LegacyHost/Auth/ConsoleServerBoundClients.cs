using System.Net.Http;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Honua.Console.Web.Auth;

public static class ConsoleServerBoundClients
{
    public const string ServerBoundClientName = "honua-server-bound";

    public const string PublicClientName = "honua-server-public";

    public const string ManagerBoundClientName = "honua-manager-bound";

    public static IServiceCollection AddConsoleServerBoundClients(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton<IConsoleOperatorBearerExchange, UnavailableConsoleOperatorBearerExchange>();
        services.TryAddSingleton<IConsoleOperatorBearerProvider, ConsoleOperatorBearerProvider>();

        services.AddHttpClient("honua-map-proxy")
            .ConfigurePrimaryHttpMessageHandler(CreatePooledPrimaryHandler)
            .AddHttpMessageHandler(serviceProvider => CreateBindingHandler(serviceProvider))
            .AddHttpMessageHandler(serviceProvider => CreateCredentialHandler(serviceProvider));

        // The privileged chain: operator guard (fail-closed) OUTERMOST, then the shared profile/session
        // binding (retarget), then the operator credential boundary and pooled primary handler. The guard
        // ensures an unresolved operator can never reach the binding step.
        services.AddHttpClient(ServerBoundClientName)
            .ConfigurePrimaryHttpMessageHandler(CreatePooledPrimaryHandler)
            .AddHttpMessageHandler(serviceProvider =>
                new ConsoleServerBoundOperatorGuardHandler(
                    serviceProvider.GetRequiredService<IConsoleOperatorContext>()))
            .AddHttpMessageHandler(serviceProvider => CreateBindingHandler(serviceProvider))
            .AddHttpMessageHandler(serviceProvider => CreateCredentialHandler(serviceProvider));

        services.TryAddSingleton<IConsoleManagerEndpoint>(provider =>
            new ConsoleManagerEndpoint(provider.GetService<IConfiguration>()));
        services.AddHttpClient(ManagerBoundClientName)
            .ConfigurePrimaryHttpMessageHandler(CreatePooledPrimaryHandler)
            .AddHttpMessageHandler(provider => new ConsoleServerBoundOperatorGuardHandler(
                provider.GetRequiredService<IConsoleOperatorContext>()))
            .AddHttpMessageHandler(provider => CreateBindingHandler(provider))
            .AddHttpMessageHandler(provider => CreateCredentialHandler(provider))
            .AddHttpMessageHandler(provider => new ConsoleManagerRoutingHandler(
                provider.GetRequiredService<IConsoleEnvironmentProfileStore>(),
                provider.GetRequiredService<IConsoleManagerEndpoint>()));

        // The anonymous-capable chain: same binding (forwards the operator bearer WHEN resolved) but NO
        // fail-closed guard, so /public open-data + the public /ogc/styles list keep rendering for
        // anonymous visitors. The final credential boundary strips all shared keys.
        services.AddHttpClient(PublicClientName)
            .ConfigurePrimaryHttpMessageHandler(CreatePooledPrimaryHandler)
            .AddHttpMessageHandler(serviceProvider => CreateBindingHandler(serviceProvider))
            .AddHttpMessageHandler(serviceProvider => CreateCredentialHandler(serviceProvider, allowAnonymous: true));

        services.TryAddSingleton<Honua.Console.Contracts.IConsoleAuthorityBindingValidator, ConsoleAuthorityBindingValidator>();
        services.TryAddSingleton<IHonuaServerBoundClientFactory, HttpClientFactoryServerBoundClientFactory>();

        return services;
    }

    private static HonuaServerBindingHandler CreateBindingHandler(IServiceProvider services) =>
        new(
            services.GetRequiredService<IConsoleEnvironmentProfileStore>(),
            services.GetRequiredService<IConsoleAccountSessionStore>(),
            StartupServerBase(services));

    private static Uri? StartupServerBase(IServiceProvider services)
    {
        var configuration = services.GetService<IConfiguration>();
        var raw = configuration?["Honua:Server:BaseUrl"] ?? configuration?["HONUA_SERVER_BASE_URL"];
        return Uri.TryCreate(raw, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri
            : null;
    }

    private static ConsoleOperatorCredentialHandler CreateCredentialHandler(IServiceProvider services, bool allowAnonymous = false) =>
        new(services.GetRequiredService<IConsoleOperatorContext>(),
            services.GetRequiredService<IConsoleEnvironmentProfileStore>(),
            services.GetRequiredService<IConsoleAccountSessionStore>(),
            services.GetRequiredService<IConsoleOperatorBearerProvider>(),
            allowAnonymous);

    private static SocketsHttpHandler CreatePooledPrimaryHandler() =>
        new()
        {
            // Refresh pooled connections so a long-lived client does not pin stale DNS for the active
            // environment's server.
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AllowAutoRedirect = false,
            UseCookies = false
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

        public HttpClient CreateManagerBoundClient(Uri baseUri, TimeSpan? timeout = null) =>
            Configure(_httpClientFactory.CreateClient(ManagerBoundClientName), baseUri, timeout);

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
