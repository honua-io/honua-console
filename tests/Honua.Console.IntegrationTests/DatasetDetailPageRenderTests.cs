using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class DatasetDetailPageRenderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Detail_ValidationDependenciesAndMountedTabsRemainAvailable(bool enabled)
    {
        var (context, registry) = CreateContext(new ConsoleV4Options { Enabled = enabled });
        using var ctx = context;
        var page = ctx.Render<OperateResourceDetailPage>(p => p.Add(x => x.ResourceId, "parcels"));
        page.WaitForAssertion(() => Assert.Equal("parcels", page.Find("h1").TextContent));
        Assert.Equal(enabled, page.FindAll(".console-page-heading-v4").Count == 1);
        Assert.Equal("/console/operate/resources/parcels#fields", page.Find("nav a").GetAttribute("href"));
        Assert.Contains("Check field values", page.Markup);
        Assert.Contains("Public parcels", page.Markup);
        if (enabled)
        {
            Assert.Equal("parcels", Assert.Single(registry.Current.Entities).ResourceId);
            CatalogAdminPreview.Capture("data-dataset-detail", page.Markup);
        }
        page.Render(p => p.Add(x => x.ResourceId, "missing"));
        page.WaitForAssertion(() => Assert.Contains("Dataset not found", page.Markup, StringComparison.OrdinalIgnoreCase));
        Assert.Empty(registry.Current.Selection);
        if (enabled) CatalogAdminPreview.Capture("data-dataset-missing", page.Markup);
    }

    private static (BunitContext Context, ConsoleAgentContextRegistry Registry) CreateContext(ConsoleV4Options options)
    {
        var ctx = new BunitContext();
        ctx.AddConsoleNotifications();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        var registry = new ConsoleAgentContextRegistry();
        ctx.Services.AddSingleton(registry);
        ctx.Services.AddSingleton(options);
        ctx.Services.AddSingleton<NavigationManager>(new DatasetDetailMountedNavigationManager());
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

internal sealed class DatasetDetailMountedNavigationManager : NavigationManager
{
    public DatasetDetailMountedNavigationManager() => Initialize("http://localhost/console/", "http://localhost/console/operate/data");
    protected override void NavigateToCore(string uri, bool forceLoad)
    {
        Uri = ToAbsoluteUri(uri).AbsoluteUri;
        NotifyLocationChanged(false);
    }
}
