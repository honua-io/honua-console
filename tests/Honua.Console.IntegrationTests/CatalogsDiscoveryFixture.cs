using Honua.Console.Contracts;

namespace Honua.Console.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CatalogsDiscoveryIntegrationCollection : ICollectionFixture<CatalogsDiscoveryFixture>
{
    public const string Name = "CatalogsDiscoveryIntegration";
}

/// <summary>
/// Boots a real honua-server (with PostgreSQL) via Testcontainers so the server-bound Operate &gt; Catalogs
/// discovery-endpoints surface can be asserted against the live catalog discovery-endpoints registry read API
/// (honua-server#1279). Off by default; skips gracefully when Docker, the server image, the opt-in flag, or
/// the admin API key is unavailable. Reuses <see cref="HonuaServerTestcontainer"/> and mirrors
/// <see cref="ShareAccessFixture"/>.
/// </summary>
public sealed class CatalogsDiscoveryFixture : IAsyncLifetime
{
    private HonuaServerTestcontainer? _container;

    public ConsoleTrustIntegrationOptions Options { get; } = ConsoleTrustIntegrationOptions.Load();

    public string? SkipReason { get; private set; } = ConsoleTrustIntegrationOptions.GetStudioSkipReason();

    public Uri BaseAddress { get; private set; } = new("https://localhost");

    public async Task InitializeAsync()
    {
        if (SkipReason is not null)
        {
            return;
        }

        if (Options.ExternalBaseUri is not null)
        {
            BaseAddress = Options.ExternalBaseUri;
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            _container = await HonuaServerTestcontainer.StartAsync(Options, timeout.Token).ConfigureAwait(false);
            BaseAddress = _container.BaseAddress;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            SkipReason = "The honua-server Console catalog discovery integration container could not start "
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

    /// <summary>Builds the production catalog discovery client against the live server with the admin API key.</summary>
    public IHonuaCatalogDiscoveryClient CreateCatalogDiscoveryClient()
    {
        var handler = new HttpClientHandler();
        if (string.Equals(BaseAddress.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        var httpClient = new HttpClient(handler) { BaseAddress = BaseAddress };
        return new HonuaCatalogDiscoveryHttpClient(
            httpClient,
            new HonuaCatalogDiscoveryClientOptions(BaseAddress, Options.StudioAdminApiKey));
    }
}
