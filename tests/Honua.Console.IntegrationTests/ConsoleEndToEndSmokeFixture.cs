using Honua.Console.Contracts;

namespace Honua.Console.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConsoleEndToEndSmokeCollection : ICollectionFixture<ConsoleEndToEndSmokeFixture>
{
    public const string Name = "ConsoleEndToEndSmoke";
}

// Boots one real honua-server (with PostgreSQL/PostGIS) via Testcontainers so the Console end-to-end
// smoke drives the catalog, Studio host and operate/publishing chain against a single live server.
// Off by default; skips cleanly when Docker, the configured server image, the opt-in flag
// (HONUA_CONSOLE_INTEGRATION / HONUA_CONSOLE_RUN_LIVE_SERVER_TESTS) or the admin API key is unavailable.
public sealed class ConsoleEndToEndSmokeFixture : IAsyncLifetime
{
    private HonuaServerTestcontainer? _container;

    public LiveServerIntegrationOptions Options { get; } = LiveServerIntegrationOptions.Load();

    public string? SkipReason { get; private set; } = LiveServerIntegrationOptions.GetEndToEndSkipReason();

    public Uri BaseAddress { get; private set; } = new("https://localhost");

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

    public IHonuaConsoleContentClient CreateContentClient() =>
        new HonuaConsoleContentHttpClient(
            CreateHttpClient(),
            new HonuaConsoleContentClientOptions(BaseAddress, Options.StudioAdminApiKey));

    public IHonuaContentPublicationClient CreatePublicationClient() =>
        new HonuaContentPublicationHttpClient(
            CreateHttpClient(),
            new HonuaContentPublicationClientOptions(BaseAddress, Options.StudioAdminApiKey));

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
