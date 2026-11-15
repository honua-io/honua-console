using System.Net;
using Honua.Console.Contracts;

namespace Honua.Console.Shell.Tests;

public sealed class PublishedLayerWireContractTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PublishingWritesPreserveStorageIdentityForMetadataRequests(bool publish)
    {
        var handler = ScriptedHttpHandler.Returning(HttpStatusCode.OK, """
            {"success":true,"data":{"storageLayerId":5,"layerName":"QA points","schema":"honua_data","table":"qa_points","geometryType":"Point","srid":4326,"primaryKey":"id","fieldCount":3,"enabled":true,"serviceName":"qa"}}
            """);
        using var client = new HonuaAdminOperateHttpClient(
            new HttpClient(handler), new HonuaAdminOperateClientOptions(new Uri("https://server.example")));

        var result = publish
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
