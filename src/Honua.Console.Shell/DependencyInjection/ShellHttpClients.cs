using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Shell.DependencyInjection;

internal static class ShellHttpClients
{
    // Generation, LLM and large-upload requests legitimately run for minutes; HttpClient's 100 s default
    // would cancel them and report a false "unreachable". This matches the server's 10-minute request timeout.
    public static readonly TimeSpan LongRequestTimeout = TimeSpan.FromMinutes(10);

    public static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);

    // These clients build absolute request URIs from the active environment profile.
    private static readonly Uri UnboundBaseUri = new("https://unbound.invalid/");

    public static HttpClient ManagerBound(IServiceProvider services) =>
        services.GetService<IHonuaServerBoundClientFactory>() is { } factory
            ? factory.CreateManagerBoundClient(UnboundBaseUri, ReadTimeout)
            : Standalone(ReadTimeout);

    public static HttpClient ServerBound(IServiceProvider services) => ServerBound(services, ReadTimeout);

    public static HttpClient ServerBoundUpload(IServiceProvider services) => ServerBound(services, LongRequestTimeout);

    public static HttpClient Standalone(TimeSpan timeout) => new(CreateBoundedLifetimeHandler()) { Timeout = timeout };

    private static HttpClient ServerBound(IServiceProvider services, TimeSpan timeout) =>
        services.GetService<IHonuaServerBoundClientFactory>() is { } factory
            ? factory.CreateServerBoundClient(UnboundBaseUri, timeout)
            : Standalone(timeout);

    // Recycling pooled connections keeps a long-lived singleton client from pinning stale DNS.
    private static SocketsHttpHandler CreateBoundedLifetimeHandler() =>
        new() { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
}
