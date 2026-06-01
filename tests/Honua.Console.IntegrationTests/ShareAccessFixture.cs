using Honua.Console.Contracts;

namespace Honua.Console.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ShareAccessIntegrationCollection : ICollectionFixture<ShareAccessFixture>
{
    public const string Name = "ShareAccessIntegration";
}

/// <summary>
/// Boots a real honua-server (with PostgreSQL) via Testcontainers so the server-bound Share management
/// surface can be asserted against the live Console Share access API (honua-server#1215). Off by default;
/// skips gracefully when Docker, the server image, the opt-in flag, or the admin API key is unavailable.
/// Reuses <see cref="HonuaServerTestcontainer"/> and mirrors <see cref="StudioFormPackageFixture"/>.
/// </summary>
public sealed class ShareAccessFixture : IAsyncLifetime
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
            SkipReason = "The honua-server Console share integration container could not start "
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

    /// <summary>Builds a raw HttpClient (admin key applied per-request) for seeding content items.</summary>
    public HttpClient CreateRawClient()
    {
        var handler = new HttpClientHandler();
        if (string.Equals(BaseAddress.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        return new HttpClient(handler) { BaseAddress = BaseAddress };
    }

    /// <summary>Builds the production Console Share client against the live server with the admin API key.</summary>
    public IHonuaConsoleShareClient CreateShareClient()
    {
        var httpClient = CreateRawClient();
        return new HonuaConsoleShareHttpClient(
            httpClient,
            new HonuaConsoleShareClientOptions(BaseAddress, Options.StudioAdminApiKey));
    }

    /// <summary>
    /// The independent verification oracle that reads server state back through canonical read APIs and the
    /// anonymous Console Share surface — never the admin Share mutation path the operation went through.
    /// </summary>
    public ServerStateVerifier CreateVerifier() =>
        new(BaseAddress, Options.StudioAdminApiKey);

    public string? AdminApiKey => Options.StudioAdminApiKey;
}
