using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free coverage for the server-bound per-layer presentation authoring data source
/// (<see cref="ServerOperateLayerStyleOverrideDataSource"/>), which closes the Bucket 3-A popup-info +
/// drawing-info gap. Drives the real <see cref="HonuaAdminOperateHttpClient"/> over a recording HTTP handler
/// (never a mock server) to assert it:
///   - resolves the route's canonical resource id to the layer's global id via the live layers projection,
///   - GET-loads popupInfo + drawingInfo and surfaces them as the slot's raw JSON,
///   - PUT-saves both to the right admin routes with the exact server body shapes (the same shapes the live
///     e2e sends: popup-info {title,fieldInfos:[...]} and drawing-info {renderer:{...}}),
///   - clears a document by saving a blank editor value (PUT null),
///   - blocks an invalid-JSON edit before any network call, and surfaces a server rejection as a binding state.
/// The unconfigured (missing-binding) path stays covered by the existing render tests.
/// </summary>
public sealed class OperateLayerStyleOverrideServerTests
{
    private const string ResourceId = "conn-1-layer-1";
    private static readonly Uri BaseAddress = new("https://honua.test");

    [Fact]
    public async Task GetOverrides_ResolvesLayerId_AndLoadsPopupAndDrawingInfo()
    {
        var requests = new List<(HttpMethod Method, string? Path)>();
        var source = new ServerOperateLayerStyleOverrideDataSource(
            new FakeLayersSource(layerId: 1, resourceId: ResourceId, serviceName: "e2e_src_fs", layerName: "E2E Source"),
            CreateClient(request =>
            {
                requests.Add((request.Method, request.RequestUri?.AbsolutePath));
                if (request.RequestUri!.AbsolutePath.EndsWith("/popup-info", StringComparison.Ordinal))
                {
                    return OkDocument(1, JsonDocument.Parse("""{"title":"{name}","fieldInfos":[{"fieldName":"name","label":"Name","visible":true}]}""").RootElement);
                }

                return OkDocument(1, JsonDocument.Parse("""{"renderer":{"type":"uniqueValue","field1":"name","uniqueValueInfos":[{"value":"a","label":"A"}]}}""").RootElement);
            }));

        var view = await source.GetOverridesAsync(ResourceId);

        Assert.Null(view.BindingState);
        var slot = Assert.Single(view.Slots);
        Assert.Equal("1", slot.SlotId);
        Assert.Equal("E2E Source", slot.ServiceDisplayName);
        Assert.Contains("\"title\"", slot.PopupInfoJson!, StringComparison.Ordinal);
        Assert.Contains("\"fieldName\": \"name\"", slot.PopupInfoJson!, StringComparison.Ordinal); // pretty-printed
        Assert.Contains("\"uniqueValue\"", slot.DrawingInfoJson!, StringComparison.Ordinal);

        Assert.Contains(requests, r => r.Method == HttpMethod.Get && r.Path == "/api/v1/admin/metadata/layers/1/popup-info");
        Assert.Contains(requests, r => r.Method == HttpMethod.Get && r.Path == "/api/v1/admin/metadata/layers/1/drawing-info");
    }

