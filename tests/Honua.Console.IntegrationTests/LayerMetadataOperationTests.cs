using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free coverage for the layer-metadata authoring OPERATION (Bucket-A layer-meta): the
/// <see cref="HonuaServerConsoleLayerMetadataOperation"/> over a stubbed admin client and the missing-binding
/// <see cref="UnsupportedConsoleLayerMetadataOperation"/>. Asserts the real route/verb/body each read+write
/// issues for display / editing / spatial (GET/PUT /api/v1/admin/metadata/layers/{id}/display|editing|spatial),
/// the result mapping, and that the unconfigured surface never performs a network call. No mocks of metadata —
/// every assertion is over the wire the operation actually sends, or what a recorded server response maps to.
/// </summary>
public sealed class LayerMetadataOperationTests
{
    private static readonly Uri BaseAddress = new("https://honua.test");

    // ---- Display ----

    [Fact]
    public async Task GetDisplay_IssuesGetToDisplayRoute_AndMapsFields()
    {
        string? path = null;
        HttpMethod? method = null;
        var data = new HonuaAdminLayerDisplay
        {
            LayerId = 1,
            MinScale = 100000,
            MaxScale = 500,
            DefaultVisibility = true,
            DisplayField = "name",
            Queryable = true,
            HasZ = false,
            HasM = true,
        };
        var operation = new HonuaServerConsoleLayerMetadataOperation(CreateClient(request =>
        {
            path = request.RequestUri?.AbsolutePath;
            method = request.Method;
            return Ok(data);
        }));

        var result = await operation.GetDisplayAsync(1);

        Assert.True(result.Bound);
        Assert.Equal(HttpMethod.Get, method);
        Assert.Equal("/api/v1/admin/metadata/layers/1/display", path);
        Assert.Equal(100000, result.MinScale);
        Assert.Equal(500, result.MaxScale);
        Assert.True(result.DefaultVisibility);
        Assert.Equal("name", result.DisplayField);
        Assert.True(result.Queryable);
        Assert.False(result.HasZ);
        Assert.True(result.HasM);
    }

    [Fact]
    public async Task SetDisplay_IssuesPutWithBody_AndMapsUpdated()
    {
        string? path = null;
        HttpMethod? method = null;
        string? body = null;
        var operation = new HonuaServerConsoleLayerMetadataOperation(CreateClient(request =>
        {
            path = request.RequestUri?.AbsolutePath;
            method = request.Method;
            body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Ok(new HonuaAdminLayerDisplay { LayerId = 1 });
        }));

        var result = await operation.SetDisplayAsync(1, new ConsoleLayerDisplay
        {
            MinScale = 100000,
            MaxScale = 500,
            DefaultVisibility = false,
            DisplayField = "name",
            Queryable = true,
            HasZ = true,
            HasM = false,
        });

        Assert.True(result.Succeeded);
        Assert.Equal("Updated", result.State);
        Assert.Equal(HttpMethod.Put, method);
        Assert.Equal("/api/v1/admin/metadata/layers/1/display", path);

        using var doc = JsonDocument.Parse(body!);
        var root = doc.RootElement;
        Assert.Equal(100000, root.GetProperty("minScale").GetDouble());
        Assert.Equal(500, root.GetProperty("maxScale").GetDouble());
        Assert.False(root.GetProperty("defaultVisibility").GetBoolean());
        Assert.Equal("name", root.GetProperty("displayField").GetString());
        Assert.True(root.GetProperty("queryable").GetBoolean());
        Assert.True(root.GetProperty("hasZ").GetBoolean());
        Assert.False(root.GetProperty("hasM").GetBoolean());
    }

    // ---- Editing ----

