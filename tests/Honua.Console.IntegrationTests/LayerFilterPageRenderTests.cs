using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class LayerFilterPageRenderTests
{
    private const string ResourceId = "conn-1-layer-1";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Filter_SavePreservesNewerDraftAndPublishesCountsOnly(bool enabled)
    {
        using var ctx = new BunitContext();
        ctx.AddConsoleNotifications();
        var registry = new ConsoleAgentContextRegistry();
        var filter = new FilterOperation();
        ctx.Services.AddSingleton(registry);
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        ctx.Services.AddSingleton<Microsoft.AspNetCore.Components.NavigationManager>(new MountedNavigationManager("/console/"));
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(FixedServicesDataSource.WithLayer(
            new OperateServiceLayer(1, "Parcels", "polygon", ResourceId, "parcels")));
        ctx.Services.AddSingleton<IConsoleLayerFilterOperation>(filter);
        var page = ctx.Render<OperateLayerFilterPage>(p => p.Add(x => x.ResourceId, ResourceId));
        page.WaitForAssertion(() => page.Find("[data-filter-save]"));
        Assert.Equal(enabled, page.FindAll(".console-page-heading-v4").Count == 1);
        Assert.Equal("/console/operate/layers/conn-1-layer-1", page.Find("[data-layer-detail-link]").GetAttribute("href"));
        if (enabled)
        {
            Assert.Equal(ResourceId, Assert.Single(registry.Current.Selection).ResourceId);
            Assert.Equal("1", Assert.Single(registry.Current.Entities).Id);
        }
        if (enabled) CatalogAdminPreview.Capture("data-layer-filter", page.Markup);
        page.Find("[data-filter-expression]").Change("private_field = 1");
        Assert.Equal(enabled ? 1 : 0, registry.Current.Unsaved.ChangedFieldCount);
        Assert.DoesNotContain("private_field", System.Text.Json.JsonSerializer.Serialize(registry.Current));
        page.Render(p => p.Add(x => x.ResourceId, ResourceId));
        Assert.Equal("private_field = 1", page.Find("[data-filter-expression]").GetAttribute("value"));
        page.Find("[data-filter-save]").Click();
        Assert.Equal(enabled ? 1 : 0, registry.Current.Unsaved.PendingOperationCount);
        page.Find("[data-filter-expression]").Change("private_field = 2");
        await page.InvokeAsync(() => filter.Completion.SetResult(new() { Succeeded = true, State = "Saved" }));
        page.WaitForAssertion(() => Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount));
        Assert.Equal("private_field = 1", filter.SavedExpression);
        Assert.Equal("private_field = 2", page.Find("[data-filter-expression]").GetAttribute("value"));
        Assert.Equal(enabled ? 1 : 0, registry.Current.Unsaved.ChangedFieldCount);
        if (enabled) CatalogAdminPreview.Capture("data-layer-filter-saved", page.Markup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Filter_ClearRequiresConfirmationAndUpdatesDraft(bool enabled)
    {
        var (context, registry, filter) = CreateContext(new ConsoleV4Options { Enabled = enabled });
        using var ctx = context;
        filter.SavedExpression = "status = 'active'";
        var page = ctx.Render<OperateLayerFilterPage>(p => p.Add(x => x.ResourceId, ResourceId));
        page.Find("[data-filter-clear]").Click();
        Assert.Equal(0, filter.ClearCalls);
        page.Find("[data-console-confirm-accept]").Click();
        page.WaitForAssertion(() => Assert.Equal(1, filter.ClearCalls));
        Assert.Equal(string.Empty, page.Find("[data-filter-expression]").GetAttribute("value"));
        Assert.Equal(0, registry.Current.Unsaved.ChangedFieldCount);
        Assert.True(page.Find("[data-filter-clear]").HasAttribute("disabled"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Filter_RejectedSavePreservesDraft(bool enabled)
    {
        var (context, registry, filter) = CreateContext(new ConsoleV4Options { Enabled = enabled });
        using var ctx = context;
        filter.Completion.SetResult(new() { Succeeded = false, State = "Rejected", Detail = "Unknown field." });
        var page = ctx.Render<OperateLayerFilterPage>(p => p.Add(x => x.ResourceId, ResourceId));
        page.Find("[data-filter-expression]").Change("private_field = 1");
        page.Find("[data-filter-save]").Click();
        page.WaitForAssertion(() => Assert.Contains("Unknown field.", page.Find("[data-filter-result]").TextContent));
        Assert.Equal("private_field = 1", page.Find("[data-filter-expression]").GetAttribute("value"));
        Assert.Equal(enabled ? 1 : 0, registry.Current.Unsaved.ChangedFieldCount);
        Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Filter_CompletionFromPreviousLayerCannotReplaceCurrentDraft(bool enabled)
    {
        var (context, registry, filter) = CreateContext(new ConsoleV4Options { Enabled = enabled });
        using var ctx = context;
        var page = ctx.Render<OperateLayerFilterPage>(p => p.Add(x => x.ResourceId, ResourceId));
        page.Find("[data-filter-expression]").Change("private_field = 1");
        page.Find("[data-filter-save]").Click();
        page.Render(p => p.Add(x => x.ResourceId, "roads"));
        page.WaitForAssertion(() => Assert.Contains("Roads", page.Find("h1").TextContent));
        page.Find("[data-filter-expression]").Change("road_class = 2");
        await page.InvokeAsync(() => filter.Completion.SetResult(new() { Succeeded = true, State = "Saved" }));
        page.WaitForAssertion(() => Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount));
        Assert.Equal("road_class = 2", page.Find("[data-filter-expression]").GetAttribute("value"));
        Assert.Empty(page.FindAll("[data-filter-result]"));
        if (enabled) Assert.Equal("roads", Assert.Single(registry.Current.Selection).ResourceId);
    }

    private static (BunitContext Context, ConsoleAgentContextRegistry Registry, FilterOperation Operation) CreateContext(ConsoleV4Options options)
    {
        var ctx = new BunitContext();
        ctx.AddConsoleNotifications();
        var registry = new ConsoleAgentContextRegistry();
        var filter = new FilterOperation();
        ctx.Services.AddSingleton(registry);
        ctx.Services.AddSingleton(options);
        ctx.Services.AddSingleton<Microsoft.AspNetCore.Components.NavigationManager>(new MountedNavigationManager("/console/"));
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new FixedServicesDataSource([
            new OperateServiceDetail("cadastre", "cadastre", "FeatureServer", "Running", "honua-server", [
                new(1, "Parcels", "polygon", ResourceId, "parcels"),
                new(2, "Roads", "line", "roads", "roads")], [], [])]));
        ctx.Services.AddSingleton<IConsoleLayerFilterOperation>(filter);
        return (ctx, registry, filter);
    }

    private sealed class FilterOperation : IConsoleLayerFilterOperation
    {
        public TaskCompletionSource<ConsoleSetLayerFilterResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? SavedExpression { get; set; }
        public int ClearCalls { get; private set; }
        public Task<ConsoleLayerFilter> GetFilterAsync(int layerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConsoleLayerFilter { Bound = true, LayerId = layerId, HasFilter = SavedExpression is not null, Expression = layerId == 1 ? SavedExpression ?? string.Empty : string.Empty });
        public Task<ConsoleSetLayerFilterResult> SaveFilterAsync(int layerId, string expression, string language, CancellationToken cancellationToken = default)
        {
            SavedExpression = expression;
            return Completion.Task;
        }
        public Task<ConsoleSetLayerFilterResult> ClearFilterAsync(int layerId, CancellationToken cancellationToken = default)
        {
            ClearCalls++;
            SavedExpression = null;
            return Task.FromResult(new ConsoleSetLayerFilterResult { Succeeded = true, State = "Cleared" });
        }
    }
}
