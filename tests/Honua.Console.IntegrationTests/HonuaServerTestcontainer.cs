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
    private IContainer? _redis;

    private HonuaServerTestcontainer(
        INetwork network,
        PostgreSqlContainer postgres,
        IContainer server,
        IContainer redis,
        Uri baseAddress,
        string postgresConnectionString)
    {
        _network = network;
        _postgres = postgres;
        _server = server;
        _redis = redis;
        BaseAddress = baseAddress;
        PostgresConnectionString = postgresConnectionString;
    }

    public Uri BaseAddress { get; }

    /// <summary>
    /// Host-reachable connection string for the shared PostGIS container (mapped host port), so a test can
    /// seed source tables directly in the same database the server connects to over the Docker network.
    /// </summary>
    public string PostgresConnectionString { get; }

    public static async Task<HonuaServerTestcontainer> StartAsync(
        ConsoleTrustIntegrationOptions options,
        CancellationToken cancellationToken)
    {
        INetwork? network = null;
        PostgreSqlContainer? postgres = null;
        IContainer? server = null;
        IContainer? redis = null;
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

            redis = new ContainerBuilder("redis:7-alpine")
                .WithNetwork(network)
                .WithNetworkAliases("redis")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("redis-cli", "ping"))
                .Build();
            await redis.StartAsync(cancellationToken).ConfigureAwait(false);

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

            builder = builder.WithEnvironment("ConnectionStrings__Redis", "redis:6379");
            server = builder.Build();
            await server.StartAsync(cancellationToken).ConfigureAwait(false);

            var baseAddress = new UriBuilder(
                options.ServerScheme,
                server.Hostname,
                server.GetMappedPublicPort(options.ServerPort)).Uri;
            // The PostgreSqlContainer's own connection string targets the mapped host port, so a test on the
            // host can seed the very database the server reaches over the Docker network (host=postgres).
            var postgresConnectionString = postgres.GetConnectionString();
            return new HonuaServerTestcontainer(network, postgres, server, redis, baseAddress, postgresConnectionString);
        }
        catch
        {
            await DisposeQuietlyAsync(server, redis, postgres, network).ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeQuietlyAsync(_server, _redis, _postgres, _network).ConfigureAwait(false);
        _server = null;
        _redis = null;
        _postgres = null;
        _network = null;
    }

    private static async ValueTask DisposeQuietlyAsync(
        IContainer? server,
        IContainer? redis,
        PostgreSqlContainer? postgres,
        INetwork? network)
    {
        if (server is not null)
        {
            var evidenceDirectory = Environment.GetEnvironmentVariable("HONUA_CONSOLE_SERVER_LOG_DIR");
            if (!string.IsNullOrWhiteSpace(evidenceDirectory))
            {
                try
                {
                    Directory.CreateDirectory(evidenceDirectory);
                    var (stdout, stderr) = await server.GetLogsAsync().ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, $"{server.Id}.log"), stdout + stderr)
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    System.Console.Error.WriteLine($"Could not retain server logs: {exception.Message}");
                }
            }

            await server.DisposeAsync().ConfigureAwait(false);
        }

        if (redis is not null)
        {
            await redis.DisposeAsync().ConfigureAwait(false);
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
