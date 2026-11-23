using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

// A detail page shows one layer or dataset, so it must ask for that item by id. Reading the whole
// list reads every connection's layers, which is slow on large servers and trips the admin rate limiter.
public sealed class ScopedDetailReadRenderTests
{
    private const string ResourceId = "conn-1-layer-1";

    [Fact]
    public void FilterPage_ReadsOnlyItsLayer() => AssertReadsOnlyItsLayer<OperateLayerFilterPage>();

    [Fact]
    public void RelationshipsPage_ReadsOnlyItsLayer() => AssertReadsOnlyItsLayer<OperateLayerRelationshipsPage>();

    [Fact]
    public void EditingPolicyPage_ReadsOnlyItsLayer() => AssertReadsOnlyItsLayer<OperateLayerMetadataPage>();

    [Fact]
    public void DatasetPage_ReadsOnlyItsDataset()
    {
        var data = new ScopedOnlyDataSource(new DatasetDataSource());
        using var ctx = CreateContext(data);

        var page = ctx.Render<OperateResourceDetailPage>(p => p.Add(x => x.ResourceId, "parcels"));

        page.WaitForAssertion(() => Assert.Equal("parcels", page.Find("h1").TextContent));
        Assert.Equal(["parcels"], data.ScopedReads);
    }

    private static void AssertReadsOnlyItsLayer<TPage>()
        where TPage : IComponent
    {
        var data = new ScopedOnlyDataSource(FixedServicesDataSource.WithLayer(
            new OperateServiceLayer(1, "Parcels", "polygon", ResourceId, "parcels")));
        using var ctx = CreateContext(data);

        var page = ctx.Render(builder =>
        {
            builder.OpenComponent<TPage>(0);
            builder.AddAttribute(1, nameof(OperateLayerFilterPage.ResourceId), ResourceId);
            builder.CloseComponent();
        });

        page.WaitForAssertion(() => Assert.Contains("Parcels", page.Markup, StringComparison.Ordinal));
        Assert.Equal([ResourceId], data.ScopedReads);
    }

    private static BunitContext CreateContext(IOperateTransitionDataSource data)
    {
        var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.AddConsoleNotifications();
        ctx.Services.AddSingleton(new ConsoleAgentContextRegistry());
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        ctx.Services.AddSingleton<NavigationManager>(new MountedNavigationManager("/console/"));
        ctx.Services.AddSingleton(data);
        ctx.Services.AddSingleton<IConsoleLayerFilterOperation, UnsupportedConsoleLayerFilterOperation>();
        ctx.Services.AddSingleton<IConsoleLayerRelationshipsOperation, UnsupportedConsoleLayerRelationshipsOperation>();
        ctx.Services.AddSingleton<IConsoleLayerMetadataOperation, UnsupportedConsoleLayerMetadataOperation>();
        ctx.Services.AddSingleton<IConsoleLayerFieldsOperation, UnsupportedConsoleLayerFieldsOperation>();
        return ctx;
    }

    // Serves only the by-id reads; any whole-list read fails the render.
    private sealed class ScopedOnlyDataSource(IOperateTransitionDataSource inner) : IOperateTransitionDataSource
    {
        public List<string> ScopedReads { get; } = [];

        public Task<OperateTransitionWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("A detail page read the whole workspace.");

        public Task<OperateServicesView> GetLayerViewAsync(string resourceId, CancellationToken cancellationToken = default)
        {
            ScopedReads.Add(resourceId);
            return inner.GetLayersViewAsync(cancellationToken);
        }

        public Task<OperateResourcesView> GetResourceViewAsync(string resourceId, CancellationToken cancellationToken = default)
        {
            ScopedReads.Add(resourceId);
            return inner.GetResourcesViewAsync(cancellationToken);
        }

        public Task<OperateConnectionSummary?> FindConnectionAsync(string connectionId, CancellationToken cancellationToken = default) =>
            inner.FindConnectionAsync(connectionId, cancellationToken);

        public Task<OperateResourceEditPreview?> FindResourceEditAsync(string resourceId, CancellationToken cancellationToken = default) =>
            inner.FindResourceEditAsync(resourceId, cancellationToken);

        public Task<OperateServiceDetail?> FindServiceAsync(string serviceName, CancellationToken cancellationToken = default) =>
            inner.FindServiceAsync(serviceName, cancellationToken);
    }
}
