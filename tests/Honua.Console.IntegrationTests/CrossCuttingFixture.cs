using Honua.Console.Contracts;

namespace Honua.Console.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CrossCuttingIntegrationCollection : ICollectionFixture<CrossCuttingFixture>
{
    public const string Name = "CrossCuttingIntegration";
}

/// <summary>
/// Boots a real honua-server (with PostGIS) via Testcontainers for the Wave 6 cross-cutting suite
/// (console-integration-test-plan.md §5): RBAC enforcement, validation field-errors, idempotency/round-trip
/// fidelity, and version/contract drift. Exposes both an ADMIN-authenticated and a genuinely UNAUTHENTICATED
/// operate client (the RBAC matrix attempts mutations without admin auth), the analysis content client (a
/// validation surface), the independent <see cref="ServerStateVerifier"/> oracle, and a host-reachable
/// PostGIS connection string for seeding source tables. Off by default; skips gracefully when Docker, the
/// server image, the opt-in flag, or the admin API key is unavailable (Console Patterns Charter section 11).
/// Reuses <see cref="HonuaServerTestcontainer"/> and mirrors <see cref="ServiceLayerPublishFixture"/>.
/// </summary>
public sealed class CrossCuttingFixture : IAsyncLifetime
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

    /// <summary>The production admin operate client WITH the admin API key (the privileged operation path).</summary>
    public IHonuaAdminOperateClient CreateOperateClient() =>
        new HonuaAdminOperateHttpClient(
            CreateHttpClient(),
            new HonuaAdminOperateClientOptions(BaseAddress, Options.StudioAdminApiKey));

    /// <summary>
    /// The production admin operate client with NO admin API key — a genuinely unauthenticated principal. The
    /// RBAC matrix uses this to prove a mutation is blocked server-side (not just behind a UI gate).
    /// </summary>
    public IHonuaAdminOperateClient CreateUnauthenticatedOperateClient() =>
        new HonuaAdminOperateHttpClient(
            CreateHttpClient(),
            new HonuaAdminOperateClientOptions(BaseAddress, ApiKey: null));

    /// <summary>The production content-publication client WITH the admin API key (the privileged publish path).</summary>
    public IHonuaContentPublicationClient CreateContentPublicationClient() =>
        new HonuaContentPublicationHttpClient(
            CreateHttpClient(),
            new HonuaContentPublicationClientOptions(BaseAddress, Options.StudioAdminApiKey));

    /// <summary>The production analysis content client WITH the admin API key (a validation surface).</summary>
    public IHonuaAnalysisContentClient CreateAnalysisClient() =>
        new HonuaAnalysisContentHttpClient(
            CreateHttpClient(),
            new HonuaAnalysisContentClientOptions(BaseAddress, Options.StudioAdminApiKey));

    /// <summary>The production Console Share client WITH the admin API key (a validation/Studio-package surface).</summary>
    public IHonuaConsoleShareClient CreateShareClient() =>
        new HonuaConsoleShareHttpClient(
            CreateHttpClient(),
            new HonuaConsoleShareClientOptions(BaseAddress, Options.StudioAdminApiKey));

    /// <summary>The independent verification oracle that reads server state back through canonical read APIs.</summary>
    public ServerStateVerifier CreateVerifier() =>
        new(BaseAddress, Options.StudioAdminApiKey);

    public string? AdminApiKey => Options.StudioAdminApiKey;

    /// <summary>Raw HttpClient (admin key + dev-cert acceptance) used to seed connections/content over the admin API.</summary>
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
