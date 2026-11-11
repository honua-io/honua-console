using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class DatasetPageRenderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Datasets_SelectionAndQueryChangesUseCanonicalResource(bool enabled)
    {
        var (context, registry) = CreateContext(new ConsoleV4Options { Enabled = enabled });
        using var ctx = context;
        var navigation = ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/console/operate/data?focus=parcels");
        var page = ctx.Render<OperateDataPage>();
        page.WaitForAssertion(() => Assert.Single(page.FindAll("[data-resource-tree]")));
        Assert.Equal(enabled, page.FindAll(".console-page-heading-v4").Count == 1);
        Assert.Equal("/console/operate/data/new", page.Find("a.console-button").GetAttribute("href"));
        Assert.Contains("parcels", page.Find("[data-layer-preview] h2").TextContent);
        if (enabled)
        {
            Assert.Equal("parcels", Assert.Single(registry.Current.Selection).ResourceId);
            CatalogAdminPreview.Capture("data-datasets-focused", page.Markup);
        }
        await page.InvokeAsync(() => navigation.NavigateTo("/console/operate/data?focus=draft"));
        page.WaitForAssertion(() => Assert.Single(page.FindAll("[data-preview-empty]")));
        if (enabled) Assert.Equal("draft", Assert.Single(registry.Current.Selection).ResourceId);
        page.Find("[data-resource-node='parcels'] .resource-tree__name").Click();
        if (enabled) Assert.Equal("parcels", Assert.Single(registry.Current.Selection).ResourceId);
        Assert.DoesNotContain("private_change", System.Text.Json.JsonSerializer.Serialize(registry.Current));
        Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount);
        if (enabled) CatalogAdminPreview.Capture("data-datasets", page.Markup);
    }

    private static (BunitContext Context, ConsoleAgentContextRegistry Registry) CreateContext(ConsoleV4Options options)
    {
        var ctx = new BunitContext();
        ctx.AddConsoleNotifications();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        var registry = new ConsoleAgentContextRegistry();
        ctx.Services.AddSingleton(registry);
        ctx.Services.AddSingleton(options);
        ctx.Services.AddSingleton<NavigationManager>(new DatasetMountedNavigationManager());
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new DatasetDataSource());
        return (ctx, registry);
    }

    private sealed class DatasetDataSource : IOperateTransitionDataSource
    {
        private static OperateResourceEditPreview Resource(string id) => new(id, id, "file", "private_change", "Review required",
            new OperateBlastRadius([], ["Public parcels"], [], [], [], []),
            [new("Warning", "owner", "Check field values", "Review source")], ["Fields", "Preview"]);
        private readonly OperateTransitionWorkspace _workspace = new([], [Resource("parcels"), Resource("draft")],
            [new("Public parcels", "Public parcels", "FeatureServer", "Running", "managed", [new(1, "Parcels", "Polygon", "parcels", "parcels")], [], [])], [], []);
        public Task<OperateTransitionWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) => Task.FromResult(_workspace);
        public Task<OperateConnectionSummary?> FindConnectionAsync(string connectionId, CancellationToken cancellationToken = default) => Task.FromResult<OperateConnectionSummary?>(null);
        public Task<OperateResourceEditPreview?> FindResourceEditAsync(string resourceId, CancellationToken cancellationToken = default) => Task.FromResult(_workspace.ResourceEdits.FirstOrDefault(x => x.ResourceId == resourceId));
        public Task<OperateServiceDetail?> FindServiceAsync(string serviceName, CancellationToken cancellationToken = default) => Task.FromResult(_workspace.Services.FirstOrDefault(x => x.Name == serviceName));
    }
}

internal sealed class DatasetMountedNavigationManager : NavigationManager
{
    public DatasetMountedNavigationManager() => Initialize("http://localhost/console/", "http://localhost/console/operate/data");
    protected override void NavigateToCore(string uri, bool forceLoad)
    {
        Uri = ToAbsoluteUri(uri).AbsoluteUri;
        NotifyLocationChanged(false);
    }
}
