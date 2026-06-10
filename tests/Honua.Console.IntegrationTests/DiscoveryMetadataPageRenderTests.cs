using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for the discovery-metadata authoring page
/// (<c>/operate/layers/{id}/discovery</c> and <c>/operate/services/{svc}/discovery</c>). Drives the page
/// through fakes (never a mock server). The merged-build Unsupported* source is exercised through real DI to
/// prove the honest missing-binding state, and the bound paths prove the GET load + the save round-trip
/// surfaces the result for both the layer and service targets.
/// </summary>
public sealed class DiscoveryMetadataPageRenderTests
{
    private const string ResourceId = "conn-1-layer-1";

    [Fact]
    public void LayerDiscovery_WhenBound_RendersLoadedFieldsAndLinks()
    {
        var fake = new FakeDiscovery
        {
            Read = new ConsoleDiscoveryMetadata
            {
                Bound = true,
                Title = "Parcels",
                Keywords = new[] { "cadastre", "parcels" },
                License = "CC-BY-4.0",
                Links = new[] { new ConsoleDiscoveryLink { Href = "https://county.example/meta", Rel = "describedby" } },
            },
        };
        var page = RenderLayer(fake);

        page.WaitForAssertion(
            () => Assert.Contains("data-discovery-title", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("value=\"Parcels\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("value=\"CC-BY-4.0\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-discovery-link-row", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-discovery-save", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void LayerDiscovery_EditThenSave_IssuesSaveToLayer()
    {
        var fake = new FakeDiscovery
        {
            Read = new ConsoleDiscoveryMetadata { Bound = true },
            SaveResult = new ConsoleSaveDiscoveryResult { Succeeded = true, State = "Updated", Detail = "Saved discovery metadata on honua-server." },
        };
        var page = RenderLayer(fake);

        page.WaitForAssertion(
            () => Assert.Contains("data-discovery-title", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        page.Find("[data-discovery-title]").Change("New title");
        page.Find("[data-discovery-keywords]").Change("alpha, beta");
        page.Find("[data-discovery-save]").Click();

        page.WaitForAssertion(
            () => Assert.Contains("data-discovery-result", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        Assert.Equal(42, fake.SavedLayerId);
        Assert.Null(fake.SavedServiceName);
        Assert.Equal("New title", fake.SavedMetadata?.Title);
        Assert.Equal(new[] { "alpha", "beta" }, fake.SavedMetadata?.Keywords);
        Assert.Contains("Saved discovery metadata", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ServiceDiscovery_WhenBound_LoadsAndSaveTargetsService()
    {
        var fake = new FakeDiscovery
        {
            Read = new ConsoleDiscoveryMetadata { Bound = true, Title = "Parcels service" },
            SaveResult = new ConsoleSaveDiscoveryResult { Succeeded = true, State = "Updated", Detail = "Saved discovery metadata on honua-server." },
        };
        var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new FakeTransition());
        ctx.Services.AddSingleton<IConsoleDiscoveryMetadataOperation>(fake);
        var page = ctx.Render<OperateDiscoveryMetadataPage>(p => p.Add(x => x.ServiceName, "svc"));

        page.WaitForAssertion(
            () => Assert.Contains("data-discovery-title", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("value=\"Parcels service\"", page.Markup, StringComparison.Ordinal);

        page.Find("[data-discovery-save]").Click();

        page.WaitForAssertion(
            () => Assert.Contains("data-discovery-result", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Equal("svc", fake.SavedServiceName);
    }

    [Fact]
    public void LayerDiscovery_MergedBuildPage_RendersMissingBindingThroughRealDi()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new FakeTransition());
        ctx.Services.AddSingleton<IConsoleDiscoveryMetadataOperation, UnsupportedConsoleDiscoveryMetadataOperation>();

        var page = ctx.Render<OperateDiscoveryMetadataPage>(p => p.Add(x => x.ResourceId, ResourceId));

        page.WaitForAssertion(
            () => Assert.Contains("data-discovery-unbound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("HONUA_SERVER_BASE_URL", page.Markup, StringComparison.Ordinal);
    }

    private static IRenderedComponent<OperateDiscoveryMetadataPage> RenderLayer(FakeDiscovery fake)
    {
        var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new FakeTransition());
        ctx.Services.AddSingleton<IConsoleDiscoveryMetadataOperation>(fake);
        return ctx.Render<OperateDiscoveryMetadataPage>(p => p.Add(x => x.ResourceId, ResourceId));
    }

    private sealed class FakeTransition : IOperateTransitionDataSource
    {
        public Task<OperateTransitionWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new OperateTransitionWorkspace(
                [],
                [],
                [
                    new OperateServiceDetail(
                        "svc", "Parcels service", "FeatureServer", "running", "server",
                        [new OperateServiceLayerProjection(42, "Parcels", "polygon", ResourceId, "parcels")],
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

    private sealed class FakeDiscovery : IConsoleDiscoveryMetadataOperation
    {
        public ConsoleDiscoveryMetadata Read { get; set; } = ConsoleDiscoveryMetadata.Unbound("test");
        public ConsoleSaveDiscoveryResult? SaveResult { get; set; }
        public int? SavedLayerId { get; private set; }
        public string? SavedServiceName { get; private set; }
        public ConsoleDiscoveryMetadata? SavedMetadata { get; private set; }

        public Task<ConsoleDiscoveryMetadata> GetLayerDiscoveryAsync(int layerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Read);

        public Task<ConsoleSaveDiscoveryResult> SaveLayerDiscoveryAsync(int layerId, ConsoleDiscoveryMetadata metadata, CancellationToken cancellationToken = default)
        {
            SavedLayerId = layerId;
            SavedMetadata = metadata;
            return Task.FromResult(SaveResult ?? new ConsoleSaveDiscoveryResult { Succeeded = true, State = "Updated" });
        }

        public Task<ConsoleDiscoveryMetadata> GetServiceDiscoveryAsync(string serviceName, CancellationToken cancellationToken = default) =>
            Task.FromResult(Read);

        public Task<ConsoleSaveDiscoveryResult> SaveServiceDiscoveryAsync(string serviceName, ConsoleDiscoveryMetadata metadata, CancellationToken cancellationToken = default)
        {
            SavedServiceName = serviceName;
            SavedMetadata = metadata;
            return Task.FromResult(SaveResult ?? new ConsoleSaveDiscoveryResult { Succeeded = true, State = "Updated" });
        }
    }
}
