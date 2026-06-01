using Honua.Console.Contracts;

namespace Honua.Console.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ServiceLayerPublishIntegrationCollection : ICollectionFixture<ServiceLayerPublishFixture>
{
    public const string Name = "ServiceLayerPublishIntegration";
}

/// <summary>
/// Boots a real honua-server (with PostGIS) via Testcontainers for the flagship service-layer-publish
/// round-trip (console-integration-test-plan.md §3.A, Wave 1). Exposes the host-reachable PostGIS
/// connection string so the test can seed source tables directly in the same database the server reaches
/// over the Docker network, plus factories for the production admin operate client (the publish OPERATION)
/// and the independent <see cref="ServerStateVerifier"/> oracle. Off by default; skips gracefully when
/// Docker, the server image, the opt-in flag, or the admin API key is unavailable (Console Patterns
/// Charter section 11). Reuses <see cref="HonuaServerTestcontainer"/>.
/// </summary>
public sealed class ServiceLayerPublishFixture : IAsyncLifetime
{
    private HonuaServerTestcontainer? _container;

    public ConsoleTrustIntegrationOptions Options { get; } = ConsoleTrustIntegrationOptions.Load();

    public string? SkipReason { get; private set; } = ConsoleTrustIntegrationOptions.GetStudioSkipReason();

    public Uri BaseAddress { get; private set; } = new("https://localhost");

    /// <summary>Host-reachable PostGIS connection string for seeding source tables, or <c>null</c> for an external server.</summary>
    public string? PostgresConnectionString { get; private set; }

    public async Task InitializeAsync()
    {
        if (SkipReason is not null)
        {
            return;
        }

        if (Options.ExternalBaseUri is not null)
        {
            // An external server target has no host-reachable PostGIS to seed; the round-trip needs the
            // Testcontainers PostGIS, so flag a clean skip rather than a false-green.
            BaseAddress = Options.ExternalBaseUri;
            SkipReason = "The service-layer-publish round-trip seeds a PostGIS table directly and requires the "
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
            SkipReason = "The honua-server service-layer-publish integration container could not start "
                + $"({ex.GetType().Name}: {ex.Message}). Ensure Docker is running and the configured server "
                + "image is pullable.";
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

    /// <summary>The production admin operate client (the publish OPERATION path), with admin key + dev-cert acceptance.</summary>
    public IHonuaAdminOperateClient CreateOperateClient() =>
        new HonuaAdminOperateHttpClient(
            CreateHttpClient(),
            new HonuaAdminOperateClientOptions(BaseAddress, Options.StudioAdminApiKey));

    /// <summary>The independent verification oracle that reads server state back through canonical read APIs.</summary>
    public ServerStateVerifier CreateVerifier() =>
        new(BaseAddress, Options.StudioAdminApiKey);

    /// <summary>Raw HttpClient (admin key + dev-cert acceptance) used to seed the secure connection over the admin API.</summary>
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
