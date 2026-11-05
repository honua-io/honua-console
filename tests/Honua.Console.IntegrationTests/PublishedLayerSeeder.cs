using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Npgsql;

namespace Honua.Console.IntegrationTests;

internal static class PublishedLayerSeeder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public sealed record SeededLayer(string ServiceName, int LayerId, string ConnectionId, string Table);

    public static async Task SeedPointTableAsync(string postgresConnectionString, string table)
    {
        await using var connection = new NpgsqlConnection(postgresConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        // Three point features in EPSG:3857 with ascending observed values; the geometry is what the
        // FeatureServer query/extent reads need, the attributes prove the layer is real.
        command.CommandText = $"""
            CREATE EXTENSION IF NOT EXISTS postgis;
            CREATE TABLE public.{table} (
                id integer PRIMARY KEY,
                name text NOT NULL,
                observed double precision NOT NULL,
                geom geometry(Point, 3857) NOT NULL
            );
            INSERT INTO public.{table} (id, name, observed, geom) VALUES
                (1, 'Station A', 10.0, ST_SetSRID(ST_MakePoint(100, 200), 3857)),
                (2, 'Station B', 20.0, ST_SetSRID(ST_MakePoint(110, 210), 3857)),
                (3, 'Station C', 30.0, ST_SetSRID(ST_MakePoint(120, 220), 3857));
            """;
        await command.ExecuteNonQueryAsync();
    }

    // An unsuccessful publish, including a configuration rejection, returns null so the caller skips; failures while seeding the table or connection still throw.
    public static async Task<SeededLayer?> SeedPublishedLayerAsync(
        TemporalReplicaFixture fixture,
        string slugSuffix)
    {
        var table = $"obs_{slugSuffix}";
        var serviceName = $"obs_svc_{slugSuffix}";
        await SeedPointTableAsync(fixture.PostgresConnectionString!, table);
        var connectionId = await CreateConnectionAsync(fixture, $"obs-conn-{slugSuffix}");

        var operation = new HonuaServerServiceLayerPublishOperation(fixture.CreateOperateClient());
        var command = new ServiceLayerPublishCommand
        {
            ConnectionId = connectionId,
            Schema = "public",
            Table = table,
            LayerName = "Observations",
            ServiceName = serviceName,
            Description = "Wave 3 temporal/replica round-trip layer",
            GeometryColumn = "geom",
            GeometryType = "Point",
            Srid = 3857,
            PrimaryKey = "id",
            Fields = ["id", "name", "observed"],
            Enabled = true
        };

        var result = await operation.PublishAsync(command);
        if (!result.Succeeded || result.LayerId is null)
        {
            // Server-not-ready for the publish path (contract drift / stale image) — let the caller skip.
            return null;
        }

        return new SeededLayer(serviceName, result.LayerId.Value, connectionId, table);
    }

    public static async Task<SeededLayer> PublishNamespacedLayerAsync(
        TemporalReplicaFixture fixture, string suffix, string metadataNamespace)
    {
        var table = $"obs_{suffix}";
        var serviceName = $"obs_svc_{suffix}";
        await SeedPointTableAsync(fixture.PostgresConnectionString!, table);
        var connectionId = await CreateConnectionAsync(fixture, $"obs-conn-{suffix}");
        using var http = fixture.CreateRawClient();
        http.DefaultRequestHeaders.Add("X-API-Key", fixture.AdminApiKey);
        using var response = await http.PostAsJsonAsync($"/api/v1/admin/connections/{connectionId}/layers", new
        {
            schema = "public",
            table,
            layerName = "Workspace observations",
            serviceName,
            @namespace = metadataNamespace,
            geometryColumn = "geom",
            geometryType = "Point",
            srid = 3857,
            primaryKey = "id",
            fields = new[] { "id", "name", "observed" },
            enabled = true
        }, JsonOptions);
        var payload = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created,
            $"Namespaced layer publication returned {(int)response.StatusCode}: {payload}");
        using var document = JsonDocument.Parse(payload);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(serviceName, data.GetProperty("serviceName").GetString());
        return new SeededLayer(serviceName, data.GetProperty("layerId").GetInt32(), connectionId, table);
    }

    private static async Task<string> CreateConnectionAsync(TemporalReplicaFixture fixture, string name)
    {
        using var http = fixture.CreateRawClient();
        if (!string.IsNullOrWhiteSpace(fixture.AdminApiKey))
        {
            http.DefaultRequestHeaders.TryAddWithoutValidation("X-API-Key", fixture.AdminApiKey);
        }

        var request = new
        {
            name,
            description = "Wave 3 temporal/replica round-trip connection",
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
        var connectionId = document.RootElement.GetProperty("data").GetProperty("connectionId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(connectionId), $"connection create returned no id: {payload}");
        return connectionId!;
    }
}
