using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free coverage for the layer 3D-extrusion / 3D-symbology + lifecycle-status authoring OPERATION and its
/// page (<c>/operate/layers/{id}/extrusion</c>). Asserts the real route/verb/body each read+write issues
/// (GET/PUT /api/v1/admin/metadata/layers/{id}/extrusion|status) over a recording handler behind the real
/// <see cref="HonuaAdminOperateHttpClient"/>, the result mapping, the missing-binding
/// <see cref="UnsupportedConsoleLayer3DOperation"/> (no network call), and that the page renders bound +
/// missing-binding and that the saves issue the right PUT. No mocks of metadata — every assertion is over the
/// wire the operation actually sends, or what a recorded server response maps to.
/// </summary>
public sealed class Layer3DAndLifecycleTests
{
    private const string ResourceId = "conn-1-layer-1";
    private static readonly Uri BaseAddress = new("https://honua.test");

    // ---- Extrusion + 3D symbology ----

    [Fact]
    public async Task GetExtrusion_IssuesGetToExtrusionRoute_AndMapsSections()
    {
        string? path = null;
        HttpMethod? method = null;
        var data = new HonuaAdminLayerExtrusion
        {
            LayerId = 1,
            Extrusion = new HonuaAdminLayerExtrusionSettings
            {
                HeightField = "building_height",
                BaseHeightField = "ground_elevation",
                Unit = "meters",
                DefaultHeight = 12.5,
                MaterialHint = "concrete",
            },
            Symbology3D = new HonuaAdminSymbology3D
            {
                DefaultColor = new HonuaAdminRgbColor { Red = 10, Green = 20, Blue = 30 },
                DefaultOpacity = 0.8,
                Rules =
                [
                    new HonuaAdminSymbology3DRule
                    {
                        Attribute = "status",
                        Comparison = "equals",
                        Value = JsonSerializer.SerializeToElement("active"),
                        Color = new HonuaAdminRgbColor { Red = 1, Green = 2, Blue = 3 },
                        Opacity = 0.5,
                        Visible = true,
                    },
                ],
            },
        };
        var operation = new HonuaServerConsoleLayer3DOperation(CreateClient(request =>
        {
            path = request.RequestUri?.AbsolutePath;
            method = request.Method;
            return Ok(data);
        }));

        var result = await operation.GetExtrusionAsync(1);

        Assert.True(result.Bound);
        Assert.Equal(HttpMethod.Get, method);
        Assert.Equal("/api/v1/admin/metadata/layers/1/extrusion", path);
        Assert.Equal("building_height", result.Extrusion!.HeightField);
        Assert.Equal("meters", result.Extrusion.Unit);
        Assert.Equal(12.5, result.Extrusion.DefaultHeight);
        Assert.Equal(0.8, result.Symbology3D!.DefaultOpacity);
        Assert.Equal(30, result.Symbology3D.DefaultColor!.Blue);
        var rule = Assert.Single(result.Symbology3D.Rules);
        Assert.Equal("status", rule.Attribute);
        Assert.Equal("active", rule.Value);
        Assert.True(rule.Visible);
    }

