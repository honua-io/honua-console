using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components;

namespace Honua.Console.IntegrationTests;

public sealed class OperateConnectionV4RenderTests
{
    private static readonly OperateConnectionSummary Connection = new("connection a", "Production database", "PostGIS", "db.example:5432 / spatial", "reader", "Healthy", "Today", null);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InventoryUsesStableEncodedConnectionLinks(bool enabled)
    {
        using var context = Context(enabled);
        var registry = context.Services.GetRequiredService<ConsoleAgentContextRegistry>();
        var page = context.Render<OperateConnectionsPage>();
        var link = page.Find("tbody a");
        Assert.Equal("/operate/connections/connection%20a", link.GetAttribute("href"));
        Assert.Equal("Production database", link.TextContent);
        Assert.Equal(enabled ? 1 : 0, page.FindAll("section.console-panel[aria-labelledby]").Count);
        Assert.Equal(enabled ? "/operate/connections" : "/", registry.Current.Route);
        if (enabled)
        {
            var entity = Assert.Single(registry.Current.Entities);
            Assert.Equal(ConsoleAgentEntityKind.Connection, entity.Kind);
            Assert.Equal(Connection.Id, entity.Id);
            Assert.Equal(link.GetAttribute("href"), ConsoleAgentRoutes.ForEntity(entity));
            CatalogAdminPreview.Capture("data-connections", page.Markup);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DetailDeepLinkSelectsStableEntityAndRetainsTesting(bool enabled)
    {
        using var context = Context(enabled);
        var registry = context.Services.GetRequiredService<ConsoleAgentContextRegistry>();
        var page = context.Render<OperateConnectionDetailPage>(parameters => parameters.Add(p => p.ConnectionId, Connection.Id));
        Assert.Equal(Connection.Name, page.Find("h1").TextContent);
        Assert.Equal(enabled ? 2 : 0, page.FindAll("section.console-panel[aria-labelledby]").Count);
        if (enabled)
        {
            var entity = Assert.Single(registry.Current.Entities);
            Assert.Equal(Connection.Id, entity.Id);
            Assert.Equal("/operate/connections/connection%20a", registry.Current.Route);
            Assert.Equal(registry.Current.Route, ConsoleAgentRoutes.ForEntity(entity));
            CatalogAdminPreview.Capture("data-connection-detail", page.Markup);
        }
        page.Find("button").Click();
        page.WaitForAssertion(() => Assert.Contains("Connection test", page.Markup));
        Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount);
    }

    [Fact]
    public void DiagnosticsContextPreservesDeepLinkRoute()
    {
        using var context = Context(true);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/operate/connections/connection%20a/diagnostics");
        context.Render<OperateConnectionDetailPage>(parameters => parameters.Add(p => p.ConnectionId, Connection.Id));
        Assert.Equal("/operate/connections/connection%20a/diagnostics", context.Services.GetRequiredService<ConsoleAgentContextRegistry>().Current.Route);
    }

    private static BunitContext Context(bool enabled)
    {
        var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        context.Services.AddSingleton<ConsoleAgentContextRegistry>();
        context.Services.AddSingleton<IOperateTransitionDataSource>(new Source());
        context.Services.AddSingleton<IConsoleConnectionCreateOperation, UnsupportedConsoleConnectionCreateOperation>();
        return context;
    }

    private sealed class Source : IOperateTransitionDataSource
    {
        public Task<OperateTransitionWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new OperateTransitionWorkspace([Connection], [], [], [], []));
        public Task<OperateConnectionSummary?> FindConnectionAsync(string connectionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(connectionId == Connection.Id ? Connection : null);
        public Task<OperateResourceEditPreview?> FindResourceEditAsync(string resourceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateResourceEditPreview?>(null);
        public Task<OperateServiceDetail?> FindServiceAsync(string serviceName, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateServiceDetail?>(null);
    }
}
