using Honua.Console.Contracts;

namespace Honua.Console.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CrossCuttingIntegrationCollection : ICollectionFixture<CrossCuttingFixture>
{
    public const string Name = "CrossCuttingIntegration";
}

public sealed class CrossCuttingFixture : IAsyncLifetime
{
    private HonuaServerTestcontainer? _container;

    public LiveServerIntegrationOptions Options { get; } = LiveServerIntegrationOptions.Load();

    public string? SkipReason { get; private set; } = LiveServerIntegrationOptions.GetStudioSkipReason();

    public Uri BaseAddress { get; private set; } = new("https://localhost");

    public string? PostgresConnectionString { get; private set; }

    public async Task InitializeAsync()
    {
        if (SkipReason is not null)
        {
            return;
        }

        if (Options.ExternalBaseUri is not null)
        {
            // An external server target has no host-reachable PostGIS to seed; the RBAC / validation /
            // idempotency round-trips seed a PostGIS table directly (CrossCuttingSeeder), so flag a clean
            // skip rather than failing with a null Npgsql connection — mirroring ServiceLayerPublishFixture.
            BaseAddress = Options.ExternalBaseUri;
            SkipReason = "The cross-cutting round-trips seed a PostGIS table directly and require the "
                + "Testcontainers PostGIS; set HONUA_CONSOLE_SERVER_IMAGE instead of HONUA_CONSOLE_EXTERNAL_BASE_URL.";
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            _container = await HonuaServerTestcontainer.StartAsync(Options, timeout.Token).ConfigureAwait(false);
            BaseAddress = _container.BaseAddress;
            PostgresConnectionString = _container.PostgresConnectionString;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            SkipReason = "The honua-server cross-cutting integration container could not start "
                + $"({ex.GetType().Name}: {ex.Message}). Ensure Docker is running and the configured server "
                + "image is pullable, or set HONUA_CONSOLE_EXTERNAL_BASE_URL.";
            if (_container is not null)
            {
                await _container.DisposeAsync().ConfigureAwait(false);
                _container = null;
            }
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
            _container = null;
        }
    }

    public IHonuaAdminOperateClient CreateOperateClient() =>
        new HonuaAdminOperateHttpClient(
            CreateHttpClient(),
            new HonuaAdminOperateClientOptions(BaseAddress, Options.StudioAdminApiKey));

    public IHonuaAdminOperateClient CreateUnauthenticatedOperateClient() =>
        new HonuaAdminOperateHttpClient(
            CreateHttpClient(),
            new HonuaAdminOperateClientOptions(BaseAddress, ApiKey: null));

    public IHonuaContentPublicationClient CreateContentPublicationClient() =>
        new HonuaContentPublicationHttpClient(
            CreateHttpClient(),
            new HonuaContentPublicationClientOptions(BaseAddress, Options.StudioAdminApiKey));

    public IHonuaConsoleShareClient CreateShareClient() =>
        new HonuaConsoleShareHttpClient(
            CreateHttpClient(),
            new HonuaConsoleShareClientOptions(BaseAddress, Options.StudioAdminApiKey));

    public ServerStateVerifier CreateVerifier() =>
        new(BaseAddress, Options.StudioAdminApiKey);

    public string? AdminApiKey => Options.StudioAdminApiKey;

    public HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler();
        if (string.Equals(BaseAddress.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        return new HttpClient(handler) { BaseAddress = BaseAddress };
    }
}
