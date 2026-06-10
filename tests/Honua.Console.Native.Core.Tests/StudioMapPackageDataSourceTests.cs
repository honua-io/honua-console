using System.Net;
using System.Text;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Behaviour coverage for the map-builder mapper and the server-bound map data source. The data source
/// is driven over a recording <see cref="HttpMessageHandler"/> so the real Studio package-lifecycle +
/// publication wiring (request shapes, generation-safe updates, save-then-publish ordering, reopen, and
/// failure-path capability states) is asserted without a live server. No in-memory map client is involved.
/// </summary>
public sealed class StudioMapPackageDataSourceTests
{
    private static readonly Uri BaseUri = new("https://server.example");

    [Fact]
    public void ApplyGeneratedPackage_BindsOneLayerPerSourceBinding_NotTheRoundTripLayersShape()
    {
        // A server-generated honua_map_package.v1 body uses sourceBindings/initialView/popupBindings/legend —
        // NOT the console round-trip `layers` shape that ApplyEnvelopeBody reads. Regression: the map "from
        // prompt" flow fed this body to ApplyEnvelopeBody and bound 0 layers; the generation mapper must bind
        // one editor layer per sourceBinding and lift the extent/popups/legend.
        var package = JsonSerializer.Deserialize<JsonElement>(
            """
            {
              "mapPackageId": "map_city_parks",
              "format": "honua_map_package.v1",
              "sourceBindings": [
                { "sourceId": "src_city_parks", "protocol": "geoservices_feature_service", "filter": "type='park'" },
                { "sourceId": "src_streets", "protocol": "vector_tile" }
              ],
              "styleRefs": null,
              "initialView": { "bbox": [-122.4194, 37.7749, -122.0574, 37.8749], "crs": "EPSG:4326" },
              "popupBindings": [
                { "sourceId": "src_city_parks", "fieldName": "name" },
                { "sourceId": "src_city_parks", "fieldName": "acres" }
              ],
              "legend": [ { "label": "City Parks", "color": "#2D69A5" } ]
            }
            """);
        var state = new StudioMapEditorState();

        StudioMapPackageMapper.ApplyGeneratedPackage(state, package);

        Assert.Equal(2, state.Layers.Count);
        Assert.Equal("src_city_parks", state.Layers[0].SourceRef);
        Assert.Equal("City Parks", state.Layers[0].Title);
        Assert.Equal("type='park'", state.Layers[0].Filter);
        Assert.Equal("name, acres", state.Layers[0].PopupFields);
        Assert.Equal("src_streets", state.Layers[1].SourceRef);
        Assert.Equal("-122.4194,37.7749,-122.0574,37.8749", state.InitialExtent);
        Assert.True(state.ShowLegend);
        Assert.Equal("City Parks", state.Title);
    }

    [Fact]
    public void ApplyGeneratedPackage_CapturesBoundLayerIdAndServiceId_DrivingTheLiveRender()
    {
        // When generation binds a real catalog layer, the locator carries serviceId + layerId. The mapper must
        // lift them onto the editor layer (BoundServiceId / BoundLayerId) — this is what lets the builder
        // preview render the actual map via /map-proxy/styles/{BoundLayerId}.json. layerId may be a string or
        // a number; both must be captured (small local models are inconsistent).
        var package = JsonSerializer.Deserialize<JsonElement>(
            """
            {
              "mapPackageId": "map_e2e_parcels",
              "format": "honua_map_package.v1",
              "sourceBindings": [
                {
                  "sourceId": "src_e2e_parcels",
                  "protocol": "geoservices_feature_service",
                  "locator": { "url": "https://placeholder/e2e_src_fs", "serviceId": "e2e_src_fs", "layerId": 1 }
                }
              ],
              "initialView": { "bbox": [0, 0, 0.0449, 0.0449], "crs": "EPSG:4326" }
            }
            """);
        var state = new StudioMapEditorState();

        StudioMapPackageMapper.ApplyGeneratedPackage(state, package);

        Assert.Single(state.Layers);
        Assert.Equal("1", state.Layers[0].BoundLayerId);
        Assert.Equal("e2e_src_fs", state.Layers[0].BoundServiceId);
    }

    [Fact]
    public void ApplyGeneratedPackage_NullOrEmptyBody_DoesNotThrowAndBindsNoLayers()
    {
        var state = new StudioMapEditorState();
        StudioMapPackageMapper.ApplyGeneratedPackage(state, null);
        StudioMapPackageMapper.ApplyGeneratedPackage(state, JsonSerializer.Deserialize<JsonElement>("{}"));
        Assert.Empty(state.Layers);
    }

