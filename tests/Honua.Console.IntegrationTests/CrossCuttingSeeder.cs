using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace Honua.Console.IntegrationTests;

internal static class CrossCuttingSeeder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task SeedPolygonTableAsync(string postgresConnectionString, string table)
    {
        await using var connection = new NpgsqlConnection(postgresConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        // Three square polygons in EPSG:3857 with a known bbox (100,200)-(130,230) and ascending area_m2.
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

    public static async Task<string> CreateConnectionAsync(CrossCuttingFixture fixture, string name)
    {
        using var http = fixture.CreateHttpClient();
        if (!string.IsNullOrWhiteSpace(fixture.AdminApiKey))
        {
            http.DefaultRequestHeaders.TryAddWithoutValidation("X-API-Key", fixture.AdminApiKey);
        }

        var request = new
        {
            name,
            description = "Cross-cutting round-trip connection",
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