    [Fact]
    public async Task SetExtrusion_IssuesPutWithBothSections_AndMapsUpdated()
    {
        string? path = null;
        HttpMethod? method = null;
        string? body = null;
        var operation = new HonuaServerConsoleLayer3DOperation(CreateClient(request =>
        {
            path = request.RequestUri?.AbsolutePath;
            method = request.Method;
            body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Ok(new HonuaAdminLayerExtrusion { LayerId = 1 });
        }));

        var result = await operation.SetExtrusionAsync(
            1,
            new ConsoleLayerExtrusionSettings { HeightField = "h", Unit = "feet", DefaultHeight = 9 },
            clearExtrusion: false,
            new ConsoleSymbology3D
            {
                DefaultColor = new ConsoleRgbColor { Red = 255, Green = 128, Blue = 0 },
                DefaultOpacity = 0.6,
                Rules =
                [
                    new ConsoleSymbology3DRule
                    {
                        Attribute = "height",
                        Comparison = "greaterThan",
                        Value = "100",
                        Color = new ConsoleRgbColor { Red = 1, Green = 2, Blue = 3 },
                        Opacity = 0.9,
                        Visible = false,
                    },
                ],
            },
            clearSymbology3D: false);

        Assert.True(result.Succeeded);
        Assert.Equal("Updated", result.State);
        Assert.Equal(HttpMethod.Put, method);
        Assert.Equal("/api/v1/admin/metadata/layers/1/extrusion", path);

        using var doc = JsonDocument.Parse(body!);
        var root = doc.RootElement;
        Assert.Equal("h", root.GetProperty("extrusion").GetProperty("heightField").GetString());
        Assert.Equal("feet", root.GetProperty("extrusion").GetProperty("unit").GetString());
        Assert.False(root.GetProperty("clearExtrusion").GetBoolean());
        var sym = root.GetProperty("symbology3D");
        Assert.Equal(255, sym.GetProperty("defaultColor").GetProperty("red").GetInt32());
        Assert.Equal(0.6, sym.GetProperty("defaultOpacity").GetDouble());
        var wireRule = sym.GetProperty("rules")[0];
        Assert.Equal("height", wireRule.GetProperty("attribute").GetString());
        Assert.Equal("greaterThan", wireRule.GetProperty("comparison").GetString());
        // "100" parses to a JSON number scalar on the wire.
        Assert.Equal(100, wireRule.GetProperty("value").GetDouble());
        Assert.False(wireRule.GetProperty("visible").GetBoolean());
        Assert.False(root.GetProperty("clearSymbology3D").GetBoolean());
    }

