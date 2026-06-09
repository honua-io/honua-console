using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free coverage for the layer permanent-filter OPERATION: the
/// <see cref="HonuaServerConsoleLayerFilterOperation"/> over a stubbed admin client and the missing-binding
/// <see cref="UnsupportedConsoleLayerFilterOperation"/>. Asserts the real route/verb/body each read+write
/// issues (GET/PUT /api/v1/admin/metadata/layers/{id}/filter), that a save sends
/// { permanentFilter: { expression, language } }, that a clear sends { permanentFilter: null }, that a 400
/// surfaces the server's validation reason verbatim (Charter section 11 — never claim success), and that the
/// unconfigured surface never performs a network call. No mocks of filter data — every assertion is over the
/// wire the operation actually sends, or what a recorded server response maps to.
/// </summary>
public sealed class LayerFilterOperationTests
{
    private static readonly Uri BaseAddress = new("https://honua.test");

    [Fact]
    public async Task GetFilter_IssuesGetToFilterRoute_AndMapsSavedFilter()
    {
        string? path = null;
        HttpMethod? method = null;
        var data = new HonuaAdminLayerFilter
        {
            LayerId = 1,
            PermanentFilter = new HonuaAdminPermanentFilter
            {
                Expression = "status = 'active'",
                Language = "arcgis-sql",
            },
        };
        var operation = new HonuaServerConsoleLayerFilterOperation(CreateClient(request =>
        {
            path = request.RequestUri?.AbsolutePath;
            method = request.Method;
            return Ok(data);
        }));

        var result = await operation.GetFilterAsync(1);

        Assert.True(result.Bound);
        Assert.Equal(HttpMethod.Get, method);
        Assert.Equal("/api/v1/admin/metadata/layers/1/filter", path);
        Assert.True(result.HasFilter);
        Assert.Equal("status = 'active'", result.Expression);
        Assert.Equal("arcgis-sql", result.Language);
    }

    [Fact]
    public async Task GetFilter_WhenNoFilterSaved_MapsHasFilterFalse()
    {
        var operation = new HonuaServerConsoleLayerFilterOperation(CreateClient(_ =>
            Ok(new HonuaAdminLayerFilter { LayerId = 1, PermanentFilter = null })));

        var result = await operation.GetFilterAsync(1);

        Assert.True(result.Bound);
        Assert.False(result.HasFilter);
        Assert.Equal(string.Empty, result.Expression);
    }

    [Fact]
    public async Task SaveFilter_IssuesPutWithPermanentFilter_AndMapsSaved()
    {
        string? path = null;
        HttpMethod? method = null;
        string? body = null;
        var operation = new HonuaServerConsoleLayerFilterOperation(CreateClient(request =>
        {
            path = request.RequestUri?.AbsolutePath;
            method = request.Method;
            body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Ok(new HonuaAdminLayerFilter
            {
                LayerId = 1,
                PermanentFilter = new HonuaAdminPermanentFilter { Expression = "status = 'active'", Language = "cql2-text" },
            });
        }));

        var result = await operation.SaveFilterAsync(1, "status = 'active'", "cql2-text");

        Assert.True(result.Succeeded);
        Assert.Equal("Saved", result.State);
        Assert.Equal(HttpMethod.Put, method);
        Assert.Equal("/api/v1/admin/metadata/layers/1/filter", path);

        // Assert the PUT carried { permanentFilter: { expression, language } }.
        using var doc = JsonDocument.Parse(body!);
        var filter = doc.RootElement.GetProperty("permanentFilter");
        Assert.Equal(JsonValueKind.Object, filter.ValueKind);
        Assert.Equal("status = 'active'", filter.GetProperty("expression").GetString());
        Assert.Equal("cql2-text", filter.GetProperty("language").GetString());
    }

    [Fact]
    public async Task SaveFilter_WhenExpressionBlank_DoesNotCallServer_AndReportsInvalid()
    {
        var called = false;
        var operation = new HonuaServerConsoleLayerFilterOperation(CreateClient(_ =>
        {
            called = true;
            return Ok(new HonuaAdminLayerFilter { LayerId = 1 });
        }));

        var result = await operation.SaveFilterAsync(1, "   ", "arcgis-sql");

        Assert.False(called);
        Assert.False(result.Succeeded);
        Assert.Equal("Invalid", result.State);
    }

    [Fact]
    public async Task ClearFilter_IssuesPutWithNullPermanentFilter_AndMapsCleared()
    {
        string? path = null;
        HttpMethod? method = null;
        string? body = null;
        var operation = new HonuaServerConsoleLayerFilterOperation(CreateClient(request =>
        {
            path = request.RequestUri?.AbsolutePath;
            method = request.Method;
            body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Ok(new HonuaAdminLayerFilter { LayerId = 1, PermanentFilter = null });
        }));

        var result = await operation.ClearFilterAsync(1);

        Assert.True(result.Succeeded);
        Assert.Equal("Cleared", result.State);
        Assert.Equal(HttpMethod.Put, method);
        Assert.Equal("/api/v1/admin/metadata/layers/1/filter", path);

        // Assert the PUT carried { "permanentFilter": null } (the explicit CLEAR contract).
        Assert.Contains("\"permanentFilter\":null", body!, StringComparison.Ordinal);
        using var doc = JsonDocument.Parse(body!);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("permanentFilter").ValueKind);
    }

    [Fact]
    public async Task SaveFilter_WhenServerRejectsExpression_SurfacesValidationReason()
    {
        // The server validates the expression against the layer schema and answers 400 with a reason.
        var operation = new HonuaServerConsoleLayerFilterOperation(CreateClient(_ =>
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = JsonContent.Create(new
                {
                    success = false,
                    message = "Unknown field 'stattus' in filter expression.",
                }),
            }));

        var result = await operation.SaveFilterAsync(1, "stattus = 'active'", "arcgis-sql");

        Assert.False(result.Succeeded);
        Assert.Contains("Unknown field 'stattus'", result.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unsupported_NeverCallsNetwork_AndReturnsMissingBinding()
    {
        var operation = new UnsupportedConsoleLayerFilterOperation();

        var read = await operation.GetFilterAsync(1);
        var save = await operation.SaveFilterAsync(1, "status = 'active'", "arcgis-sql");
        var clear = await operation.ClearFilterAsync(1);

        Assert.False(read.Bound);
        Assert.Contains("HONUA_SERVER_BASE_URL", read.Detail!, StringComparison.Ordinal);
        Assert.False(save.Succeeded);
        Assert.Equal("Missing binding", save.State);
        Assert.Contains("HONUA_SERVER_BASE_URL", save.Detail!, StringComparison.Ordinal);
        Assert.False(clear.Succeeded);
        Assert.Equal("Missing binding", clear.State);
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
