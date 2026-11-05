using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Layout;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class ConsolePublicationNavigationRenderTests
{
    [Fact]
    public void RootPagesFiveHundredServicesWithoutLoadingChildrenAndExposesSiblingTotals()
    {
        using var context = Context();
        var source = new NavigationSource();
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(source);
        var tree = context.Render<ConsolePublicationNavigation>();
        tree.WaitForAssertion(() => Assert.Equal(26, tree.FindAll("[role='treeitem']").Count));
        Assert.Equal(0, source.ChildReads);
        Assert.Equal(0, source.ConnectionReads);
        Assert.Equal("500", tree.FindAll("[role='treeitem']")[1].GetAttribute("aria-setsize"));
        tree.Find("[data-tree-more]").Click();
        Assert.Equal(51, tree.FindAll("[role='treeitem']").Count);
        tree.Find("input").Input("service-499");
        Assert.Equal(2, tree.FindAll("[role='treeitem']").Count);
        Assert.Equal("service-499", tree.FindAll("[role='treeitem']")[1].GetAttribute("aria-label"));
        Assert.Equal(0, source.ChildReads);
        Assert.Contains("500 service names and 0 loaded layers", tree.Markup);
    }

    [Fact]
    public void ExpandLoadsOnlyThatServicesLayersAndDoesNotReloadACompletedBranch()
    {
        using var context = Context();
        var source = new NavigationSource();
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(source);
        var tree = context.Render<ConsolePublicationNavigation>();
        tree.WaitForState(() => tree.FindAll("[role='treeitem']").Count == 26);
        tree.Find("[data-publication-key='service:service-000'] [data-tree-expand]").Click();
        tree.WaitForAssertion(() => Assert.Single(tree.FindAll("a[href='/operate/layers/connection-one-layer-7']")));
        Assert.Equal(["service-000"], source.RequestedServices);
        Assert.Equal(1, source.ConnectionReads);
        var layer = tree.Find("[aria-label='Bridges']");
        Assert.Equal("3", layer.GetAttribute("aria-level"));
        Assert.False(layer.HasAttribute("aria-expanded"));
        tree.Find("[data-publication-key='service:service-000'] [data-tree-expand]").Click();
        tree.Find("[data-publication-key='service:service-000'] [data-tree-expand]").Click();
        Assert.Equal(1, source.ChildReads);
        tree.Find("input").Input("Bridges");
        Assert.Equal(3, tree.FindAll("[role='treeitem']").Count);
    }

    [Fact]
    public void RefreshClearsTheTreeSnapshotAndItsConnectionCache()
    {
        using var context = Context();
        var source = new NavigationSource();
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(source);
        var tree = context.Render<ConsolePublicationNavigation>();
        tree.WaitForState(() => tree.FindAll("[role='treeitem']").Count == 26);
        tree.Find("[data-publication-key='service:service-000'] [data-tree-expand]").Click();
        tree.WaitForAssertion(() => Assert.Equal(1, source.ConnectionReads));
        tree.FindAll("button").Single(button => button.TextContent == "Refresh services").Click();
        tree.WaitForAssertion(() => Assert.Equal(2, source.ServiceReads));
        Assert.Empty(tree.FindAll("[aria-label='Bridges']"));
        tree.Find("[data-publication-key='service:service-000'] [data-tree-expand]").Click();
        tree.WaitForAssertion(() => Assert.Equal(2, source.ConnectionReads));
        Assert.Equal(2, source.ChildReads);
    }

    [Fact]
    public void ForbiddenChildrenShowPermissionStateAndRetryWithoutClaimingEmpty()
    {
        using var context = Context();
        var source = new NavigationSource
        {
            LayerResult = OperateSectionResult<IReadOnlyList<ConsolePublicationLayer>>
            .Denied(OperateSectionStatus.Forbidden, "Service layers require permission.")
        };
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(source);
        var tree = context.Render<ConsolePublicationNavigation>();
        tree.WaitForState(() => tree.FindAll("[role='treeitem']").Count == 26);
        tree.Find("[data-publication-key='service:service-000'] [data-tree-expand]").Click();
        tree.WaitForAssertion(() => Assert.Contains("Permission required", tree.Markup));
        Assert.DoesNotContain("No layers returned", tree.Markup);
        source.LayerResult = OperateSectionResult<IReadOnlyList<ConsolePublicationLayer>>.Allowed([]);
        tree.Find("[data-tree-retry]").Click();
        tree.WaitForAssertion(() => Assert.Contains("No layers returned for this service", tree.Markup));
        Assert.Equal(2, source.ChildReads);
    }

    [Fact]
    public void MissingBindingDoesNotSeedATreeOrEnableAnInertFilter()
    {
        using var context = Context();
        var tree = context.Render<ConsolePublicationNavigation>();
        tree.WaitForAssertion(() => Assert.Contains("Connect an environment", tree.Markup));
        Assert.Empty(tree.FindAll("[role='tree']"));
        Assert.True(tree.Find("input").HasAttribute("disabled"));
        Assert.Equal("/environments/new", tree.Find("a").GetAttribute("href"));
    }

    [Fact]
    public async Task AuthorityChangeRejectsDelayedChildrenFromThePreviousOperator()
    {
        using var context = new BunitContext();
        var authorization = context.AddAuthorization();
        authorization.SetAuthorized("first-synthetic-operator");
        var source = new NavigationSource { PendingLayers = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(source);
        var tree = context.Render<ConsolePublicationNavigation>();
        tree.WaitForState(() => tree.FindAll("[role='treeitem']").Count == 26);
        var expansion = tree.Find("[data-publication-key='service:service-000'] [data-tree-expand]")
            .ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        tree.WaitForAssertion(() => Assert.Equal(1, source.ChildReads));
        authorization.SetAuthorized("second-synthetic-operator");
        tree.WaitForState(() => source.ServiceReads == 2);
        source.PendingLayers.SetResult(source.LayerResult);
        await expansion;
        tree.WaitForState(() => tree.FindAll("[role='treeitem']").Count == 26);
        Assert.Empty(tree.FindAll("a[href='/operate/layers/connection-one-layer-7']"));
        Assert.Contains("500 service names and 0 loaded layers", tree.Markup);
    }

    private static BunitContext Context()
    {
        var context = new BunitContext();
        context.AddAuthorization().SetAuthorized("synthetic-operator");
        return context;
    }

    private sealed class NavigationSource : IConsolePublicationNavigationSource
    {
        public int ServiceReads { get; private set; }
        public TaskCompletionSource<OperateSectionResult<IReadOnlyList<ConsolePublicationLayer>>>? PendingLayers { get; init; }
        public int ChildReads { get; private set; }
        public int ConnectionReads { get; private set; }
        public List<string> RequestedServices { get; } = [];
        public OperateSectionResult<IReadOnlyList<ConsolePublicationLayer>> LayerResult { get; set; } =
            OperateSectionResult<IReadOnlyList<ConsolePublicationLayer>>.Allowed([new("connection-one-layer-7", "Bridges", "LineString")]);
        public Task<OperateSectionResult<IReadOnlyList<HonuaAdminServiceSummary>>> ReadServicesAsync(CancellationToken cancellationToken)
        {
            ServiceReads++;
            return Task.FromResult(OperateSectionResult<IReadOnlyList<HonuaAdminServiceSummary>>.Allowed(Enumerable.Range(0, 500)
                .Select(index => new HonuaAdminServiceSummary { ServiceName = $"service-{index:D3}", LayerCount = 10 }).ToArray()));
        }
        public Task<OperateSectionResult<IReadOnlyList<Guid>>> ReadConnectionsAsync(CancellationToken cancellationToken)
        {
            ConnectionReads++;
            return Task.FromResult(OperateSectionResult<IReadOnlyList<Guid>>.Allowed([Guid.Empty]));
        }
        public Task<OperateSectionResult<IReadOnlyList<ConsolePublicationLayer>>> ReadLayersAsync(
            string serviceName, IReadOnlyList<Guid> connectionIds, CancellationToken cancellationToken)
        {
            ChildReads++;
            RequestedServices.Add(serviceName);
            return PendingLayers?.Task ?? Task.FromResult(LayerResult);
        }
    }
}
