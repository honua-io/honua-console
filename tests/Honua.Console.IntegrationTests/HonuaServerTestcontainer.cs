using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Testcontainers.PostgreSql;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Boots a real honua-server container backed by a PostgreSQL/PostGIS container on a shared network,
/// honouring <see cref="ConsoleTrustIntegrationOptions"/> (image, port, scheme, health path, env). Shared
/// by the trust and Studio integration fixtures so the container-boot mechanics live in one place. The
/// caller owns skip-on-failure semantics: <see cref="StartAsync"/> throws when the daemon/image/readiness
/// is unavailable, and the fixture records the skip reason.
/// </summary>
internal sealed class HonuaServerTestcontainer : IAsyncDisposable
{
    private INetwork? _network;
    private PostgreSqlContainer? _postgres;
    private IContainer? _server;

    private HonuaServerTestcontainer(INetwork network, PostgreSqlContainer postgres, IContainer server, Uri baseAddress)
    {
        _network = network;
        _postgres = postgres;
        _server = server;
        BaseAddress = baseAddress;
    }

    public Uri BaseAddress { get; }

    public static async Task<HonuaServerTestcontainer> StartAsync(
        ConsoleTrustIntegrationOptions options,
        CancellationToken cancellationToken)
    {
        INetwork? network = null;
        PostgreSqlContainer? postgres = null;
        IContainer? server = null;
        try
        {
            network = new NetworkBuilder().Build();

            postgres = new PostgreSqlBuilder()
                .WithImage("postgis/postgis:16-3.4")
                .WithNetwork(network)
                .WithNetworkAliases("postgres")
                .WithDatabase("honua")
                .WithUsername("honua")
                .WithPassword("honua")
                .Build();
            await postgres.StartAsync(cancellationToken).ConfigureAwait(false);

            const string connectionString = "Host=postgres;Port=5432;Database=honua;Username=honua;Password=honua";
            var useTls = string.Equals(options.ServerScheme, "https", StringComparison.OrdinalIgnoreCase);

            var builder = new ContainerBuilder(options.ServerImage!)
                .WithNetwork(network)
                .WithPortBinding(options.ServerPort, assignRandomHostPort: true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request =>
                {
                    request = request
                        .ForPort(options.ServerPort)
                        .ForPath(options.ServerHealthPath)
                        .ForStatusCodeMatching(code => (int)code is >= 200 and < 500);

                    if (useTls)
                    {
                        // The fixture server presents a self-signed/dev certificate; accept it for the readiness probe only.
                        request = request
                            .UsingTls()
                            .UsingHttpMessageHandler(new HttpClientHandler
                            {
                                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                            });
                    }

                    return request;
                }));

            foreach (var (key, value) in options.BuildServerEnvironment(connectionString))
            {
                builder = builder.WithEnvironment(key, value);
            }

            server = builder.Build();
            await server.StartAsync(cancellationToken).ConfigureAwait(false);

            var baseAddress = new UriBuilder(
                options.ServerScheme,
                server.Hostname,
                server.GetMappedPublicPort(options.ServerPort)).Uri;
            return new HonuaServerTestcontainer(network, postgres, server, baseAddress);
        }
        catch
        {
            await DisposeQuietlyAsync(server, postgres, network).ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeQuietlyAsync(_server, _postgres, _network).ConfigureAwait(false);
        _server = null;
        _postgres = null;
        _network = null;
    }

    private static async ValueTask DisposeQuietlyAsync(
        IContainer? server,
        PostgreSqlContainer? postgres,
        INetwork? network)
    {
        if (server is not null)
        {
            await server.DisposeAsync().ConfigureAwait(false);
        }

        if (postgres is not null)
        {
            await postgres.DisposeAsync().ConfigureAwait(false);
        }

        if (network is not null)
        {
            await network.DisposeAsync().ConfigureAwait(false);
        }
    }
}
