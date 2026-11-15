using System.Net;
using System.Text.Json;
using Honua.Console.Contracts;

namespace Honua.Console.Shell.Tests;

public sealed class PublishedLayerWireContractTests
{
    [Theory]
    [InlineData("{\"layerId\":7}", 7)]
    [InlineData("{\"layerId\":7,\"storageLayerId\":8}", 8)]
    [InlineData("{\"storageLayerId\":8,\"layerId\":7}", 8)]
    [InlineData("{\"layerId\":7,\"storageLayerId\":0}", 0)]
    public void PublishedLayerReadsLegacyIdentityAndPrefersStorageIdentity(string json, int expectedId)
    {
        var layer = JsonSerializer.Deserialize<HonuaAdminPublishedLayerSummary>(json)!;

        Assert.Equal(expectedId, layer.LayerId);
        var serialized = JsonSerializer.SerializeToElement(layer);
        Assert.Equal(expectedId, serialized.GetProperty("storageLayerId").GetInt32());
        Assert.False(serialized.TryGetProperty("layerId", out _));
    }

    [Theory]
    [InlineData("publish")]
    [InlineData("enable")]
    public async Task PublishingWritesPreserveStorageIdentityForMetadataRequests(string operation)
    {
        var handler = ScriptedHttpHandler.Returning(HttpStatusCode.OK, """
            {"success":true,"data":{"storageLayerId":5,"layerName":"QA points","schema":"honua_data","table":"qa_points","geometryType":"Point","srid":4326,"primaryKey":"id","fieldCount":3,"enabled":true,"serviceName":"qa"}}
            """);
        using var client = new HonuaAdminOperateHttpClient(
            new HttpClient(handler), new HonuaAdminOperateClientOptions(new Uri("https://server.example")));

        var result = operation == "publish"
            ? await client.PublishLayerAsync("connection", new() { Schema = "honua_data", Table = "qa_points", LayerName = "QA points" })
            : await client.SetLayerEnabledAsync("connection", 5, true, "qa");

        Assert.Null(result.Issue);
        var layer = Assert.IsType<HonuaAdminPublishedLayerSummary>(result.Data);
        Assert.Equal(5, layer.LayerId);
        Assert.Equal("qa", layer.ServiceName);
        Assert.Equal(4326, layer.Srid);
        Assert.Equal(3, layer.FieldCount);
        Assert.True(layer.Enabled);

        await client.GetLayerFieldsAsync(layer.LayerId);
        Assert.Equal("/api/v1/admin/metadata/layers/5/fields", handler.Requests.Last().Uri?.AbsolutePath);
    }
}
