using Honua.Console.Contracts;

namespace Honua.Console.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ContentPublicationIntegrationCollection : ICollectionFixture<ContentPublicationFixture>
{
    public const string Name = "ContentPublicationIntegration";
}

public sealed class ContentPublicationFixture : IAsyncLifetime
{
    private HonuaServerTestcontainer? _container;

    public LiveServerIntegrationOptions Options { get; } = LiveServerIntegrationOptions.Load();

    public string? SkipReason { get; private set; } = LiveServerIntegrationOptions.GetStudioSkipReason();

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
            SkipReason = "The honua-server content-publication integration container could not start "
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

    public IHonuaContentPublicationClient CreateContentPublicationClient() =>
        new HonuaContentPublicationHttpClient(
            CreateRawClient(),
            new HonuaContentPublicationClientOptions(BaseAddress, Options.StudioAdminApiKey));

    public ServerStateVerifier CreateVerifier() =>
        new(BaseAddress, Options.StudioAdminApiKey);

    public string? AdminApiKey => Options.StudioAdminApiKey;

    // Dev-auth bypass authenticates every request, including the anonymous probe, so denial assertions are skipped only for that profile.
    public bool DevAuthBypassEnabled =>
        (Options.ServerEnvironment ?? string.Empty)
            .Split([';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(entry => entry.Split('=', 2))
            .Any(parts =>
                parts.Length == 2
                && parts[0].Trim().Equals("HONUA_DEV_AUTH_ALLOW_BYPASS", StringComparison.OrdinalIgnoreCase)
                && parts[1].Trim() is var value
                && (value.Equals("true", StringComparison.OrdinalIgnoreCase)
                    || value == "1"
                    || value.Equals("yes", StringComparison.OrdinalIgnoreCase)));
}
