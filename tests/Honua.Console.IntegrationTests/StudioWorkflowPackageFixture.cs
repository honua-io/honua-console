using Honua.Console.Contracts;

namespace Honua.Console.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class StudioWorkflowPackageIntegrationCollection : ICollectionFixture<StudioWorkflowPackageFixture>
{
    public const string Name = "StudioWorkflowPackageIntegration";
}

/// <summary>
/// Boots a real honua-server (with PostgreSQL) via Testcontainers so the server-backed Studio workflow
/// package client can be asserted against the live GP/ETL workflow package + node registry + dry-run +
/// publication API (#1185). Off by default; skips gracefully when Docker, the server image, the opt-in
/// flag, or the admin API key is unavailable (AC: Testcontainers coverage with Docker-unavailable skip).
/// Reuses <see cref="HonuaServerTestcontainer"/> and <see cref="ConsoleTrustIntegrationOptions"/> so the
/// container-boot mechanics and admin API-key gating are not duplicated.
/// </summary>
public sealed class StudioWorkflowPackageFixture : IAsyncLifetime
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
            SkipReason = "The honua-server workflow integration container could not start "
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
    /// Builds the production workflow package API client against the live server. Accepts the dev/self-signed
    /// certificate only when the fixture server is TLS, so the test exercises the real client + admin
    /// API-key path against the admin-authorized <c>/api/v1/console/workflow-*</c> endpoints.
    /// </summary>
    public IWorkflowPackageApiClient CreateApiClient()
    {
        var handler = new HttpClientHandler();
        if (string.Equals(BaseAddress.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        var httpClient = new HttpClient(handler) { BaseAddress = BaseAddress };
        return new HttpWorkflowPackageApiClient(
            httpClient,
            new WorkflowPackageClientOptions(BaseAddress, Options.StudioAdminApiKey));
    }
}
