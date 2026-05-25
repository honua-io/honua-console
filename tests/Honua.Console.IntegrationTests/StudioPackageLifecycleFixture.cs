using Honua.Console.Contracts;

namespace Honua.Console.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class StudioPackageLifecycleIntegrationCollection : ICollectionFixture<StudioPackageLifecycleFixture>
{
    public const string Name = "StudioPackageLifecycleIntegration";
}

/// <summary>
/// Boots a real honua-server (with PostgreSQL) via Testcontainers so the server-backed Studio authoring
/// shell can be asserted against the live package draft/lifecycle + validation/preview API (#1180/#1181).
/// Off by default; skips gracefully when Docker, the server image, the opt-in flag, or the admin API key
/// is unavailable (AC: Testcontainers coverage with Docker-unavailable skip). Reuses
/// <see cref="HonuaServerTestcontainer"/> so the container-boot mechanics are not duplicated.
/// </summary>
public sealed class StudioPackageLifecycleFixture : IAsyncLifetime
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
            // The lane is opt-in and must skip - never fail - when Docker/Testcontainers cannot start the
            // server. Record the reason so the SkippableFact bodies skip with it, and tear down anything
            // that partially started.
            SkipReason = "The honua-server Studio integration container could not start "
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

    /// <summary>
    /// Builds the production Studio lifecycle client against the live server. Accepts the dev/self-signed
    /// certificate only when the fixture server is TLS, so the test exercises the real client + admin
    /// API-key path.
    /// </summary>
    public IStudioPackageLifecycleClient CreateClient()
    {
        var handler = new HttpClientHandler();
        if (string.Equals(BaseAddress.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        var httpClient = new HttpClient(handler) { BaseAddress = BaseAddress };
        return new HttpStudioPackageLifecycleClient(
            httpClient,
            new StudioPackageLifecycleClientOptions(BaseAddress, Options.StudioAdminApiKey));
    }
}
