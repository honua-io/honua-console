using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;
using Honua.Sdk.Admin.Models;

namespace Honua.Console.IntegrationTests;

public sealed class OperateServicesPageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ServicesPage_WithServices_RendersListAndEscapedSettingsLinks(bool enabled)
    {
        using var context = CreateContext(enabled);
        OperateServiceDetail service = new("roads/public", "Public roads", "WMS, WFS", "Ready", "", [], [], []);
        context.Services.AddSingleton<IOperateTransitionDataSource>(
            new FixedServicesDataSource(service));

        var page = context.Render<OperateServicesPage>();

        var registry = context.Services.GetRequiredService<ConsoleAgentContextRegistry>();
        Assert.Equal(enabled ? "/operate/services" : "/", registry.Current.Route);
        Assert.Empty(registry.Current.Entities);
        Assert.Empty(registry.Current.Selection);
        var serviceTarget = new ConsoleAgentEntity { Kind = ConsoleAgentEntityKind.Service, Id = service.Name, ServiceName = service.Name };
        Assert.Single(page.FindAll($"a[href='{ConsoleAgentRoutes.ForEntity(serviceTarget)}']"));
        Assert.Equal("Services", page.Find("h1").TextContent);
        Assert.Equal(enabled ? 1 : 0, page.FindAll(".console-page-heading-v4").Count);
        Assert.Equal(enabled ? 1 : 0, page.FindAll(".console-panel[aria-labelledby='services-list-heading']").Count);
        Assert.Equal(enabled ? 0 : 1, page.FindAll(".console-page > .console-page-heading .console-kicker").Count);
        Assert.Equal("Public roads", page.Find("a[href='/operate/services/roads%2Fpublic/settings']").TextContent);
        Assert.Equal(enabled ? "roads/public" : null, page.FindAll(".console-name-identifier-id").SingleOrDefault()?.TextContent);
        Assert.Equal(enabled ? ["WMS", "WFS"] : [], page.FindAll(".console-protocol-list li").Select(item => item.TextContent));
        Assert.Equal(enabled ? "Service inventory" : "Services", page.Find(".console-table-wrap").GetAttribute("aria-label"));
        Assert.Equal("0", page.FindAll("tbody td")[2].TextContent);
        Assert.Equal("Ready", page.FindAll("tbody td")[3].TextContent);
        Assert.Empty(page.FindAll("[data-resource-tree], [data-connection-notice]"));
        if (enabled) CatalogAdminPreview.Capture("data-services", page.Markup);
        else Assert.Equal(["Public roads", "WMS, WFS", "0", "Ready"], page.FindAll("tbody td").Select(cell => cell.TextContent.Trim()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ServicesPage_WithoutEnvironment_ShowsConnectionNoticeInsteadOfEmptyList(bool enabled)
    {
        using var context = CreateContext(enabled);
        context.Services.AddSingleton<IOperateTransitionDataSource>(new UnsupportedOperateTransitionDataSource());

        var page = context.Render<OperateServicesPage>();

        Assert.Equal("DIV", page.Find("[data-connection-notice][role=alert]").TagName);
        Assert.Equal("Connect environment", page.Find("[data-connection-notice] a").TextContent);
        Assert.Empty(page.FindAll("table, .console-state-empty"));
        if (enabled) CatalogAdminPreview.Capture("data-services-unbound", page.Markup);
    }

    [Theory]
    [InlineData(false, "Connections", 0)]
    [InlineData(true, "Connections", 0)]
    [InlineData(false, "Layers", 0)]
    [InlineData(true, "Layers", 0)]
    [InlineData(false, "Layers", 1)]
    [InlineData(true, "Layers", 1)]
    public void ServicesPage_IncompleteLayerRead_DoesNotClaimATotal(bool enabled, string surface, int visibleLayers)
    {
        using var context = CreateContext(enabled);
        var layers = Enumerable.Range(0, visibleLayers)
            .Select(index => new OperateServiceLayerProjection(index, "Roads", "LineString", "roads", "Roads")).ToArray();
        OperateServiceDetail service = new("roads", "Roads", "WFS", "Ready", "", layers, [], []);
        var states = new[] { new OperateCapabilityState(surface, "Forbidden", "admin", "Layer enumeration is incomplete.") };
        context.Services.AddSingleton<IOperateTransitionDataSource>(new ServicesDataSource(() =>
            Task.FromResult(new OperateServicesView([service], states))));

        var page = context.Render<OperateServicesPage>();

        Assert.Equal("Unknown", page.FindAll("tbody td")[2].TextContent);
        Assert.Single(page.FindAll("[data-connection-notice]"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ServicesPage_WhileLoading_DoesNotClaimTheListIsEmpty(bool enabled)
    {
        using var context = CreateContext(enabled);
        var response = new TaskCompletionSource<OperateServicesView>();
        context.Services.AddSingleton<IOperateTransitionDataSource>(new ServicesDataSource(() => response.Task));

        var page = context.Render<OperateServicesPage>();

        Assert.Equal("Reading services…", page.Find("[role='status']").TextContent);
        Assert.Empty(page.FindAll(".console-state-empty"));
        response.SetResult(new OperateServicesView([], []));
        page.WaitForAssertion(() =>
        {
            Assert.Empty(page.FindAll("[role='status']"));
            Assert.Contains("services", page.Find(".console-state-empty").TextContent, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ServicesPage_AfterReadFailure_RetriesWithoutShowingExceptionDetails(bool enabled)
    {
        using var context = CreateContext(enabled);
        var requests = 0;
        context.Services.AddSingleton<IOperateTransitionDataSource>(new ServicesDataSource(() =>
            ++requests == 1
                ? Task.FromException<OperateServicesView>(new HttpRequestException("private diagnostic"))
                : Task.FromResult(new OperateServicesView([], []))));
        var page = context.Render<OperateServicesPage>();

        Assert.Contains("Could not read services", page.Find("[data-connection-notice]").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("private diagnostic", page.Markup, StringComparison.Ordinal);
        Assert.Empty(page.FindAll(".console-state-empty"));
        page.Find("[data-connection-notice] button").Click();

        Assert.Equal(2, requests);
        Assert.Empty(page.FindAll("[data-connection-notice]"));
        Assert.Single(page.FindAll(".console-state-empty"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ServicesPage_PublishesFolderSelectionAndDirtyCountsOnlyWhenEnabled(bool enabled)
    {
        using var context = CreateContext(enabled);
        context.Services.AddSingleton<IConsoleFolderClient>(new FolderClient());
        context.Services.AddSingleton<IOperateTransitionDataSource>(new FixedServicesDataSource());
        var registry = context.Services.GetRequiredService<ConsoleAgentContextRegistry>();
        var page = context.Render<OperateServicesPage>();
        page.Find("[data-folder-select]").Change("transport");
        page.Find("[data-folder-title]").Change("Private title");
        Assert.Equal(enabled ? "/operate/services" : "/", registry.Current.Route);
        Assert.Equal(enabled ? 1 : 0, registry.Current.Unsaved.ChangedFieldCount);
        if (enabled)
        {
            var selected = Assert.Single(registry.Current.Selection);
            Assert.Equal(ConsoleAgentEntityKind.Folder, selected.Kind);
            Assert.Equal("transport", selected.Id);
            Assert.Equal("/operate/folders/transport", ConsoleAgentRoutes.ForEntity(selected));
        }
        else Assert.Empty(registry.Current.Selection);
        Assert.DoesNotContain("Private title", System.Text.Json.JsonSerializer.Serialize(registry.Current));
        page.Find("[data-folder-save]").Click();
        Assert.Equal(0, registry.Current.Unsaved.ChangedFieldCount);
        Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount);
        Assert.Empty(registry.Current.Selection);
        page.Find("[data-folder-select]").Change("transport");
        page.Find("[data-folder-title]").Change("Unsaved before disposal");
        Assert.Equal(enabled ? 1 : 0, registry.Current.Unsaved.ChangedFieldCount);
        Assert.Equal(enabled ? 1 : 0, registry.Current.Selection.Count);
        page.Dispose();
        Assert.Equal(enabled ? "/operate/services" : "/", registry.Current.Route);
        Assert.Empty(registry.Current.Entities);
        Assert.Empty(registry.Current.Selection);
        Assert.Equal(0, registry.Current.Unsaved.ChangedFieldCount);
    }

    private sealed class FolderClient : IConsoleFolderClient
    {
        public Task<OperateSectionResult<ConsoleFolderCatalog>> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<ConsoleFolderCatalog>.Allowed(new(
                [new Folder { Metadata = new() { Id = "transport", Name = "Transport", Title = "Transport data" } }],
                "\"catalog-1\"", "authority")));
        public Task<ConsoleFolderMutationResult> CreateAsync(ConsoleFolderCatalog catalog, CreateFolderRequest request, CancellationToken cancellationToken = default) => Saved();
        public Task<ConsoleFolderMutationResult> UpdateAsync(ConsoleFolderCatalog catalog, string folderId, UpdateFolderRequest request, CancellationToken cancellationToken = default) => Saved();
        public Task<ConsoleFolderMutationResult> DeleteAsync(ConsoleFolderCatalog catalog, string folderId, CancellationToken cancellationToken = default) => Saved();
        public Task<ConsoleFolderMutationResult> MoveAsync(ConsoleFolderCatalog catalog, string serviceName, MoveServiceRequest request, CancellationToken cancellationToken = default) => Saved();
        private static Task<ConsoleFolderMutationResult> Saved() => Task.FromResult(new ConsoleFolderMutationResult { Succeeded = true, State = "Saved" });
    }

    private static BunitContext CreateContext(bool enabled)
    {
        var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        context.Services.AddSingleton<ConsoleAgentContextRegistry>();
        return context;
    }

    private sealed class ServicesDataSource(Func<Task<OperateServicesView>> load) : IOperateTransitionDataSource
    {
        public Task<OperateServicesView> GetServicesViewAsync(CancellationToken cancellationToken = default) => load();

        public Task<OperateTransitionWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OperateConnectionSummary?> FindConnectionAsync(string connectionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OperateResourceEditPreview?> FindResourceEditAsync(string datasetId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OperateServiceDetail?> FindServiceAsync(string serviceName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