    [Fact]
    public void EnvelopeBody_RoundTripsLayersFrameBehaviourAndSharePolicy()
    {
        var state = new StudioMapEditorState
        {
            Title = "Public works",
            Description = "Hydrants and mains",
            Basemap = "basemap:streets",
            InitialExtent = "-158.3,21.2,-157.6,21.7",
            ShowLegend = false,
            PopupsEnabled = false,
            InteractionsEnabled = false,
            ShareTier = "workspace",
            EmbedAllowed = true
        };
        state.Layers.Add(new StudioMapLayerEditor
        {
            SourceRef = "content:hydrants@v12",
            Title = "Hydrants",
            Visible = false,
            Filter = "status = 'active'",
            Style = "style:point-red",
            PopupFields = "asset_id, condition"
        });

        var body = StudioMapPackageMapper.BuildEnvelopeBody(state);

        // Round-trips through the serializer and back into a fresh editor state.
        var json = JsonSerializer.Serialize(body);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(StudioMapPackageMapper.SchemaVersion, document.RootElement.GetProperty("schemaVersion").GetString());

        var rehydrated = StudioMapPackageMapper.CreateTemplate();
        StudioMapPackageMapper.ApplyEnvelopeBody(rehydrated, document.RootElement.Clone());

        Assert.Equal("Public works", rehydrated.Title);
        Assert.Equal("Hydrants and mains", rehydrated.Description);
        Assert.Equal("basemap:streets", rehydrated.Basemap);
        Assert.Equal("-158.3,21.2,-157.6,21.7", rehydrated.InitialExtent);
        Assert.False(rehydrated.ShowLegend);
        Assert.False(rehydrated.PopupsEnabled);
        Assert.False(rehydrated.InteractionsEnabled);
        Assert.Equal("workspace", rehydrated.ShareTier);
        Assert.True(rehydrated.EmbedAllowed);

        var layer = Assert.Single(rehydrated.Layers);
        Assert.Equal("content:hydrants@v12", layer.SourceRef);
        Assert.Equal("Hydrants", layer.Title);
        Assert.False(layer.Visible);
        Assert.Equal("status = 'active'", layer.Filter);
        Assert.Equal("style:point-red", layer.Style);
        Assert.Equal("asset_id, condition", layer.PopupFields);
    }

    [Fact]
    public void BuildPackageKey_SlugsTitleAndFallsBackWhenBlank()
    {
        Assert.Equal("studio-map-public-works", StudioMapPackageMapper.BuildPackageKey(new StudioMapEditorState { Title = "Public Works!" }));
        Assert.Equal("studio-map", StudioMapPackageMapper.BuildPackageKey(new StudioMapEditorState { Title = "   " }));
    }

    [Fact]
    public async Task GetWorkspace_WithNoDrafts_ReturnsEmptyListWithoutCapabilityState()
    {
        var handler = new RecordingHandler();
        handler.Map(
            HttpMethod.Get,
            "/api/v1/studio/package-drafts",
            """{"success":true,"data":{"drafts":[]}}""");
        var source = CreateSource(handler);

        var workspace = await source.GetWorkspaceAsync();

        Assert.Empty(workspace.Packages);
        Assert.Empty(workspace.CapabilityStates);
        Assert.Equal("/api/v1/studio/package-drafts", Assert.Single(handler.RequestedPaths).Path);
    }

    [Fact]
    public async Task GetWorkspace_EnumeratesLiveMapDrafts()
    {
        var draftId = Guid.NewGuid();
        var listJson = "{\"success\":true,\"data\":{\"drafts\":[{"
            + "\"draftId\":\"" + draftId + "\","
            + "\"itemId\":\"" + Guid.NewGuid() + "\","
            + "\"packageKey\":\"studio-map-public-works\","
            + "\"family\":\"map\",\"validationStatus\":\"valid\",\"generation\":1,"
            + "\"createdAt\":\"2026-01-01T00:00:00Z\",\"updatedAt\":\"2026-01-02T00:00:00Z\"}]}}";
        var handler = new RecordingHandler();
        handler.Map(HttpMethod.Get, "/api/v1/studio/package-drafts", listJson);
        var source = CreateSource(handler);

        var workspace = await source.GetWorkspaceAsync();

        Assert.Empty(workspace.CapabilityStates);
        var item = Assert.Single(workspace.Packages);
        Assert.Equal(draftId.ToString(), item.MapId);
        Assert.Equal("studio-map-public-works", item.Title);
    }

