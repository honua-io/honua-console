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
/// Docker-free coverage for the layer 3D-extrusion / lifecycle-status operation and its page.
/// The pinned server does not register those routes, so the live client returns Unsupported and
/// issues no HTTP. The missing-binding operation still names HONUA_SERVER_BASE_URL, and the page
/// tests cover a bound fake plus that missing-binding surface.
/// </summary>
public sealed class Layer3DAndLifecycleTests
{
    private const string ResourceId = "conn-1-layer-1";
    private static readonly Uri BaseAddress = new("https://honua.test");

    [Fact]
    public async Task UnregisteredExtrusionAndStatusRoutes_FailClosedWithoutHttp()
    {
        var calls = 0;
        var operation = new HonuaServerConsoleLayer3DOperation(CreateClient(_ =>
        {
            calls++;
            throw new InvalidOperationException("unregistered route must not send HTTP");
        }));

        var readExtrusion = await operation.GetExtrusionAsync(1);
        var readStatus = await operation.GetStatusAsync(1);
        var writeExtrusion = await operation.SetExtrusionAsync(
            1, new ConsoleLayerExtrusionSettings { HeightField = "h" }, false, null, false);
        var writeStatus = await operation.SetStatusAsync(1, "archived", null);

        Assert.Equal(0, calls);
        Assert.False(readExtrusion.Bound);
        Assert.False(readStatus.Bound);
        Assert.Contains(UnregisteredServerRoutes.Detail, readExtrusion.Detail, StringComparison.Ordinal);
        Assert.Contains(UnregisteredServerRoutes.Detail, readStatus.Detail, StringComparison.Ordinal);
        Assert.False(writeExtrusion.Succeeded);
        Assert.Equal(UnregisteredServerRoutes.State, writeExtrusion.State);
        Assert.False(writeStatus.Succeeded);
        Assert.Equal(UnregisteredServerRoutes.State, writeStatus.State);
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
        using var ctx = new Bunit.BunitContext();
        ctx.AddConsoleNotifications();
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new FakeTransition());
        ctx.Services.AddSingleton<IConsoleLayer3DOperation, UnsupportedConsoleLayer3DOperation>();

        var page = ctx.Render<OperateLayer3DPage>(p => p.Add(x => x.ResourceId, ResourceId));

        page.WaitForAssertion(
            () => Assert.Contains("data-extrusion-unbound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("data-status-unbound", page.Markup, StringComparison.Ordinal);
        Assert.Contains("HONUA_SERVER_BASE_URL", page.Markup, StringComparison.Ordinal);
    }

    private static IRenderedComponent<OperateLayer3DPage> RenderPage(FakeLayer3D fake)
    {
        var ctx = new Bunit.BunitContext();
        ctx.AddConsoleNotifications();
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new FakeTransition());
        ctx.Services.AddSingleton<IConsoleLayer3DOperation>(fake);
        return ctx.Render<OperateLayer3DPage>(p => p.Add(x => x.ResourceId, ResourceId));
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
