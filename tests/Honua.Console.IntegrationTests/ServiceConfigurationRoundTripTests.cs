using System.Net.Http.Json;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Npgsql;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Wave 5 service-CONFIGURATION round-trips (console-integration-test-plan.md §6 Wave 5 + §3 Family A).
///
/// Each test seeds a PostGIS table + admin connection, publishes a queryable service layer (the Wave-1
/// publish OPERATION), then drives a Wave-5 CONFIGURATION operation through the console's real
/// <see cref="IServiceConfigurationOperation"/> — layer enable/disable, service protocol change, or service
/// access-policy change — and asserts the resulting server state INDEPENDENTLY through the
/// <see cref="ServerStateVerifier"/> oracle (admin layer registry, admin service-settings projection, and
/// the GeoServices FeatureServer protocol surface). The console operation lands a real mutation; the verify
/// reads through a DIFFERENT API than the one the operation went through (plan rule #2). Negative +
/// idempotency companions round out the family.
///
/// Off by default; the SkippableFacts skip cleanly without Docker / the opt-in env (Console Patterns
/// Charter section 11) and RUN in the nightly lane (.github/workflows/console-nightly.yml).
/// </summary>
[Collection(ServiceConfigurationIntegrationCollection.Name)]
public sealed class ServiceConfigurationRoundTripTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ServiceConfigurationFixture _fixture;

    public ServiceConfigurationRoundTripTests(ServiceConfigurationFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task SetLayerEnabled_DisableThenEnable_ReflectsInRegistryAndQueryability()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var setup = await SeedAndPublishAsync("layertoggle");
        Skip.If(setup is null, "The pinned honua-server image could not service the layer-publish precondition.");
        var (connectionId, serviceName, layerId) = setup!.Value;

        var operation = new HonuaServerServiceConfigurationOperation(_fixture.CreateOperateClient());
        using var verifier = _fixture.CreateVerifier();

        // Precondition: published layer is enabled + queryable.
        var initial = await verifier.GetRegisteredLayerAsync(connectionId, serviceName, layerId);
        Assert.NotNull(initial);
        Assert.True(initial!.Enabled ?? false);

        // --- OPERATION: disable the layer through the console's real configuration operation. ---
        var disable = await operation.SetLayerEnabledAsync(new ServiceLayerEnableCommand
        {
            ConnectionId = connectionId,
            LayerId = layerId,
            ServiceName = serviceName,
            Enabled = false
        });
        Assert.True(disable.Succeeded, $"Disable failed: {disable.State} — {disable.Detail}");
        Assert.Equal("Disabled", disable.State);
        Assert.False(disable.Enabled ?? true);

        // Independent verification: the admin layer registry reflects enabled=false.
        var afterDisable = await verifier.GetRegisteredLayerAsync(connectionId, serviceName, layerId);
        Assert.NotNull(afterDisable);
        Assert.False(afterDisable!.Enabled ?? true);

        // --- OPERATION: re-enable the layer. ---
        var enable = await operation.SetLayerEnabledAsync(new ServiceLayerEnableCommand
        {
            ConnectionId = connectionId,
            LayerId = layerId,
            ServiceName = serviceName,
            Enabled = true
        });
        Assert.True(enable.Succeeded, $"Enable failed: {enable.State} — {enable.Detail}");
        Assert.Equal("Enabled", enable.State);
        Assert.True(enable.Enabled ?? false);

        // Independent verification: re-enabled and queryable again with the right data.
        var afterEnable = await verifier.GetRegisteredLayerAsync(connectionId, serviceName, layerId);
        Assert.NotNull(afterEnable);
        Assert.True(afterEnable!.Enabled ?? false);

        var rows = await verifier.QueryFeatureServerAsync(serviceName, layerId, "1=1");
        Assert.NotNull(rows);
        Assert.Equal(3, rows!.Count);
    }

    [SkippableFact]
    public async Task UpdateProtocols_RestrictToFeatureServer_ReflectsInSettingsAndProtocolSurface()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var setup = await SeedAndPublishAsync("protocols");
        Skip.If(setup is null, "The pinned honua-server image could not service the layer-publish precondition.");
        var (_, serviceName, layerId) = setup!.Value;

        var operation = new HonuaServerServiceConfigurationOperation(_fixture.CreateOperateClient());
        using var verifier = _fixture.CreateVerifier();

        // A freshly published service exposes all protocols; confirm FeatureServer is among them.
        var before = await verifier.GetServiceSettingsAsync(serviceName);
        Skip.If(before is null, "The pinned honua-server image does not expose the service-settings projection.");
        Assert.Contains("FeatureServer", before!.EnabledProtocols, StringComparer.OrdinalIgnoreCase);

        // --- OPERATION: restrict the service to FeatureServer + MapServer only. ---
        var update = await operation.UpdateProtocolsAsync(new ServiceProtocolsCommand
        {
            ServiceName = serviceName,
            EnabledProtocols = ["FeatureServer", "MapServer"]
        });
        Assert.True(update.Succeeded, $"Protocol update failed: {update.State} — {update.Detail}");
        Assert.Equal("Updated", update.State);

        // Independent verification via the settings projection: exactly the configured protocols, no more.
        var after = await verifier.GetServiceSettingsAsync(serviceName);
        Assert.NotNull(after);
        Assert.Contains("FeatureServer", after!.EnabledProtocols, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("MapServer", after.EnabledProtocols, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("OData", after.EnabledProtocols, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Stac", after.EnabledProtocols, StringComparer.OrdinalIgnoreCase);

        // Independent verification via the actual GeoServices protocol surface: FeatureServer still resolves
        // (and remains queryable), proving the protocol config is real and not just a settings projection.
        Assert.True(await verifier.GeoServicesProtocolResolvesAsync(serviceName, "FeatureServer"));
        var rows = await verifier.QueryFeatureServerAsync(serviceName, layerId, "1=1");
        Assert.NotNull(rows);
        Assert.Equal(3, rows!.Count);
    }

    [SkippableFact]
    public async Task UpdateProtocols_WithInvalidProtocol_IsRejectedAndNothingChanges()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var setup = await SeedAndPublishAsync("protoneg");
        Skip.If(setup is null, "The pinned honua-server image could not service the layer-publish precondition.");
        var (_, serviceName, _) = setup!.Value;

        var operation = new HonuaServerServiceConfigurationOperation(_fixture.CreateOperateClient());
        using var verifier = _fixture.CreateVerifier();

        var before = await verifier.GetServiceSettingsAsync(serviceName);
        Skip.If(before is null, "The pinned honua-server image does not expose the service-settings projection.");
        var originalProtocols = before!.EnabledProtocols.OrderBy(p => p, StringComparer.Ordinal).ToArray();

        // Invalid config: a bogus protocol name is rejected deterministically by the server
        // (HandleUpdateProtocols validates against ServiceProtocols.All), and nothing changes.
        var update = await operation.UpdateProtocolsAsync(new ServiceProtocolsCommand
        {
            ServiceName = serviceName,
            EnabledProtocols = ["FeatureServer", "NotARealProtocol"]
        });
        Assert.False(update.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(update.Detail));

        // Independently confirm the enabled protocols are unchanged.
        var after = await verifier.GetServiceSettingsAsync(serviceName);
        Assert.NotNull(after);
        Assert.Equal(
            originalProtocols,
            after!.EnabledProtocols.OrderBy(p => p, StringComparer.Ordinal).ToArray());
    }

    [SkippableFact]
    public async Task UpdateProtocols_ReapplyIdenticalConfig_IsIdempotent()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var setup = await SeedAndPublishAsync("protoidem");
        Skip.If(setup is null, "The pinned honua-server image could not service the layer-publish precondition.");
        var (_, serviceName, _) = setup!.Value;

        var operation = new HonuaServerServiceConfigurationOperation(_fixture.CreateOperateClient());
        using var verifier = _fixture.CreateVerifier();

        Skip.If(
            await verifier.GetServiceSettingsAsync(serviceName) is null,
            "The pinned honua-server image does not expose the service-settings projection.");

        var command = new ServiceProtocolsCommand
        {
            ServiceName = serviceName,
            EnabledProtocols = ["FeatureServer"]
        };

        var first = await operation.UpdateProtocolsAsync(command);
        Assert.True(first.Succeeded, $"First protocol update failed: {first.State} — {first.Detail}");
        var firstState = (await verifier.GetServiceSettingsAsync(serviceName))!
            .EnabledProtocols.OrderBy(p => p, StringComparer.Ordinal).ToArray();

        // Re-applying the identical config is a no-op: same enabled protocols, no duplicate entries.
        var second = await operation.UpdateProtocolsAsync(command);
        Assert.True(second.Succeeded, $"Second protocol update failed: {second.State} — {second.Detail}");
        var secondState = (await verifier.GetServiceSettingsAsync(serviceName))!
            .EnabledProtocols.OrderBy(p => p, StringComparer.Ordinal).ToArray();

        Assert.Equal(firstState, secondState);
        Assert.Equal(firstState.Length, firstState.Distinct(StringComparer.Ordinal).Count());
    }

    [SkippableFact]
    public async Task UpdateAccessPolicy_ChangeAnonymousAccess_ReflectsInSettings()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var setup = await SeedAndPublishAsync("accesspolicy");
        Skip.If(setup is null, "The pinned honua-server image could not service the layer-publish precondition.");
        var (_, serviceName, _) = setup!.Value;

        var operation = new HonuaServerServiceConfigurationOperation(_fixture.CreateOperateClient());
        using var verifier = _fixture.CreateVerifier();

        var before = await verifier.GetServiceSettingsAsync(serviceName);
        Skip.If(
            before?.AccessPolicy is null,
            "The pinned honua-server image does not expose the service access-policy projection.");

        // --- OPERATION: flip anonymous access to the opposite of its current value, restrict write. ---
        var target = !(before!.AccessPolicy!.AllowAnonymous ?? false);
        var update = await operation.UpdateAccessPolicyAsync(new ServiceAccessPolicyCommand
        {
            ServiceName = serviceName,
            AllowAnonymous = target,
            AllowAnonymousWrite = false,
            AllowedRoles = ["viewer", "editor"]
        });
        Assert.True(update.Succeeded, $"Access-policy update failed: {update.State} — {update.Detail}");

        // Independent verification: the settings projection reflects the configured policy.
        var after = await verifier.GetServiceSettingsAsync(serviceName);
        Assert.NotNull(after);
        Assert.NotNull(after!.AccessPolicy);
        Assert.Equal(target, after.AccessPolicy!.AllowAnonymous);
        Assert.False(after.AccessPolicy.AllowAnonymousWrite ?? true);
        Assert.Contains("viewer", after.AccessPolicy.AllowedRoles, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("editor", after.AccessPolicy.AllowedRoles, StringComparer.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------------------------------------
    //  Shared seed + publish precondition (mirrors the Wave-1 flagship setup).
    // ---------------------------------------------------------------------------------------------

    // Seeds a parcels table + admin connection and publishes a queryable layer. Returns null when the
    // pinned server image cannot service the publish path (contract drift), so callers skip cleanly.
    private async Task<(string ConnectionId, string ServiceName, int LayerId)?> SeedAndPublishAsync(string tag)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var table = $"parcels_{tag}_{suffix}";
        var serviceName = $"parcels_{tag}_svc_{suffix}";
        await SeedParcelsTableAsync(table);
        var connectionId = await CreateConnectionAsync($"parcels-{tag}-conn-{suffix}");

        var publish = new HonuaServerServiceLayerPublishOperation(_fixture.CreateOperateClient());
        var result = await publish.PublishAsync(new ServiceLayerPublishCommand
        {
            ConnectionId = connectionId,
            Schema = "public",
            Table = table,
            LayerName = "Parcels",
            ServiceName = serviceName,
            GeometryColumn = "geom",
            GeometryType = "Polygon",
            Srid = 3857,
            PrimaryKey = "id",
            Fields = ["id", "name", "area_m2"],
            Enabled = true
        });

        if (!result.Succeeded || result.LayerId is null)
        {
            // Contract-drift: the pinned image cannot publish. Caller skips rather than false-fails.
            return null;
        }

        return (connectionId, serviceName, result.LayerId.Value);
    }

    private async Task SeedParcelsTableAsync(string table)
    {
        await using var connection = new NpgsqlConnection(_fixture.PostgresConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            CREATE EXTENSION IF NOT EXISTS postgis;
            CREATE TABLE public.{table} (
                id integer PRIMARY KEY,
                name text NOT NULL,
                area_m2 double precision NOT NULL,
                geom geometry(Polygon, 3857) NOT NULL
            );
            INSERT INTO public.{table} (id, name, area_m2, geom) VALUES
                (1, 'Alpha', 100.0, ST_SetSRID(ST_MakeEnvelope(100, 200, 110, 210, 3857), 3857)),
                (2, 'Bravo', 200.0, ST_SetSRID(ST_MakeEnvelope(110, 210, 120, 220, 3857), 3857)),
                (3, 'Charlie', 300.0, ST_SetSRID(ST_MakeEnvelope(120, 220, 130, 230, 3857), 3857));
            """;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<string> CreateConnectionAsync(string name)
    {
        using var http = _fixture.CreateHttpClient();
        if (!string.IsNullOrWhiteSpace(_fixture.Options.StudioAdminApiKey))
        {
            http.DefaultRequestHeaders.TryAddWithoutValidation("X-API-Key", _fixture.Options.StudioAdminApiKey);
        }

        var request = new
        {
            name,
            description = "Service-configuration round-trip connection",
            host = "postgres",
            port = 5432,
            databaseName = "honua",
            username = "honua",
            password = "honua",
            provider = "postgis",
            sslRequired = false,
            sslMode = "Disable"
        };

        using var response = await http.PostAsync(
            "/api/v1/admin/connections/",
            JsonContent.Create(request, options: JsonOptions));
        var payload = await response.Content.ReadAsStringAsync();
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(payload);
        Assert.True(document.RootElement.TryGetProperty("data", out var data), $"connection create payload: {payload}");
        var connectionId = data.GetProperty("connectionId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(connectionId), $"connection create returned no id: {payload}");
        return connectionId!;
    }
}
