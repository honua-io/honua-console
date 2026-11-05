using Honua.Console.Contracts;

namespace Honua.Console.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TemporalReplicaIntegrationCollection : ICollectionFixture<TemporalReplicaFixture>
{
    public const string Name = "TemporalReplicaIntegration";
}

public sealed class TemporalReplicaFixture : IAsyncLifetime
{
    public const string CatalogWorkspace = "default";
    public const string CatalogTenant = "public";
    public const string CatalogNamespace = "console-catalog-fixture";

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

    public IHonuaAdminOperateClient CreateOperateClient() =>
        new HonuaAdminOperateHttpClient(
            CreateRawClient(),
            new HonuaAdminOperateClientOptions(BaseAddress, Options.StudioAdminApiKey));

    public IHonuaTemporalClient CreateTemporalClient() =>
        new HonuaTemporalHttpClient(
            CreateRawClient(),
            new HonuaTemporalClientOptions(BaseAddress, Options.StudioAdminApiKey));

    public ServerStateVerifier CreateVerifier() =>
        new(BaseAddress, Options.StudioAdminApiKey);

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