    [Fact]
    public async Task GetEditing_IssuesGetToEditingRoute_AndMapsFields()
    {
        string? path = null;
        HttpMethod? method = null;
        var data = new HonuaAdminLayerEditing
        {
            LayerId = 1,
            GlobalIdField = "globalid",
            CreatorField = "created_user",
            CreatedAtField = "created_date",
            EditorField = "last_edited_user",
            UpdatedAtField = "last_edited_date",
            CanModify = true,
            SupportsAttachments = false,
            SupportsRelatedRecords = true,
        };
        var operation = new HonuaServerConsoleLayerMetadataOperation(CreateClient(request =>
        {
            path = request.RequestUri?.AbsolutePath;
            method = request.Method;
            return Ok(data);
        }));

        var result = await operation.GetEditingAsync(1);

        Assert.True(result.Bound);
        Assert.Equal(HttpMethod.Get, method);
        Assert.Equal("/api/v1/admin/metadata/layers/1/editing", path);
        Assert.Equal("globalid", result.GlobalIdField);
        Assert.Equal("created_user", result.CreatorField);
        Assert.Equal("last_edited_date", result.UpdatedAtField);
        Assert.True(result.CanModify);
        Assert.False(result.SupportsAttachments);
        Assert.True(result.SupportsRelatedRecords);
    }

    [Fact]
    public async Task SetEditing_IssuesPutWithBody_AndMapsUpdated()
    {
        string? path = null;
        HttpMethod? method = null;
        string? body = null;
        var operation = new HonuaServerConsoleLayerMetadataOperation(CreateClient(request =>
        {
            path = request.RequestUri?.AbsolutePath;
            method = request.Method;
            body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Ok(new HonuaAdminLayerEditing { LayerId = 1 });
        }));

        var result = await operation.SetEditingAsync(1, new ConsoleLayerEditing
        {
            GlobalIdField = "globalid",
            CreatorField = "created_user",
            CanModify = true,
            SupportsAttachments = true,
            SupportsRelatedRecords = false,
        });

        Assert.True(result.Succeeded);
        Assert.Equal(HttpMethod.Put, method);
        Assert.Equal("/api/v1/admin/metadata/layers/1/editing", path);

        using var doc = JsonDocument.Parse(body!);
        var root = doc.RootElement;
        Assert.Equal("globalid", root.GetProperty("globalIdField").GetString());
        Assert.Equal("created_user", root.GetProperty("creatorField").GetString());
        Assert.True(root.GetProperty("canModify").GetBoolean());
        Assert.True(root.GetProperty("supportsAttachments").GetBoolean());
        Assert.False(root.GetProperty("supportsRelatedRecords").GetBoolean());
    }

    // ---- Spatial / CRS ----

    [Fact]
    public async Task GetSpatial_IssuesGetToSpatialRoute_AndMapsFields()
    {
        string? path = null;
        HttpMethod? method = null;
        var data = new HonuaAdminLayerSpatial
        {
            LayerId = 1,
            Srid = 4326,
            GeometryType = "polygon",
            SupportedCrs = ["http://www.opengis.net/def/crs/EPSG/0/4326", "http://www.opengis.net/def/crs/EPSG/0/3857"],
            StorageCrs = "http://www.opengis.net/def/crs/EPSG/0/4326",
            StorageCrsCoordinateEpoch = 2020.5,
        };
        var operation = new HonuaServerConsoleLayerMetadataOperation(CreateClient(request =>
        {
            path = request.RequestUri?.AbsolutePath;
            method = request.Method;
            return Ok(data);
        }));

        var result = await operation.GetSpatialAsync(1);

        Assert.True(result.Bound);
        Assert.Equal(HttpMethod.Get, method);
        Assert.Equal("/api/v1/admin/metadata/layers/1/spatial", path);
        Assert.Equal(4326, result.Srid);
        Assert.Equal("polygon", result.GeometryType);
        Assert.Equal(2, result.SupportedCrs.Count);
        Assert.Equal("http://www.opengis.net/def/crs/EPSG/0/4326", result.StorageCrs);
        Assert.Equal(2020.5, result.StorageCrsCoordinateEpoch);
    }