    [Fact]
    public async Task SetExtrusion_WithClearFlags_OmitsSections_AndSetsClearFlags()
    {
        string? body = null;
        var operation = new HonuaServerConsoleLayer3DOperation(CreateClient(request =>
        {
            body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Ok(new HonuaAdminLayerExtrusion { LayerId = 1 });
        }));

        await operation.SetExtrusionAsync(
            1,
            extrusion: null,
            clearExtrusion: true,
            symbology3D: null,
            clearSymbology3D: true);

        using var doc = JsonDocument.Parse(body!);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("clearExtrusion").GetBoolean());
        Assert.True(root.GetProperty("clearSymbology3D").GetBoolean());
        // Null sections are omitted on the wire so a clear flag is unambiguous.
        Assert.False(root.TryGetProperty("extrusion", out _));
        Assert.False(root.TryGetProperty("symbology3D", out _));
    }

    // ---- Lifecycle status ----

    [Fact]
    public async Task GetStatus_IssuesGetToStatusRoute_AndMapsFields()
    {
        string? path = null;
        HttpMethod? method = null;
        var operation = new HonuaServerConsoleLayer3DOperation(CreateClient(request =>
        {
            path = request.RequestUri?.AbsolutePath;
            method = request.Method;
            return Ok(new HonuaAdminLayerStatus { LayerId = 1, Lifecycle = "active", State = "ready" });
        }));

        var result = await operation.GetStatusAsync(1);

        Assert.True(result.Bound);
        Assert.Equal(HttpMethod.Get, method);
        Assert.Equal("/api/v1/admin/metadata/layers/1/status", path);
        Assert.Equal("active", result.Lifecycle);
        Assert.Equal("ready", result.State);
    }

    [Fact]
    public async Task SetStatus_IssuesPutWithBody_AndMapsUpdated()
    {
        string? path = null;
        HttpMethod? method = null;
        string? body = null;
        var operation = new HonuaServerConsoleLayer3DOperation(CreateClient(request =>
        {
            path = request.RequestUri?.AbsolutePath;
            method = request.Method;
            body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Ok(new HonuaAdminLayerStatus { LayerId = 1 });
        }));

        var result = await operation.SetStatusAsync(1, "deprecated", "degraded");

        Assert.True(result.Succeeded);
        Assert.Equal(HttpMethod.Put, method);
        Assert.Equal("/api/v1/admin/metadata/layers/1/status", path);

        using var doc = JsonDocument.Parse(body!);
        var root = doc.RootElement;
        Assert.Equal("deprecated", root.GetProperty("lifecycle").GetString());
        Assert.Equal("degraded", root.GetProperty("state").GetString());
    }

    [Fact]
    public async Task SetStatus_WithOnlyLifecycle_OmitsState_SoStateIsLeftUnchanged()
    {
        string? body = null;
        var operation = new HonuaServerConsoleLayer3DOperation(CreateClient(request =>
        {
            body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Ok(new HonuaAdminLayerStatus { LayerId = 1 });
        }));

        await operation.SetStatusAsync(1, "archived", state: null);

        using var doc = JsonDocument.Parse(body!);
        var root = doc.RootElement;
        Assert.Equal("archived", root.GetProperty("lifecycle").GetString());
        // null state is omitted on the wire so the server leaves it unchanged.
        Assert.False(root.TryGetProperty("state", out _));
    }

    [Fact]
    public async Task SetStatus_WhenServerRejects_MapsFailureWithDetail()
    {
        var operation = new HonuaServerConsoleLayer3DOperation(CreateClient(_ =>
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = JsonContent.Create(new { success = false, message = "lifecycle 'nope' is not a valid stage." }),
            }));

        var result = await operation.SetStatusAsync(1, "nope", null);

        Assert.False(result.Succeeded);
        Assert.Contains("not a valid stage", result.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unsupported_NeverCallsNetwork_AndReturnsMissingBinding()
    {
        var operation = new UnsupportedConsoleLayer3DOperation();

        var threeD = await operation.GetExtrusionAsync(1);
        var status = await operation.GetStatusAsync(1);
        var saveExtrusion = await operation.SetExtrusionAsync(1, null, false, null, false);
        var saveStatus = await operation.SetStatusAsync(1, "active", "ready");

        Assert.False(threeD.Bound);
        Assert.False(status.Bound);
        Assert.Contains("HONUA_SERVER_BASE_URL", threeD.Detail!, StringComparison.Ordinal);
        Assert.Contains("HONUA_SERVER_BASE_URL", status.Detail!, StringComparison.Ordinal);
        foreach (var write in new[] { saveExtrusion, saveStatus })
        {
            Assert.False(write.Succeeded);
            Assert.Equal("Missing binding", write.State);
            Assert.Contains("HONUA_SERVER_BASE_URL", write.Detail!, StringComparison.Ordinal);
        }
    }

    // ---- Page render ----

    [Fact]
    public void Page_WhenBound_RendersExtrusionStatusFromGet()
    {
        var fake = new FakeLayer3D
        {
            ThreeD = new ConsoleLayer3D
            {
                Bound = true,
                LayerId = 1,
                Extrusion = new ConsoleLayerExtrusionSettings { HeightField = "building_height", Unit = "meters" },
                Symbology3D = new ConsoleSymbology3D
                {
                    DefaultColor = new ConsoleRgbColor { Red = 10, Green = 20, Blue = 30 },
                    Rules = [new ConsoleSymbology3DRule { Attribute = "status", Comparison = "equals", Value = "1" }],
                },
            },
            Status = new ConsoleLayerStatus { Bound = true, LayerId = 1, Lifecycle = "active", State = "ready" },
        };
        var page = RenderPage(fake);

        page.WaitForAssertion(
            () => Assert.Contains("data-extrusion-save", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("value=\"building_height\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-status-save", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-symbology-rule-row", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Page_SaveExtrusion_IssuesSetExtrusionAndSurfacesResult()
    {
        var fake = new FakeLayer3D
        {
            ThreeD = new ConsoleLayer3D
            {
                Bound = true,
                LayerId = 1,
                Extrusion = new ConsoleLayerExtrusionSettings { HeightField = "h" },
            },
            Status = new ConsoleLayerStatus { Bound = true, LayerId = 1 },
            ExtrusionSaveResult = new ConsoleSetLayerMetadataResult { Succeeded = true, State = "Updated", Detail = "Saved the layer's 3D extrusion / symbology on honua-server." },
        };
        var page = RenderPage(fake);

        page.WaitForAssertion(
            () => Assert.Contains("data-extrusion-save", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        page.Find("[data-extrusion-save]").Click();

        page.WaitForAssertion(
            () => Assert.Contains("data-extrusion-result", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.True(fake.ExtrusionSaved);
        Assert.Contains("3D extrusion", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Page_SaveStatus_IssuesSetStatusAndSurfacesResult()
    {
        var fake = new FakeLayer3D
        {
            ThreeD = new ConsoleLayer3D { Bound = true, LayerId = 1 },
            Status = new ConsoleLayerStatus { Bound = true, LayerId = 1, Lifecycle = "draft" },
            StatusSaveResult = new ConsoleSetLayerMetadataResult { Succeeded = true, State = "Updated", Detail = "Saved the layer's lifecycle status on honua-server." },
        };
        var page = RenderPage(fake);

        page.WaitForAssertion(
            () => Assert.Contains("data-status-save", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        page.Find("[data-status-save]").Click();

        page.WaitForAssertion(
            () => Assert.Contains("data-status-result", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.True(fake.StatusSaved);
        Assert.Contains("lifecycle status", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Page_MergedBuild_RendersMissingBindingThroughRealDi()
    {
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new FakeTransition());
        ctx.Services.AddSingleton<IConsoleLayer3DOperation, UnsupportedConsoleLayer3DOperation>();

        var page = ctx.RenderComponent<OperateLayer3DPage>(p => p.Add(x => x.ResourceId, ResourceId));

        page.WaitForAssertion(
            () => Assert.Contains("data-extrusion-unbound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("data-status-unbound", page.Markup, StringComparison.Ordinal);
        Assert.Contains("HONUA_SERVER_BASE_URL", page.Markup, StringComparison.Ordinal);
    }

    private static IRenderedComponent<OperateLayer3DPage> RenderPage(FakeLayer3D fake)
    {
        var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new FakeTransition());
        ctx.Services.AddSingleton<IConsoleLayer3DOperation>(fake);
        return ctx.RenderComponent<OperateLayer3DPage>(p => p.Add(x => x.ResourceId, ResourceId));
    }

    private static HttpResponseMessage Ok<T>(T data) =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { success = true, data, timestamp = DateTimeOffset.UtcNow }),
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
            _ = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            return Task.FromResult(responder(request));
        }
    }

    private sealed class FakeTransition : IOperateTransitionDataSource
    {
        public Task<OperateTransitionWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new OperateTransitionWorkspace(
                [],
                [],
                [
                    new OperateServiceDetail(
                        "svc", "Service", "FeatureServer", "running", "server",
                        [new OperateServiceLayerProjection(1, "Parcels", "polygon", ResourceId, "parcels")],
                        [],
                        [])
                ],
                [],
                []));

        public Task<OperateConnectionSummary?> FindConnectionAsync(string connectionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateConnectionSummary?>(null);

        public Task<OperateResourceEditPreview?> FindResourceEditAsync(string resourceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateResourceEditPreview?>(null);

        public Task<OperateServiceDetail?> FindServiceAsync(string serviceName, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateServiceDetail?>(null);
    }

    private sealed class FakeLayer3D : IConsoleLayer3DOperation
    {
        public ConsoleLayer3D ThreeD { get; set; } = ConsoleLayer3D.Unbound("test");
        public ConsoleLayerStatus Status { get; set; } = ConsoleLayerStatus.Unbound("test");

        public ConsoleSetLayerMetadataResult? ExtrusionSaveResult { get; set; }
        public ConsoleSetLayerMetadataResult? StatusSaveResult { get; set; }

        public bool ExtrusionSaved { get; private set; }
        public bool StatusSaved { get; private set; }

        public Task<ConsoleLayer3D> GetExtrusionAsync(int layerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ThreeD);

        public Task<ConsoleSetLayerMetadataResult> SetExtrusionAsync(
            int layerId,
            ConsoleLayerExtrusionSettings? extrusion,
            bool clearExtrusion,
            ConsoleSymbology3D? symbology3D,
            bool clearSymbology3D,
            CancellationToken cancellationToken = default)
        {
            ExtrusionSaved = true;
            return Task.FromResult(ExtrusionSaveResult ?? new ConsoleSetLayerMetadataResult { Succeeded = true, State = "Updated" });
        }

        public Task<ConsoleLayerStatus> GetStatusAsync(int layerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Status);

        public Task<ConsoleSetLayerMetadataResult> SetStatusAsync(
            int layerId,
            string? lifecycle,
            string? state,
            CancellationToken cancellationToken = default)
        {
            StatusSaved = true;
            return Task.FromResult(StatusSaveResult ?? new ConsoleSetLayerMetadataResult { Succeeded = true, State = "Updated" });
        }
    }
}