    [Fact]
    public async Task GetWorkspace_WhenListEndpointFails_SurfacesCapabilityState()
    {
        var handler = new RecordingHandler();
        handler.MapStatus(HttpMethod.Get, "/api/v1/studio/package-drafts", HttpStatusCode.Forbidden);
        var source = CreateSource(handler);

        var workspace = await source.GetWorkspaceAsync();

        Assert.Empty(workspace.Packages);
        var state = Assert.Single(workspace.CapabilityStates);
        Assert.Contains("list", state.Contract, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Load_NewMap_ReturnsBlankScaffoldWithoutCallingServer()
    {
        var handler = new RecordingHandler();
        var source = CreateSource(handler);

        var load = await source.LoadAsync(null);

        Assert.True(load.HasEditor);
        Assert.Empty(load.State!.Layers);
        Assert.Null(load.State.DraftId);
        Assert.Empty(handler.RequestedPaths); // a brand-new map never hits the server
    }

    [Fact]
    public async Task Load_NonGuidMapId_SurfacesUnsupportedRatherThanCallingServer()
    {
        var handler = new RecordingHandler();
        var source = CreateSource(handler);

        var load = await source.LoadAsync("not-a-guid");

        Assert.False(load.HasEditor);
        Assert.Equal("Unsupported", Assert.Single(load.CapabilityStates).State);
        Assert.Empty(handler.RequestedPaths);
    }

    [Fact]
    public async Task SaveDraft_NewMap_PostsCreateDraftAndCarriesServerIdentity()
    {
        var draftId = Guid.NewGuid();
        var handler = new RecordingHandler();
        handler.Map(HttpMethod.Post, "/api/v1/studio/package-drafts", DraftJson(draftId, Guid.Empty, generation: 1));
        var source = CreateSource(handler);

        var result = await source.SaveDraftAsync(ReadyState());

        Assert.True(result.Succeeded);
        Assert.NotNull(result.State);
        Assert.Equal(draftId, result.State!.DraftId);
        Assert.Equal(1, result.State.Generation);
        Assert.Equal("/api/v1/studio/package-drafts", Assert.Single(handler.RequestedPaths).Path);

        // The create body carries the map family + the authored layers projected into the envelope body.
        var body = handler.LastRequestBody!;
        using var document = JsonDocument.Parse(body);
        Assert.Equal("map", document.RootElement.GetProperty("envelope").GetProperty("family").GetString());
        Assert.Equal(
            "content:hydrants@v12",
            document.RootElement.GetProperty("envelope").GetProperty("body").GetProperty("layers")[0].GetProperty("sourceRef").GetString());
    }

    [Fact]
    public async Task SaveDraft_ExistingMap_PutsUpdateWithGenerationForOptimisticConcurrency()
    {
        var draftId = Guid.NewGuid();
        var handler = new RecordingHandler();
        handler.Map(HttpMethod.Put, $"/api/v1/studio/package-drafts/{draftId}", DraftJson(draftId, Guid.Empty, generation: 8));
        var source = CreateSource(handler);

        var state = ReadyState();
        state.DraftId = draftId;
        state.Generation = 7;

        var result = await source.SaveDraftAsync(state);

        Assert.True(result.Succeeded);
        Assert.Equal(8, result.State!.Generation);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        using var document = JsonDocument.Parse(request.Body!);
        Assert.Equal(7, document.RootElement.GetProperty("generation").GetInt64());
    }

    [Fact]
    public async Task SaveDraft_PublishedVersion_RefusesWithoutCallingServer()
    {
        var handler = new RecordingHandler();
        var source = CreateSource(handler);

        var state = ReadyState();
        state.Status = StudioMapStatuses.Published;

        var result = await source.SaveDraftAsync(state);

        Assert.False(result.Succeeded);
        Assert.Contains("Reopen", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.RequestedPaths);
    }

    [Fact]
    public async Task SaveDraft_ConflictResponse_SurfacesConflictCapabilityState()
    {
        var draftId = Guid.NewGuid();
        var handler = new RecordingHandler();
        handler.MapStatus(HttpMethod.Put, $"/api/v1/studio/package-drafts/{draftId}", HttpStatusCode.Conflict);
        var source = CreateSource(handler);

        var state = ReadyState();
        state.DraftId = draftId;
        state.Generation = 3;

        var result = await source.SaveDraftAsync(state);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Issue);
        Assert.Equal("Conflict", result.Issue!.State);
    }

    [Fact]
    public async Task Publish_FreezesContentVersionThenRoutesToPublicationRegistry()
    {
        var draftId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var handler = new RecordingHandler();
        handler.Map(
            HttpMethod.Post,
            $"/api/v1/studio/package-drafts/{draftId}/content-versions",
            ContentVersionJson(itemId, versionId, versionNumber: 4));
        handler.Map(
            HttpMethod.Post,
            $"/api/v1/studio/content-items/{itemId}/versions/{versionId}/publish-requests",
            PublicationRequestJson(itemId, versionId, status: "accepted"));
        var source = CreateSource(handler);

        var state = ReadyState();
        state.DraftId = draftId;

        var result = await source.PublishAsync(state);

        Assert.True(result.Succeeded);
        Assert.Equal(StudioMapStatuses.Published, result.State!.Status);
        Assert.Equal(4, result.State.Version);
        Assert.Equal(itemId, result.State.ItemId);
        Assert.Equal(versionId, result.State.VersionId);

        // The save-version call must precede the publish-request call (publish freezes then routes).
        Assert.Collection(
            handler.RequestedPaths,
            first => Assert.EndsWith("/content-versions", first.Path, StringComparison.Ordinal),
            second => Assert.EndsWith("/publish-requests", second.Path, StringComparison.Ordinal));

        // The publish intent carries the reviewed share tier + embed decision (AC#2).
        using var document = JsonDocument.Parse(handler.LastRequestBody!);
        var intent = document.RootElement.GetProperty("intent");
        Assert.Equal(state.ShareTier, intent.GetProperty("visibility").GetString());
        Assert.True(intent.GetProperty("embed").GetBoolean());
    }

    [Fact]
    public async Task Publish_IncompleteMap_IsGatedBeforeAnyServerCall()
    {
        var handler = new RecordingHandler();
        var source = CreateSource(handler);

        var state = new StudioMapEditorState { Title = "Incomplete", DraftId = Guid.NewGuid() };

        var result = await source.PublishAsync(state);

        Assert.False(result.Succeeded);
        Assert.Contains("Add at least one layer.", result.Message, StringComparison.Ordinal);
        Assert.Empty(handler.RequestedPaths);
    }

    [Fact]
    public async Task Publish_UnsavedMap_RefusesUntilDraftIsSaved()
    {
        var handler = new RecordingHandler();
        var source = CreateSource(handler);

        var result = await source.PublishAsync(ReadyState());

        Assert.False(result.Succeeded);
        Assert.Contains("Save", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.RequestedPaths);
    }

    [Fact]
    public async Task Publish_VersionSaveFails_DoesNotCallPublicationRegistry()
    {
        var draftId = Guid.NewGuid();
        var handler = new RecordingHandler();
        handler.MapStatus(
            HttpMethod.Post,
            $"/api/v1/studio/package-drafts/{draftId}/content-versions",
            HttpStatusCode.Forbidden);
        var source = CreateSource(handler);

        var state = ReadyState();
        state.DraftId = draftId;

        var result = await source.PublishAsync(state);

        Assert.False(result.Succeeded);
        Assert.Equal("Missing permission", result.Issue!.State);
        Assert.Single(handler.RequestedPaths); // never reached the publish-request route
        Assert.EndsWith("/content-versions", handler.RequestedPaths[0].Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reopen_PostsReopenAndReturnsFreshDraftSeededFromVersion()
    {
        var itemId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var newDraftId = Guid.NewGuid();
        var handler = new RecordingHandler();
        handler.Map(
            HttpMethod.Post,
            $"/api/v1/studio/content-items/{itemId}/versions/{versionId}/reopen",
            DraftJson(newDraftId, itemId, generation: 0));
        var source = CreateSource(handler);

        var published = ReadyState();
        published.ItemId = itemId;
        published.VersionId = versionId;
        published.Version = 4;
        published.Status = StudioMapStatuses.Published;

        var result = await source.ReopenAsync(published);

        Assert.True(result.Succeeded);
        Assert.Equal(newDraftId, result.State!.DraftId);
        Assert.Equal(StudioMapStatuses.Draft, result.State.Status);
        Assert.Equal(4, result.State.ReopenedFromVersion);
        Assert.EndsWith("/reopen", Assert.Single(handler.RequestedPaths).Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reopen_WithoutServerVersion_RefusesWithoutCallingServer()
    {
        var handler = new RecordingHandler();
        var source = CreateSource(handler);

        var result = await source.ReopenAsync(new StudioMapEditorState { Status = StudioMapStatuses.Published });

        Assert.False(result.Succeeded);
        Assert.Empty(handler.RequestedPaths);
    }

    private static StudioMapEditorState ReadyState()
    {
        var state = new StudioMapEditorState
        {
            Title = "Public works",
            Basemap = "basemap:streets",
            InitialExtent = "-158.3,21.2,-157.6,21.7",
            ShareTier = "workspace",
            EmbedAllowed = true
        };
        state.Layers.Add(new StudioMapLayerEditor { SourceRef = "content:hydrants@v12", Title = "Hydrants" });
        return state;
    }

    private static HonuaServerStudioMapPackageDataSource CreateSource(RecordingHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = BaseUri };
        var client = new HttpStudioPackageLifecycleClient(
            httpClient,
            new StudioPackageLifecycleClientOptions(BaseUri, "test-api-key"));
        return new HonuaServerStudioMapPackageDataSource(client, new NoopStudioMapGenerationClient(), new UnsupportedOperateTransitionDataSource());
    }

    private static string DraftJson(Guid draftId, Guid itemId, long generation)
    {
        var data = new
        {
            draftId,
            itemId,
            packageKey = "studio-map-public-works",
            family = "map",
            generation,
            envelope = new
            {
                family = "map",
                schemaVersion = StudioMapPackageMapper.SchemaVersion,
                body = new
                {
                    schemaVersion = StudioMapPackageMapper.SchemaVersion,
                    title = "Public works",
                    layers = new[]
                    {
                        new { sourceRef = "content:hydrants@v12", title = "Hydrants", visible = true }
                    }
                }
            },
            validation = new { status = "not-validated" },
            createdAt = "2026-05-30T00:00:00Z",
            updatedAt = "2026-05-30T00:00:00Z"
        };
        return Envelope(data);
    }

    private static string ContentVersionJson(Guid itemId, Guid versionId, int versionNumber)
    {
        var data = new
        {
            itemId,
            packageKey = "studio-map-public-works",
            versionId,
            versionNumber,
            contentHash = "abc",
            envelope = new { family = "map", schemaVersion = StudioMapPackageMapper.SchemaVersion },
            validation = new { status = "valid" },
            createdAt = "2026-05-30T00:00:00Z"
        };
        return Envelope(data);
    }

    private static string PublicationRequestJson(Guid itemId, Guid versionId, string status)
    {
        var data = new
        {
            requestId = Guid.NewGuid(),
            itemId,
            versionId,
            status,
            validation = new { status = "valid" },
            createdAt = "2026-05-30T00:00:00Z"
        };
        return Envelope(data);
    }

    private static string Envelope(object data) =>
        JsonSerializer.Serialize(new { success = true, data });

    private sealed record RecordedRequest(HttpMethod Method, string Path, string? Body);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode Status, string? Json)> _responses = new(StringComparer.Ordinal);
        private readonly List<RecordedRequest> _requests = new();

        public IReadOnlyList<RecordedRequest> Requests => _requests;

        public IReadOnlyList<RecordedRequest> RequestedPaths => _requests;

        public string? LastRequestBody => _requests.Count == 0 ? null : _requests[^1].Body;

        public void Map(HttpMethod method, string path, string json) =>
            _responses[Key(method, path)] = (HttpStatusCode.OK, json);

        public void MapStatus(HttpMethod method, string path, HttpStatusCode status) =>
            _responses[Key(method, path)] = (status, null);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _requests.Add(new RecordedRequest(request.Method, path, body));

            if (_responses.TryGetValue(Key(request.Method, path), out var mapped))
            {
                if (mapped.Json is null)
                {
                    return new HttpResponseMessage(mapped.Status)
                    {
                        Content = new StringContent(
                            """{"success":false,"message":"mapped failure"}""",
                            Encoding.UTF8,
                            "application/json")
                    };
                }

                return new HttpResponseMessage(mapped.Status)
                {
                    Content = new StringContent(mapped.Json, Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent(
                    """{"success":false,"message":"missing fixture"}""",
                    Encoding.UTF8,
                    "application/json")
            };
        }

        private static string Key(HttpMethod method, string path) => $"{method} {path}";
    }
}