    [Fact]
    public async Task SetSpatial_WithList_IssuesPutWithSupportedCrs_AndMapsUpdated()
    {
        string? path = null;
        HttpMethod? method = null;
        string? body = null;
        var operation = new HonuaServerConsoleLayerMetadataOperation(CreateClient(request =>
        {
            path = request.RequestUri?.AbsolutePath;
            method = request.Method;
            body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Ok(new HonuaAdminLayerSpatial { LayerId = 1 });
        }));

        var result = await operation.SetSpatialAsync(
            1,
            ["http://www.opengis.net/def/crs/EPSG/0/4326"],
            "http://www.opengis.net/def/crs/EPSG/0/3857",
            2020.5,
            clearStorageCrs: false,
            clearStorageCrsCoordinateEpoch: false);

        Assert.True(result.Succeeded);
        Assert.Equal(HttpMethod.Put, method);
        Assert.Equal("/api/v1/admin/metadata/layers/1/spatial", path);

        using var doc = JsonDocument.Parse(body!);
        var root = doc.RootElement;
        var crs = root.GetProperty("supportedCrs");
        Assert.Equal(1, crs.GetArrayLength());
        Assert.Equal("http://www.opengis.net/def/crs/EPSG/0/4326", crs[0].GetString());
        Assert.Equal("http://www.opengis.net/def/crs/EPSG/0/3857", root.GetProperty("storageCrs").GetString());
        Assert.Equal(2020.5, root.GetProperty("storageCrsCoordinateEpoch").GetDouble());
        Assert.False(root.GetProperty("clearStorageCrs").GetBoolean());
        Assert.False(root.GetProperty("clearStorageCrsCoordinateEpoch").GetBoolean());
    }

    [Fact]
    public async Task SetSpatial_NullList_OmitsSupportedCrs_SoListIsLeftUnchanged()
    {
        string? body = null;
        var operation = new HonuaServerConsoleLayerMetadataOperation(CreateClient(request =>
        {
            body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Ok(new HonuaAdminLayerSpatial { LayerId = 1 });
        }));

        var result = await operation.SetSpatialAsync(
            1,
            supportedCrs: null,
            storageCrs: null,
            storageCrsCoordinateEpoch: null,
            clearStorageCrs: false,
            clearStorageCrsCoordinateEpoch: false);

        Assert.True(result.Succeeded);
        using var doc = JsonDocument.Parse(body!);
        // Omit = unchanged: the supportedCrs property must NOT appear on the wire when null.
        Assert.False(doc.RootElement.TryGetProperty("supportedCrs", out _));
    }

    [Fact]
    public async Task SetSpatial_EmptyList_SendsEmptyArray_ToClear()
    {
        string? body = null;
        var operation = new HonuaServerConsoleLayerMetadataOperation(CreateClient(request =>
        {
            body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Ok(new HonuaAdminLayerSpatial { LayerId = 1 });
        }));

        await operation.SetSpatialAsync(
            1,
            supportedCrs: [],
            storageCrs: null,
            storageCrsCoordinateEpoch: null,
            clearStorageCrs: false,
            clearStorageCrsCoordinateEpoch: false);

        Assert.Contains("\"supportedCrs\":[]", body!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetSpatial_WithClearFlags_SendsClearFlagsTrue()
    {
        string? body = null;
        var operation = new HonuaServerConsoleLayerMetadataOperation(CreateClient(request =>
        {
            body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Ok(new HonuaAdminLayerSpatial { LayerId = 1 });
        }));

        await operation.SetSpatialAsync(
            1,
            supportedCrs: null,
            storageCrs: "ignored-when-clearing",
            storageCrsCoordinateEpoch: 2020,
            clearStorageCrs: true,
            clearStorageCrsCoordinateEpoch: true);

        using var doc = JsonDocument.Parse(body!);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("clearStorageCrs").GetBoolean());
        Assert.True(root.GetProperty("clearStorageCrsCoordinateEpoch").GetBoolean());
        // When clearing, the scalar values are sent null (and therefore omitted) so the clear flag is authoritative.
        Assert.False(root.TryGetProperty("storageCrs", out _));
        Assert.False(root.TryGetProperty("storageCrsCoordinateEpoch", out _));
    }

    // ---- Rejection + missing-binding ----

    [Fact]
    public async Task SetDisplay_WhenServerRejects_MapsFailureWithDetail()
    {
        var operation = new HonuaServerConsoleLayerMetadataOperation(CreateClient(_ =>
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = JsonContent.Create(new { success = false, message = "displayField 'nope' is not a field on this layer." })
            }));

        var result = await operation.SetDisplayAsync(1, new ConsoleLayerDisplay { DisplayField = "nope" });

        Assert.False(result.Succeeded);
        Assert.Contains("not a field", result.Detail!, StringComparison.Ordinal);
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
