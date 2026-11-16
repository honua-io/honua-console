using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class ImportPublishHandoffRenderTests
{
    [Fact]
    public async Task ImportedPhysicalTableRequiresConnectionChoiceAndPublishesOnlyWhenSubmitted()
    {
        using var context = CreateContext("unique");
        var page = context.Render<OperatePublishLayerPage>();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-import-publish-notice]")));
        page.Find("[aria-label='Connection']").Change("conn-b");
        page.WaitForAssertion(() => Assert.Equal("conn-b", page.Find("[aria-label='Connection']").GetAttribute("value")));
        Assert.Equal("imports.physical_demo", page.Find("[aria-label='Table']").GetAttribute("value"));
        Assert.Equal("physical_demo", page.Find("[data-workspace-field='serviceName']").GetAttribute("value"));
        Assert.Equal("physical_demo", page.Find("[data-workspace-field='layerName']").GetAttribute("value"));
        Assert.Empty(page.FindAll("[data-import-publish-notice]"));
        var operation = context.Services.GetRequiredService<PublishOperation>();
        Assert.Empty(operation.Commands);

        await page.Find("[data-workspace-publish]").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        var command = Assert.Single(operation.Commands);
        Assert.Equal("conn-b", command.ConnectionId);
        Assert.Equal("imports", command.Schema);
        Assert.Equal("physical_demo", command.Table);
    }

    [Theory]
    [InlineData("unique")]
    [InlineData("ambiguous")]
    [InlineData("missing")]
    [InlineData("unavailable")]
    public void UnresolvedImportRequiresAnExplicitConnectionChoice(string scenario)
    {
        using var context = CreateContext(scenario);
        var page = context.Render<OperatePublishLayerPage>();

        page.WaitForAssertion(() => Assert.Contains("imports.physical_demo", page.Find("[data-import-publish-notice]").TextContent));
        Assert.Equal("", page.Find("[aria-label='Connection']").GetAttribute("value"));
        Assert.Equal("", page.Find("[aria-label='Table']").GetAttribute("value"));
        Assert.Empty(context.Services.GetRequiredService<PublishOperation>().Commands);
    }

    [Fact]
    public void QueryRefreshDoesNotReplaceTheOperatorsNames()
    {
        using var context = CreateContext("unique");
        var page = context.Render<OperatePublishLayerPage>();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-import-publish-notice]")));
        page.Find("[aria-label='Connection']").Change("conn-b");
        page.WaitForAssertion(() => Assert.Equal("conn-b", page.Find("[aria-label='Connection']").GetAttribute("value")));
        page.Find("[data-workspace-field='serviceName']").Input("my-service");
        page.Find("[data-workspace-field='layerName']").Input("my-layer");
        page.Render();

        Assert.Equal("my-service", page.Find("[data-workspace-field='serviceName']").GetAttribute("value"));
        Assert.Equal("my-layer", page.Find("[data-workspace-field='layerName']").GetAttribute("value"));
    }

    [Fact]
    public void DelayedImportDiscoveryPreservesANewerConnectionChoice()
    {
        using var context = CreateContext("delayed");
        var operation = context.Services.GetRequiredService<PublishOperation>();
        var page = context.Render<OperatePublishLayerPage>();
        page.Find("[aria-label='Connection']").Change("conn-b");
        page.Find("[aria-label='Connection']").Change("conn-a");
        var renderCount = page.RenderCount;
        operation.Discovery.SetResult([new() { Schema = "imports", Table = "physical_demo", GeometryType = "POINT" }]);
        page.WaitForState(() => page.RenderCount > renderCount);

        page.WaitForAssertion(() => Assert.Equal("conn-a", page.Find("[aria-label='Connection']").GetAttribute("value")));
        Assert.Equal("", page.Find("[aria-label='Table']").GetAttribute("value"));
        Assert.Empty(operation.Commands);
    }

    [Fact]
    public async Task UnresolvedQueryChangeClearsThePreviousPublishTarget()
    {
        using var context = CreateContext("unique");
        var page = context.Render<OperatePublishLayerPage>();
        page.Find("[aria-label='Connection']").Change("conn-b");
        page.WaitForAssertion(() => Assert.Equal("imports.physical_demo", page.Find("[aria-label='Table']").GetAttribute("value")));
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/operate/publishing/quick?schema=imports&table=missing_import");
        page.Render();

        page.WaitForAssertion(() => Assert.Contains("imports.missing_import", page.Find("[data-import-publish-notice]").TextContent));
        Assert.Equal("", page.Find("[aria-label='Connection']").GetAttribute("value"));
        Assert.Equal("", page.Find("[aria-label='Table']").GetAttribute("value"));
        await page.Find("[data-workspace-publish]").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        Assert.Empty(context.Services.GetRequiredService<PublishOperation>().Commands);
    }

    [Fact]
    public void DelayedQueryChangePreservesANewerTableChoice()
    {
        using var context = CreateContext("ambiguous");
        var page = context.Render<OperatePublishLayerPage>();
        page.Find("[aria-label='Connection']").Change("conn-b");
        page.WaitForAssertion(() => Assert.Equal("imports.physical_demo", page.Find("[aria-label='Table']").GetAttribute("value")));
        var operation = context.Services.GetRequiredService<PublishOperation>();
        operation.DelayDiscovery = true;
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/operate/publishing/quick?schema=imports&table=next_import");
        page.Render();
        page.Find("[aria-label='Connection']").Change("conn-b");
        page.WaitForAssertion(() => Assert.Equal("", page.Find("[aria-label='Table']").GetAttribute("value")));
        page.Find("[aria-label='Connection']").Change("conn-a");
        page.Find("[aria-label='Table']").Change("imports.manual_choice");
        var renderCount = page.RenderCount;
        operation.Discovery.SetResult([new() { Schema = "imports", Table = "next_import", GeometryType = "POINT" }]);
        page.WaitForState(() => page.RenderCount > renderCount);

        Assert.Equal("conn-a", page.Find("[aria-label='Connection']").GetAttribute("value"));
        Assert.Equal("imports.manual_choice", page.Find("[aria-label='Table']").GetAttribute("value"));
        Assert.Empty(operation.Commands);
    }

    private static BunitContext CreateContext(string scenario)
    {
        var context = new BunitContext();
        context.AddConsoleNotifications();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IOperateTransitionDataSource>(new Connections());
        var operation = new PublishOperation(scenario);
        context.Services.AddSingleton(operation);
        context.Services.AddSingleton<IServiceLayerPublishOperation>(operation);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/operate/publishing/quick?schema=imports&table=physical_demo");
        return context;
    }

    private sealed class PublishOperation(string scenario) : IServiceLayerPublishOperation
    {
        public bool DelayDiscovery { get; set; }
        public List<ServiceLayerPublishCommand> Commands { get; } = [];
        public TaskCompletionSource<IReadOnlyList<ServiceLayerPublishTable>> Discovery { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<ServiceLayerPublishTable>> ListTablesAsync(string connectionId, CancellationToken cancellationToken = default)
        {
            if ((scenario == "delayed" || DelayDiscovery) && connectionId == "conn-b") return Discovery.Task;
            if (scenario == "unavailable" && connectionId == "conn-a") throw new HttpRequestException("Unavailable");
            IReadOnlyList<ServiceLayerPublishTable> tables = scenario == "missing" || connectionId == "conn-a" && scenario != "ambiguous"
                ? [] : [new() { Schema = "imports", Table = "physical_demo", GeometryType = "POINT" },
                    new() { Schema = "imports", Table = "manual_choice", GeometryType = "POINT" }];
            return Task.FromResult(tables);
        }

        public Task<ServiceLayerPublishResult> PublishAsync(ServiceLayerPublishCommand command, CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            return Task.FromResult(new ServiceLayerPublishResult { Succeeded = true, State = "Published" });
        }
    }

    private sealed class Connections : IOperateTransitionDataSource
    {
        public Task<OperateTransitionWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new OperateTransitionWorkspace(
                [new("conn-a", "Managed", "postgis", "a.db", "svc", "Connected", "now", null),
                 new("conn-b", "Imported", "postgis", "b.db", "svc", "Connected", "now", null)], [], [], [], []));
        public Task<OperateConnectionSummary?> FindConnectionAsync(string connectionId, CancellationToken cancellationToken = default) => Task.FromResult<OperateConnectionSummary?>(null);
        public Task<OperateResourceEditPreview?> FindResourceEditAsync(string resourceId, CancellationToken cancellationToken = default) => Task.FromResult<OperateResourceEditPreview?>(null);
        public Task<OperateServiceDetail?> FindServiceAsync(string serviceName, CancellationToken cancellationToken = default) => Task.FromResult<OperateServiceDetail?>(null);
    }
}
