using Honua.Console.Contracts;

namespace Honua.Console.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class StudioWorkflowPackageIntegrationCollection : ICollectionFixture<StudioWorkflowPackageFixture>
{
    public const string Name = "StudioWorkflowPackageIntegration";
}

public sealed class StudioWorkflowPackageFixture : IAsyncLifetime
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

    public IWorkflowPackageApiClient CreateApiClient()
    {
        var handler = new HttpClientHandler();
        if (string.Equals(BaseAddress.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        var httpClient = new HttpClient(new WorkflowRegistryEvidenceHandler(handler)) { BaseAddress = BaseAddress };
        return new HttpWorkflowPackageApiClient(
            httpClient,
            new WorkflowPackageClientOptions(BaseAddress, Options.StudioAdminApiKey));
    }
}
