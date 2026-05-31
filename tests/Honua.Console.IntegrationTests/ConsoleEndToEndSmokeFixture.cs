using Honua.Console.Contracts;

namespace Honua.Console.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConsoleEndToEndSmokeCollection : ICollectionFixture<ConsoleEndToEndSmokeFixture>
{
    public const string Name = "ConsoleEndToEndSmoke";
}

/// <summary>
/// Boots a single real honua-server (with PostgreSQL/PostGIS) via Testcontainers so the Console
/// end-to-end smoke can drive the full cross-surface chain — catalog list, a Studio package render, and an
/// operate/publishing surface — against ONE live server boot and assert live responses
/// (issue #59; Console Patterns Charter section 11 real-server policy; supersedes the in-memory-only
/// <c>smoke/parity</c> harness for the <c>honua-console#9</c> gate).
///
/// This consolidates the individual per-surface fixtures (<see cref="ConsoleContentFixture"/>,
/// <see cref="StudioPackageLifecycleFixture"/>, <see cref="PublishingWorkspaceFixture"/>) into one server
/// so the smoke proves the chain end to end rather than each surface in isolation. Off by default; skips
/// cleanly when Docker, the configured server image, the opt-in flag
/// (<c>HONUA_CONSOLE_INTEGRATION</c> / <c>HONUA_CONSOLE_RUN_LIVE_SERVER_TESTS</c>), or the admin API key is
/// unavailable, exactly like the existing live-server lanes — so the standard CI runner stays green
/// without Docker. Reuses <see cref="HonuaServerTestcontainer"/> so the container-boot mechanics live in
/// one place.
/// </summary>
public sealed class ConsoleEndToEndSmokeFixture : IAsyncLifetime
{
    private HonuaServerTestcontainer? _container;

    public ConsoleTrustIntegrationOptions Options { get; } = ConsoleTrustIntegrationOptions.Load();

    public string? SkipReason { get; private set; } = ConsoleTrustIntegrationOptions.GetEndToEndSkipReason();

    public Uri BaseAddress { get; private set; } = new("https://localhost");

    /// <summary>The resolved honua-server image/origin recorded in the smoke evidence (sourceHydrated provenance).</summary>
    public string ServerImageOrOrigin =>
        Options.ExternalBaseUri?.ToString() ?? Options.ServerImage ?? "(unknown)";

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
            // The lane is opt-in and must skip — never fail — when Docker/Testcontainers cannot boot the
            // server. Record the reason so the SkippableFact body skips with it, and tear down anything that
            // partially started.
            SkipReason = "The Console end-to-end smoke container could not start "
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

    /// <summary>Production Console content/catalog client bound to the live server (catalog flow).</summary>
    public IHonuaConsoleContentClient CreateContentClient() =>
        new HonuaConsoleContentHttpClient(
            CreateHttpClient(),
            new HonuaConsoleContentClientOptions(BaseAddress, Options.StudioAdminApiKey));

    /// <summary>Production Studio package-lifecycle client bound to the live server (Studio package render flow).</summary>
    public IStudioPackageLifecycleClient CreateStudioClient() =>
        new HttpStudioPackageLifecycleClient(
            CreateHttpClient(),
            new StudioPackageLifecycleClientOptions(BaseAddress, Options.StudioAdminApiKey));

    /// <summary>Production content publication client bound to the live server (operate/publishing flow).</summary>
    public IHonuaContentPublicationClient CreatePublicationClient() =>
        new HonuaContentPublicationHttpClient(
            CreateHttpClient(),
            new HonuaContentPublicationClientOptions(BaseAddress, Options.StudioAdminApiKey));

    /// <summary>
    /// Raw HttpClient (with admin key + dev-cert acceptance only when the fixture server is TLS) used to
    /// seed publications via the real publish POST. Never an in-memory client.
    /// </summary>
    public HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler();
        if (string.Equals(BaseAddress.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        var client = new HttpClient(handler) { BaseAddress = BaseAddress };
        if (!string.IsNullOrWhiteSpace(Options.StudioAdminApiKey))
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-API-Key", Options.StudioAdminApiKey);
        }

        return client;
    }
}
