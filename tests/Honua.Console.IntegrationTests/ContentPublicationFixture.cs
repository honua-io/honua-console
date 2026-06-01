using Honua.Console.Contracts;

namespace Honua.Console.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ContentPublicationIntegrationCollection : ICollectionFixture<ContentPublicationFixture>
{
    public const string Name = "ContentPublicationIntegration";
}

/// <summary>
/// Boots a real honua-server (with PostgreSQL) via Testcontainers for the publish→catalog-landing round-trip
/// (console-integration-test-plan.md Wave 2, Family B). Exposes the production content-publication client
/// (the publish OPERATION → <c>POST /api/v1/console/publications</c>) and the independent
/// <see cref="ServerStateVerifier"/> oracle that reads the resulting route/visibility back through the admin
/// publication detail and the ANONYMOUS published-route surface. Off by default; skips gracefully when
/// Docker, the server image, the opt-in flag, or the admin API key is unavailable (Console Patterns Charter
/// section 11). Reuses <see cref="HonuaServerTestcontainer"/> and mirrors <see cref="ShareAccessFixture"/>.
/// </summary>
public sealed class ContentPublicationFixture : IAsyncLifetime
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

    /// <summary>Raw HttpClient (admin key applied per-request, dev-cert acceptance) for seeding content items.</summary>
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

    /// <summary>The production content-publication client (the publish OPERATION path) with the admin API key.</summary>
    public IHonuaContentPublicationClient CreateContentPublicationClient() =>
        new HonuaContentPublicationHttpClient(
            CreateRawClient(),
            new HonuaContentPublicationClientOptions(BaseAddress, Options.StudioAdminApiKey));

    /// <summary>The independent verification oracle that reads server state back through canonical read APIs.</summary>
    public ServerStateVerifier CreateVerifier() =>
        new(BaseAddress, Options.StudioAdminApiKey);

    public string? AdminApiKey => Options.StudioAdminApiKey;

    /// <summary>
    /// True when the server target runs with dev-auth bypass (HONUA_DEV_AUTH_ALLOW_BYPASS), which
    /// auto-authenticates every request — including the verifier's "anonymous" probe — on auth-gated
    /// surfaces like /api/v1/published. Negative anonymous-denial assertions cannot be proven in that
    /// profile and are skipped ONLY when this is set, so a genuine leak still fails in a non-bypass run.
    /// </summary>
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
