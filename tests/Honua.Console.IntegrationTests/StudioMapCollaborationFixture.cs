using Honua.Console.Contracts;

namespace Honua.Console.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class StudioMapCollaborationIntegrationCollection : ICollectionFixture<StudioMapCollaborationFixture>
{
    public const string Name = "StudioMapCollaborationIntegration";
}

/// <summary>
/// Boots a real honua-server (with PostgreSQL) via Testcontainers so the server-bound Studio map
/// collaboration surface can be asserted against the live durable collaboration API (honua-server#1278,
/// slice 1). Off by default; skips gracefully when Docker, the server image, the opt-in flag, or the admin
/// API key is unavailable. Reuses <see cref="HonuaServerTestcontainer"/> and mirrors
/// <see cref="ShareAccessFixture"/>.
/// </summary>
public sealed class StudioMapCollaborationFixture : IAsyncLifetime
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
            SkipReason = "The honua-server Console collaboration integration container could not start "
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

    /// <summary>Builds a raw HttpClient (admin key applied per-request) for seeding collaboration threads.</summary>
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

    /// <summary>Builds the production collaboration client against the live server with the admin API key.</summary>
    public IHonuaStudioMapCollaborationClient CreateCollaborationClient()
    {
        var httpClient = CreateRawClient();
        return new HonuaStudioMapCollaborationHttpClient(
            httpClient,
            new HonuaStudioMapCollaborationClientOptions(BaseAddress, Options.StudioAdminApiKey));
    }

    /// <summary>The independent verification oracle that reads collab state back through the server collab API.</summary>
    public ServerStateVerifier CreateVerifier() =>
        new(BaseAddress, Options.StudioAdminApiKey);

    public string? AdminApiKey => Options.StudioAdminApiKey;
}
