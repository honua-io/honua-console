using System.Net.Http.Json;
using System.Text.Json;
using Honua.Console.Contracts;
using Npgsql;

namespace Honua.Console.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class StudioFormPackageIntegrationCollection : ICollectionFixture<StudioFormPackageFixture>
{
    public const string Name = "StudioFormPackageIntegration";
}

/// <summary>
/// Boots a real honua-server (with PostgreSQL) via Testcontainers so the server-backed form builder can be
/// asserted against the live form package lifecycle (honua-server#1184). Off by default; skips gracefully
/// when Docker, the server image, the opt-in flag, or the admin API key is unavailable (AC: Testcontainers
/// coverage with Docker-unavailable skip). Reuses <see cref="HonuaServerTestcontainer"/> so the container
/// boot mechanics are not duplicated.
/// </summary>
public sealed class StudioFormPackageFixture : IAsyncLifetime
{
    private HonuaServerTestcontainer? _container;

    public ConsoleTrustIntegrationOptions Options { get; } = ConsoleTrustIntegrationOptions.Load();

    public string? SkipReason { get; private set; } = ConsoleTrustIntegrationOptions.GetStudioSkipReason();

    public Uri BaseAddress { get; private set; } = new("https://localhost");

    public string TargetServiceId { get; private set; } = "console-form-fixture";

    public int TargetLayerId { get; private set; }

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
            SkipReason = "The honua-server form package integration container could not start "
                + $"({ex.GetType().Name}: {ex.Message}). Ensure Docker is running and the configured server "
                + "image is pullable, or set HONUA_CONSOLE_EXTERNAL_BASE_URL.";
            if (_container is not null)
            {
                await _container.DisposeAsync().ConfigureAwait(false);
                _container = null;
            }
        }

        if (SkipReason is null)
        {
            await SeedTargetLayerAsync();
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

    private async Task SeedTargetLayerAsync()
    {
        if (_container is null)
        {
            throw new InvalidOperationException("Form publishing proof requires the seeded local PostGIS fixture.");
        }

        await using var database = new NpgsqlConnection(_container.PostgresConnectionString);
        await database.OpenAsync();
        await using var command = database.CreateCommand();
        command.CommandText = """
            CREATE TABLE public.console_form_assets (
                id integer PRIMARY KEY,
                asset_id text NOT NULL,
                condition text NOT NULL,
                geom geometry(Point, 4326) NOT NULL);
            INSERT INTO public.console_form_assets VALUES
                (1, 'asset-1', 'good', ST_SetSRID(ST_Point(-157.8, 21.3), 4326));
            """;
        await command.ExecuteNonQueryAsync();

        using var http = new HttpClient { BaseAddress = BaseAddress };
        http.DefaultRequestHeaders.Add("X-API-Key", Options.StudioAdminApiKey);
        using var connectionResponse = await http.PostAsJsonAsync("/api/v1/admin/connections/", new
        {
            name = "console-form-source",
            host = "postgres",
            port = 5432,
            databaseName = "honua",
            username = "honua",
            password = "honua",
            provider = "postgis",
            sslRequired = false,
            sslMode = "Disable"
        });
        connectionResponse.EnsureSuccessStatusCode();
        using var connection = JsonDocument.Parse(await connectionResponse.Content.ReadAsStringAsync());
        var connectionId = connection.RootElement.GetProperty("data").GetProperty("connectionId").GetString();

        TargetServiceId = "console-form-fixture";
        using var publishResponse = await http.PostAsJsonAsync($"/api/v1/admin/connections/{connectionId}/layers/", new
        {
            schema = "public",
            table = "console_form_assets",
            layerName = "Form assets",
            serviceName = TargetServiceId,
            geometryColumn = "geom",
            geometryType = "Point",
            srid = 4326,
            primaryKey = "id",
            fields = new[] { "id", "asset_id", "condition" },
            enabled = true,
            createEditableCopy = true,
            capabilities = new[] { "Query", "Create", "Update" }
        });
        publishResponse.EnsureSuccessStatusCode();
        using var published = JsonDocument.Parse(await publishResponse.Content.ReadAsStringAsync());
        TargetLayerId = published.RootElement.GetProperty("data").GetProperty("layerId").GetInt32();
    }

    /// <summary>
    /// Builds the production form package client against the live server, accepting the dev/self-signed
    /// certificate only when the fixture server is TLS so the test exercises the real client + admin
    /// API-key path (never an in-memory client).
    /// </summary>
    public IHonuaFormPackageClient CreateFormClient()
    {
        var handler = new HttpClientHandler();
        if (string.Equals(BaseAddress.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        var httpClient = new HttpClient(handler) { BaseAddress = BaseAddress };
        return new HonuaFormPackageHttpClient(
            httpClient,
            new HonuaFormPackageClientOptions(BaseAddress, Options.StudioAdminApiKey));
    }

    /// <summary>The independent verification oracle that reads server state back through canonical read APIs.</summary>
    public ServerStateVerifier CreateVerifier() =>
        new(BaseAddress, Options.StudioAdminApiKey);
}
