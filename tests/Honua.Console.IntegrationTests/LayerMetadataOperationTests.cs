using System.Net;
using System.Text;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

public sealed class LayerMetadataOperationTests
{
    private static readonly Uri BaseAddress = new("https://honua.test");

    private static readonly ConsoleLayerEditingChange Change = new()
    {
        ServiceName = "parcels service",
        LayerId = 7,
        GlobalIdField = "globalid",
        AllowDelete = false,
    };

    [Fact]
    public async Task SetEditing_PutsTheEditingPatchToTheServiceLayerMetadataRoute()
    {
        HttpRequestMessage? sent = null;
        string? body = null;
        var operation = new HonuaServerConsoleLayerMetadataOperation(CreateClient(request =>
        {
            sent = request;
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(HttpStatusCode.OK, """{"success":true,"data":{"layerId":7,"layerName":"Parcels"}}""");
        }));

        var result = await operation.SetEditingAsync(Change);

        Assert.True(result.Succeeded, result.Detail);
        Assert.Equal(HttpMethod.Put, sent!.Method);
        Assert.Equal("/api/v1/admin/services/parcels%20service/layers/7/metadata", sent.RequestUri!.AbsolutePath);
        using var json = JsonDocument.Parse(body!);
        var editing = json.RootElement.GetProperty("editing");
        Assert.Equal("globalid", editing.GetProperty("globalIdField").GetString());
        Assert.False(editing.GetProperty("delete").GetBoolean());
        Assert.Equal(JsonValueKind.Null, editing.GetProperty("create").ValueKind);
    }

    [Fact]
    public async Task SetEditing_SurfacesTheServerRejection()
    {
        var operation = new HonuaServerConsoleLayerMetadataOperation(CreateClient(_ =>
            Json(HttpStatusCode.BadRequest, """{"success":false,"message":"globalIdField must name a UUID field"}""")));

        var result = await operation.SetEditingAsync(Change);

        Assert.False(result.Succeeded);
        Assert.Contains("globalIdField must name a UUID field", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unsupported_ReturnsMissingBinding()
    {
        var result = await new UnsupportedConsoleLayerMetadataOperation().SetEditingAsync(Change);

        Assert.False(result.Succeeded);
        Assert.Equal("Missing binding", result.State);
        Assert.Contains("HONUA_SERVER_BASE_URL", result.Detail, StringComparison.Ordinal);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static IHonuaAdminOperateClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var httpClient = new HttpClient(new StubHandler(responder)) { BaseAddress = BaseAddress };
        return new HonuaAdminOperateHttpClient(httpClient, new HonuaAdminOperateClientOptions(BaseAddress, "test-key"));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
