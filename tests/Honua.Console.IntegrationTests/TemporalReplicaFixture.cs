using Honua.Console.Contracts;

namespace Honua.Console.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TemporalReplicaIntegrationCollection : ICollectionFixture<TemporalReplicaFixture>
{
    public const string Name = "TemporalReplicaIntegration";
}

/// <summary>
/// Boots a real honua-server (with PostGIS) via Testcontainers for the Wave 3 temporal + replica round-trips
/// (console-integration-test-plan.md Wave 3, families E/F). Exposes the host-reachable PostGIS connection
/// string so a test can seed a source table directly in the same database the server reaches over the Docker
/// network, plus factories for the admin operate client (the publish OPERATION that creates a service/layer),
/// the production temporal capability client (the console temporal OPERATION path), a raw admin HttpClient
/// (for seeding a named replica through the GeoServices <c>createReplica</c> verb), and the independent
/// <see cref="ServerStateVerifier"/> oracle. Off by default; skips gracefully when Docker, the server image,
/// the opt-in flag, or the admin API key is unavailable (Console Patterns Charter section 11). Reuses
/// <see cref="HonuaServerTestcontainer"/> and mirrors <see cref="ServiceLayerPublishFixture"/>.
/// </summary>
public sealed class TemporalReplicaFixture : IAsyncLifetime
{
    public const string CatalogWorkspace = "default";
    public const string CatalogTenant = "public";
    public const string CatalogNamespace = "console-catalog-fixture";

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
            BaseAddress = Options.ExternalBaseUri;
            SkipReason = "The temporal/replica round-trips seed a PostGIS table directly and require the "
                + "Testcontainers PostGIS; set HONUA_CONSOLE_SERVER_IMAGE instead of HONUA_CONSOLE_EXTERNAL_BASE_URL.";
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            // Explicit mapping for this ephemeral server; real publications must carry the namespace.
            var scopedOptions = Options with
            {
                ServerEnvironment = string.Join("\n", Options.ServerEnvironment,
                    $"Console__CatalogDiscovery__Workspaces__0__Id={CatalogWorkspace}",
                    "MultiTenancy__Enabled=true",
                    $"MultiTenancy__DefaultTenantId={CatalogTenant}",
                    $"Console__CatalogDiscovery__Workspaces__0__TenantId={CatalogTenant}",
                    $"Console__CatalogDiscovery__Workspaces__0__Namespace={CatalogNamespace}",
                    "Console__CatalogDiscovery__Workspaces__1__Id=other-tenant-workspace",
                    "Console__CatalogDiscovery__Workspaces__1__TenantId=other-tenant",
                    $"Console__CatalogDiscovery__Workspaces__1__Namespace={CatalogNamespace}")
            };
            _container = await HonuaServerTestcontainer.StartAsync(scopedOptions, timeout.Token).ConfigureAwait(false);
            BaseAddress = _container.BaseAddress;
            PostgresConnectionString = _container.PostgresConnectionString;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            SkipReason = "The honua-server temporal/replica integration container could not start "
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

    /// <summary>The production admin operate client (the service-layer publish OPERATION path).</summary>
    public IHonuaAdminOperateClient CreateOperateClient() =>
        new HonuaAdminOperateHttpClient(
            CreateRawClient(),
            new HonuaAdminOperateClientOptions(BaseAddress, Options.StudioAdminApiKey));

    /// <summary>The production temporal client (the console temporal capability + as-of OPERATION path).</summary>
    public IHonuaTemporalClient CreateTemporalClient() =>
        new HonuaTemporalHttpClient(
            CreateRawClient(),
            new HonuaTemporalClientOptions(BaseAddress, Options.StudioAdminApiKey));

    /// <summary>The independent verification oracle that reads server state back through canonical read APIs.</summary>
    public ServerStateVerifier CreateVerifier() =>
        new(BaseAddress, Options.StudioAdminApiKey);

    /// <summary>Raw HttpClient (admin key applied per-request, dev-cert acceptance) for seeding connections + replicas.</summary>
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

    public string? AdminApiKey => Options.StudioAdminApiKey;
}