    [Fact]
    public async Task SaveOverride_IssuesPutsWithExactServerBodies()
    {
        var bodies = new Dictionary<string, string>(StringComparer.Ordinal);
        var source = new ServerOperateLayerStyleOverrideDataSource(
            new FakeLayersSource(1, ResourceId, "e2e_src_fs", "E2E Source"),
            CreateClient(request =>
            {
                var path = request.RequestUri!.AbsolutePath;
                bodies[path] = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
                return OkDocument(1, null);
            }));

        var edit = new OperateLayerSlotStyleOverrideEdit(
            ResourceId,
            SlotId: "1",
            PopupInfoJson: """{"title":"{name}","fieldInfos":[{"fieldName":"name","label":"Name","visible":true}]}""",
            DrawingInfoJson: """{"renderer":{"type":"uniqueValue","field1":"name","uniqueValueInfos":[{"value":"a","label":"A"}]}}""");

        var result = await source.SaveOverrideAsync(edit);

        Assert.True(result.Succeeded);
        Assert.Null(result.BindingState);

        var popupBody = bodies["/api/v1/admin/metadata/layers/1/popup-info"];
        Assert.Contains("\"title\":\"{name}\"", popupBody, StringComparison.Ordinal);
        Assert.Contains("\"fieldName\":\"name\"", popupBody, StringComparison.Ordinal);

        var drawingBody = bodies["/api/v1/admin/metadata/layers/1/drawing-info"];
        Assert.Contains("\"renderer\":", drawingBody, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"uniqueValue\"", drawingBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveOverride_BlankEdit_ClearsDocumentsWithNullBody()
    {
        var bodies = new Dictionary<string, string>(StringComparer.Ordinal);
        var source = new ServerOperateLayerStyleOverrideDataSource(
            new FakeLayersSource(1, ResourceId, "e2e_src_fs", "E2E Source"),
            CreateClient(request =>
            {
                bodies[request.RequestUri!.AbsolutePath] = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
                return OkDocument(1, null);
            }));

        var result = await source.SaveOverrideAsync(new OperateLayerSlotStyleOverrideEdit(
            ResourceId, SlotId: "1", PopupInfoJson: "  ", DrawingInfoJson: string.Empty));

        Assert.True(result.Succeeded);
        Assert.Equal("null", bodies["/api/v1/admin/metadata/layers/1/popup-info"]);
        Assert.Equal("null", bodies["/api/v1/admin/metadata/layers/1/drawing-info"]);
    }

    [Fact]
    public async Task SaveOverride_InvalidJson_BlocksBeforeAnyNetworkCall()
    {
        var called = false;
        var source = new ServerOperateLayerStyleOverrideDataSource(
            new FakeLayersSource(1, ResourceId, "e2e_src_fs", "E2E Source"),
            CreateClient(_ =>
            {
                called = true;
                return OkDocument(1, null);
            }));

        var result = await source.SaveOverrideAsync(new OperateLayerSlotStyleOverrideEdit(
            ResourceId, SlotId: "1", PopupInfoJson: "{ not json", DrawingInfoJson: string.Empty));

        Assert.False(called);
        Assert.False(result.Succeeded);
        Assert.NotNull(result.BindingState);
        Assert.Equal(OperateLayerStyleBindingState.Unsupported, result.BindingState!.State);
    }

    [Fact]
    public async Task GetOverrides_WhenLayerNotResolvable_ReturnsMissingBinding()
    {
        var source = new ServerOperateLayerStyleOverrideDataSource(
            new FakeLayersSource(1, "conn-1-layer-99", "e2e_src_fs", "Other"),
            CreateClient(_ => OkDocument(1, null)));

        var view = await source.GetOverridesAsync(ResourceId);

        Assert.Empty(view.Slots);
        Assert.NotNull(view.BindingState);
        Assert.Equal(OperateLayerStyleBindingState.MissingBinding, view.BindingState!.State);
    }

    [Fact]
    public async Task GetOverrides_WhenServerForbids_SurfacesForbiddenBindingState()
    {
        var source = new ServerOperateLayerStyleOverrideDataSource(
            new FakeLayersSource(1, ResourceId, "e2e_src_fs", "E2E Source"),
            CreateClient(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)));

        var view = await source.GetOverridesAsync(ResourceId);

        Assert.Empty(view.Slots);
        Assert.Equal(OperateLayerStyleBindingState.Forbidden, view.BindingState!.State);
    }

    private static HttpResponseMessage OkDocument(int layerId, JsonElement? document) =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                success = true,
                data = new { layerId, document },
                timestamp = DateTimeOffset.UtcNow,
            }),
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

    // Minimal live-layers projection: only GetLayersViewAsync is exercised by the data source.
    private sealed class FakeLayersSource(int layerId, string resourceId, string serviceName, string layerName)
        : IOperateTransitionDataSource
    {
        public Task<OperateServicesView> GetLayersViewAsync(CancellationToken cancellationToken = default)
        {
            var layer = new OperateServiceLayerProjection(
                LayerId: layerId,
                Name: layerName,
                Geometry: "Polygon",
                CanonicalResourceId: resourceId,
                CanonicalResourceName: layerName);
            var service = new OperateServiceDetail(
                Name: serviceName,
                DisplayName: serviceName,
                ServiceType: "FeatureServer",
                RuntimeStatus: "running",
                MetadataOwnership: "server",
                Layers: [layer],
                RuntimeSettings: [],
                PublicationSlots: []);
            return Task.FromResult(new OperateServicesView([service], []));
        }

        public Task<OperateTransitionWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OperateConnectionSummary?> FindConnectionAsync(string connectionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OperateResourceEditPreview?> FindResourceEditAsync(string resourceId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OperateServiceDetail?> FindServiceAsync(string serviceName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
