using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Layout;
using Honua.Console.Shell.Models;
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
        Assert.Equal("100", tree.FindAll("[role='treeitem']")[1].GetAttribute("aria-setsize"));
        Assert.Contains("500 service names and 5000 layers", tree.Markup);
        tree.Find("[data-tree-more]").Click();
        Assert.Equal(51, tree.FindAll("[role='treeitem']").Count);
        tree.Find("input").Input("service-499");
        tree.WaitForAssertion(() => Assert.Contains("1 service name and 10 layers", tree.Markup));
        Assert.Equal(2, tree.FindAll("[role='treeitem']").Count);
        Assert.Equal("service-499 10 layers", tree.FindAll("[role='treeitem']")[1].GetAttribute("aria-label"));
        Assert.Equal(0, source.ChildReads);
        Assert.Equal("service-499", source.LastQuery.NameQuery);
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
        var layer = tree.Find("[aria-label='Bridges LineString']");
        Assert.Equal("3", layer.GetAttribute("aria-level"));
        Assert.Contains("Bridges LineString", layer.TextContent);
        Assert.Contains("service-000 10", tree.Find("[data-publication-key='service:service-000']").TextContent);
        Assert.False(layer.HasAttribute("aria-expanded"));
        tree.Find("[data-publication-key='service:service-000'] [data-tree-expand]").Click();
        tree.Find("[data-publication-key='service:service-000'] [data-tree-expand]").Click();
        Assert.Equal(1, source.ChildReads);
        tree.Find("input").Input("Bridges");
        tree.WaitForAssertion(() => Assert.Contains("No services match this filter.", tree.Markup));
        Assert.Equal("Bridges", source.LastQuery.NameQuery);
        Assert.Equal(1, source.ChildReads);
        Assert.DoesNotContain("Bridges LineString", tree.Markup);
    }

    [Fact]
    public async Task OrdinaryNavigationRetainsLoadedBranchesWithinTheSameAuthority()
    {
        using var context = Context();
        var source = new NavigationSource();
        var capabilities = new CapabilitySignals();
        context.Services.AddSingleton<IConsoleCapabilityManifest>(capabilities);
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(source);
        var tree = context.Render<ConsolePublicationNavigation>();
        tree.WaitForState(() => tree.FindAll("[role='treeitem']").Count == 26);
        tree.Find("[data-publication-key='service:service-000'] [data-tree-expand]").Click();
        tree.WaitForAssertion(() => Assert.Single(tree.FindAll("[aria-label='Bridges LineString']")));
        context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>()
            .NavigateTo("/operate/services/service-000/settings");
        tree.WaitForAssertion(() => Assert.Equal("true", tree.Find("[data-publication-key='service:service-000']").GetAttribute("aria-selected")));
        Assert.Single(tree.FindAll("[aria-label='Bridges LineString']"));
        Assert.Equal(1, source.ServiceReads);
        Assert.Equal(1, source.ChildReads);
        tree.Find("input").Input("service-499");
        tree.WaitForAssertion(() => Assert.Contains("1 service name and 10 layers", tree.Markup));
        await tree.InvokeAsync(() => capabilities.RefreshAsync());
        Assert.Equal("service-499", tree.Find("input").GetAttribute("value"));
        Assert.Equal(2, source.ServiceReads);
        Assert.Equal(1, source.ConnectionReads);
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
        Assert.Empty(tree.FindAll("[aria-label='Bridges LineString']"));
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
        Assert.Contains("Service layers require permission.", tree.Markup);
        Assert.DoesNotContain("Node.ReadMessage", tree.Markup);
        source.LayerResult = OperateSectionResult<IReadOnlyList<ConsolePublicationLayer>>.Allowed([]);
        tree.Find("[data-tree-retry]").Click();
        tree.WaitForAssertion(() => Assert.Contains("No layers returned for this service", tree.Markup));
        Assert.Equal(2, source.ChildReads);
    }

    [Fact]
    public void ReportedFoldersExpandWithoutClaimingSelectionOrInventingFolderLinks()
    {
        using var context = Context();
        var source = new NavigationSource
        {
            ServiceSummaries = [new() { ServiceName = "internal/parcels", FolderId = "public", FolderName = "Public", Title = "Parcels" }]
        };
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(source);
        var tree = context.Render<ConsolePublicationNavigation>();
        tree.WaitForState(() => tree.FindAll("[role='treeitem']").Count == 2);
        var folder = tree.Find("[data-publication-key='folder:public']");
        Assert.False(folder.HasAttribute("aria-selected"));
        Assert.Equal("Public 1 service", folder.GetAttribute("aria-label"));
        Assert.Empty(folder.QuerySelectorAll("a"));
        folder.QuerySelector("[data-tree-expand]")!.Click();
        var service = tree.Find("[data-publication-key='service:internal%2Fparcels']");
        Assert.Equal("false", service.GetAttribute("aria-selected"));
        Assert.Equal("/operate/services/internal%2Fparcels/settings", service.QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal(0, source.ChildReads);
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
    public void ShowMoreLoadsTheNextServicePageWhenTheFirstPageIsVisible()
    {
        using var context = Context();
        var source = new NavigationSource();
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(source);
        var tree = context.Render<ConsolePublicationNavigation>();
        tree.WaitForState(() => tree.FindAll("[role='treeitem']").Count == 26);
        for (var click = 0; click < 3; click++) tree.Find("[data-tree-more='environment']").Click();
        Assert.Equal(1, source.ServiceReads);
        Assert.Equal(101, tree.FindAll("[role='treeitem']").Count);
        tree.Find("[data-tree-more='environment']").Click();
        tree.WaitForAssertion(() => Assert.Equal(2, source.ServiceReads));
        Assert.Equal(2, source.LastQuery.Page);
        Assert.Contains("service-100", tree.Markup);
    }

    [Fact]
    public void FailedSearchShowsTheFailureAndRetryRestoresTheCatalog()
    {
        using var context = Context();
        var source = new NavigationSource();
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(source);
        var tree = context.Render<ConsolePublicationNavigation>();
        tree.WaitForState(() => tree.FindAll("[role='treeitem']").Count == 26);
        source.FailNext = OperateSectionResult<HonuaAdminServicePage>.Denied(
            OperateSectionStatus.Forbidden, "Service navigation: Forbidden.");
        tree.Find("input").Input("secret");
        tree.WaitForAssertion(() => Assert.Contains("Forbidden", tree.Markup));
        Assert.Empty(tree.FindAll("[role='tree']"));
        Assert.False(tree.Find("input").HasAttribute("disabled"));
        source.FailNext = null;
        tree.Find("[data-tree-retry]").Click();
        tree.WaitForAssertion(() => Assert.Contains("No services match this filter", tree.Markup));
        Assert.Equal(HonuaAdminCatalogQuery.First with { NameQuery = "secret" }, source.LastQuery);
    }

    [Fact]
    public async Task AuthorityChangeRejectsDelayedChildrenFromThePreviousOperator()
    {
        using var context = new BunitContext();
        var authorization = context.AddAuthorization();
        authorization.SetAuthorized("first-synthetic-operator");
        var source = DelayedSource();
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
        Assert.Contains("500 service names and 5000 layers", tree.Markup);
    }

    [Fact]
    public void NavigatingToAFilteredHighIndexServicePreservesItsSelectedRow()
    {
        using var context = Context();
        var source = new NavigationSource();
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(source);
        var tree = context.Render<ConsolePublicationNavigation>();
        tree.WaitForState(() => tree.FindAll("[role='treeitem']").Count == 26);
        tree.Find("input").Input("service-499");
        context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>()
            .NavigateTo("/operate/services/service-499/settings");
        tree.WaitForAssertion(() => Assert.Equal("true", tree.Find("[data-publication-key='service:service-499']").GetAttribute("aria-selected")));
        Assert.Equal(2, tree.FindAll("[role='treeitem']").Count);
        Assert.Equal("1", tree.Find("[data-publication-key='service:service-499']").GetAttribute("aria-posinset"));
        Assert.Equal(0, source.ConnectionReads);
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("server")]
    [InlineData("account")]
    public async Task SamePageAuthorityChangeClearsConnectionsAndRejectsPreviousChildren(string changedField)
    {
        using var context = Context();
        var profile = new ConsoleEnvironmentProfile { Id = "first", ServerBaseUri = new Uri("https://first.example.test") };
        var profiles = new InMemoryConsoleEnvironmentProfileStore([profile]);
        var capabilities = new CapabilitySignals();
        context.Services.AddSingleton<IConsoleEnvironmentProfileStore>(profiles);
        context.Services.AddSingleton<IConsoleCapabilityManifest>(capabilities);
        var source = DelayedSource();
        var oldLayers = source.PendingLayers!;
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(source);
        var tree = context.Render<ConsolePublicationNavigation>();
        tree.WaitForState(() => tree.FindAll("[role='treeitem']").Count == 26);
        var expansion = tree.Find("[data-publication-key='service:service-000'] [data-tree-expand]")
            .ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        tree.WaitForAssertion(() => Assert.Equal(1, source.ChildReads));
        await tree.InvokeAsync(() => capabilities.RefreshAsync());
        Assert.Equal(1, source.ServiceReads);
        Assert.Equal(1, source.ConnectionReads);
        var changed = ChangedProfile(profile, changedField);
        await profiles.UpsertProfileAsync(changed);
        await profiles.ActivateProfileAsync(changed.Id);
        source.ConnectionKeys = ["connection-two"];
        source.PendingLayers = null;
        source.LayerResult = OperateSectionResult<IReadOnlyList<ConsolePublicationLayer>>.Allowed([new("connection-two-layer-7", "Current layer", "Point")]);
        await tree.InvokeAsync(() => capabilities.RefreshAsync());
        tree.WaitForState(() => source.ServiceReads == 2 && tree.FindAll("[role='treeitem']").Count == 26);
        tree.Find("[data-publication-key='service:service-000'] [data-tree-expand]").Click();
        tree.WaitForAssertion(() => Assert.Single(tree.FindAll("a[href='/operate/layers/connection-two-layer-7']")));
        oldLayers.SetResult(OperateSectionResult<IReadOnlyList<ConsolePublicationLayer>>.Allowed([new("connection-one-layer-7", "Previous layer", "Point")]));
        await expansion;
        Assert.Empty(tree.FindAll("a[href='/operate/layers/connection-one-layer-7']"));
        Assert.Equal(["connection-one"], source.RequestedConnectionSets[0]);
        Assert.Equal(["connection-two"], source.RequestedConnectionSets[1]);
        Assert.Equal(2, source.ConnectionReads);
    }

    private static ConsoleEnvironmentProfile ChangedProfile(ConsoleEnvironmentProfile profile, string field) => field switch
    {
        "profile" => profile with { Id = "second" },
        "server" => profile with { ServerBaseUri = new Uri("https://second.example.test") },
        "account" => profile with { Account = profile.Account with { AccountId = "second-synthetic-account" } },
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };

    private static NavigationSource DelayedSource() => new()
    {
        PendingLayers = new(TaskCreationOptions.RunContinuationsAsynchronously)
    };

    private sealed class CapabilitySignals : IConsoleCapabilityManifest
    {
        public event Action? Changed;
        public bool IsAdvertised(string capabilityKey) => false;
        public Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            Changed?.Invoke();
            return Task.CompletedTask;
        }
    }

    private static BunitContext Context()
    {
        var context = new BunitContext();
        context.AddAuthorization().SetAuthorized("synthetic-operator");
        return context;
    }

    private sealed class NavigationSource : IConsolePublicationNavigationSource
    {
        public IReadOnlyList<HonuaAdminServiceSummary>? ServiceSummaries { get; init; }
        public OperateSectionResult<HonuaAdminServicePage>? FailNext { get; set; }
        public int ServiceReads { get; private set; }
        public HonuaAdminCatalogQuery LastQuery { get; private set; }
        public TaskCompletionSource<OperateSectionResult<IReadOnlyList<ConsolePublicationLayer>>>? PendingLayers { get; set; }
        public IReadOnlyList<string> ConnectionKeys { get; set; } = ["connection-one"];
        public List<IReadOnlyList<string>> RequestedConnectionSets { get; } = [];
        public int ChildReads { get; private set; }
        public int ConnectionReads { get; private set; }
        public List<string> RequestedServices { get; } = [];
        public OperateSectionResult<IReadOnlyList<ConsolePublicationLayer>> LayerResult { get; set; } =
            OperateSectionResult<IReadOnlyList<ConsolePublicationLayer>>.Allowed([new("connection-one-layer-7", "Bridges", "LineString")]);
        public Task<OperateSectionResult<HonuaAdminServicePage>> ReadServicesAsync(
            HonuaAdminCatalogQuery query, CancellationToken cancellationToken)
        {
            ServiceReads++;
            LastQuery = query;
            if (FailNext is { } failed)
            {
                FailNext = null;
                return Task.FromResult(failed);
            }

            var services = ServiceSummaries?.ToArray() ?? Enumerable.Range(0, 500)
                .Select(index => new HonuaAdminServiceSummary { ServiceName = $"service-{index:D3}", LayerCount = 10 })
                .ToArray();
            if (!string.IsNullOrWhiteSpace(query.NameQuery))
            {
                services = services.Where(service =>
                    service.ServiceName?.Contains(query.NameQuery, StringComparison.OrdinalIgnoreCase) == true
                    || service.Title?.Contains(query.NameQuery, StringComparison.OrdinalIgnoreCase) == true
                    || service.PublicName?.Contains(query.NameQuery, StringComparison.OrdinalIgnoreCase) == true).ToArray();
            }

            var pageSize = query.PageSize <= 0 ? HonuaAdminCatalogQuery.MaxPageSize : query.PageSize;
            var start = Math.Max(0, query.Page - 1) * pageSize;
            var pageItems = services.Skip(start).Take(pageSize).ToArray();
            return Task.FromResult(OperateSectionResult<HonuaAdminServicePage>.Allowed(new HonuaAdminServicePage
            {
                Items = pageItems,
                Page = query.Page,
                PageSize = pageSize,
                Total = services.Length,
                LayerTotal = services.Sum(service => service.LayerCount)
            }));
        }
        public Task<OperateSectionResult<IReadOnlyList<string>>> ReadConnectionsAsync(CancellationToken cancellationToken)
        {
            ConnectionReads++;
            return Task.FromResult(OperateSectionResult<IReadOnlyList<string>>.Allowed(ConnectionKeys));
        }
        public Task<OperateSectionResult<IReadOnlyList<ConsolePublicationLayer>>> ReadLayersAsync(
            string serviceName, IReadOnlyList<string> connectionKeys, CancellationToken cancellationToken)
        {
            ChildReads++;
            RequestedServices.Add(serviceName);
            RequestedConnectionSets.Add(connectionKeys.ToArray());
            return PendingLayers?.Task ?? Task.FromResult(LayerResult);
        }
    }
}
