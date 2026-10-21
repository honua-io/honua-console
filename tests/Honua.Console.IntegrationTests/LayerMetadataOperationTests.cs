using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free coverage for display, editing, and spatial metadata. The pinned server does not
/// register those routes, so the live client returns Unsupported and issues no HTTP. The
/// missing-binding operation still names HONUA_SERVER_BASE_URL.
/// </summary>
public sealed class LayerMetadataOperationTests
{
    private static readonly Uri BaseAddress = new("https://honua.test");

    [Fact]
    public async Task UnregisteredDisplayEditingSpatialRoutes_FailClosedWithoutHttp()
    {
        var calls = 0;
        var operation = new HonuaServerConsoleLayerMetadataOperation(CreateClient(_ =>
        {
            calls++;
            throw new InvalidOperationException("unregistered route must not send HTTP");
        }));

        var display = await operation.GetDisplayAsync(1);
        var editing = await operation.GetEditingAsync(1);
        var spatial = await operation.GetSpatialAsync(1);
        var saveDisplay = await operation.SetDisplayAsync(1, new ConsoleLayerDisplay());
        var saveEditing = await operation.SetEditingAsync(1, new ConsoleLayerEditing());
        var saveSpatial = await operation.SetSpatialAsync(1, ["EPSG:4326"], null, null, false, false);

        Assert.Equal(0, calls);
        Assert.False(display.Bound);
        Assert.False(editing.Bound);
        Assert.False(spatial.Bound);
        Assert.Contains(UnregisteredServerRoutes.Detail, display.Detail, StringComparison.Ordinal);
        foreach (var write in new[] { saveDisplay, saveEditing, saveSpatial })
        {
            Assert.False(write.Succeeded);
            Assert.Equal(UnregisteredServerRoutes.State, write.State);
            Assert.Contains(UnregisteredServerRoutes.Detail, write.Detail, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Unsupported_NeverCallsNetwork_AndReturnsMissingBinding()
    {
        var operation = new UnsupportedConsoleLayerMetadataOperation();

        var display = await operation.GetDisplayAsync(1);
        var editing = await operation.GetEditingAsync(1);
        var spatial = await operation.GetSpatialAsync(1);
        var saveDisplay = await operation.SetDisplayAsync(1, new ConsoleLayerDisplay());
        var saveEditing = await operation.SetEditingAsync(1, new ConsoleLayerEditing());
        var saveSpatial = await operation.SetSpatialAsync(1, ["x"], null, null, false, false);

        Assert.False(display.Bound);
        Assert.False(editing.Bound);
        Assert.False(spatial.Bound);
        Assert.Contains("HONUA_SERVER_BASE_URL", display.Detail!, StringComparison.Ordinal);
        foreach (var write in new[] { saveDisplay, saveEditing, saveSpatial })
        {
            Assert.False(write.Succeeded);
            Assert.Equal("Missing binding", write.State);
            Assert.Contains("HONUA_SERVER_BASE_URL", write.Detail!, StringComparison.Ordinal);
        }
    }

    private static HttpResponseMessage Ok<T>(T data) =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { success = true, data, timestamp = DateTimeOffset.UtcNow })
        };

    private static IHonuaAdminOperateClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var httpClient = new HttpClient(new StubHandler(responder)) { BaseAddress = BaseAddress };
        return new HonuaAdminOperateHttpClient(httpClient, new HonuaAdminOperateClientOptions(BaseAddress, "test-key"));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Materialize content before returning so the request-body assertion can read it.
            _ = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            return Task.FromResult(responder(request));
        }
    }
}
