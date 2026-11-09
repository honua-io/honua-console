using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Layout;
using Honua.Console.Shell.Components.Operate;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Honua.Sdk.Admin.Models;
using Microsoft.Extensions.DependencyInjection.Extensions;

using static Honua.Console.IntegrationTests.ConsolePublicationRenderFixture;

namespace Honua.Console.IntegrationTests;

public sealed class ConsolePublicationNavigationRenderTests
{
    [Fact]
    public void RootPagesFiveHundredServicesWithoutLoadingChildrenAndExposesSiblingTotals()
    {
        using var context = Context();
        var source = new PublicationNavigationSource();
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
        var source = new PublicationNavigationSource();
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
        var source = new PublicationNavigationSource();
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
        var source = new PublicationNavigationSource();
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
        var source = new PublicationNavigationSource
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
        var source = new PublicationNavigationSource
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
    public void V4FolderNavigationSelectsTheFolderWithoutReadingItsServices()
    {
        using var context = Context();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        var source = new PublicationNavigationSource
        {
            ServiceSummaries = [new() { ServiceName = "roads", FolderId = "public/transport", FolderName = "Transport" }]
        };
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(source);
        context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>()
            .NavigateTo("/operate/folders/public%2Ftransport");
        var tree = context.Render<ConsolePublicationNavigation>();
        tree.WaitForAssertion(() => Assert.Equal("true", tree.Find("[data-publication-key='folder:public%2Ftransport']").GetAttribute("aria-selected")));
        Assert.Equal("/operate/folders/public%2Ftransport", tree.Find("[data-publication-key='folder:public%2Ftransport'] a").GetAttribute("href"));
        Assert.Equal(0, source.ChildReads);
    }

    [Fact]
    public void V4BreadcrumbLinksTheFolderAndOmitsTheOperateArea()
    {
        var source = new PublicationNavigationSource
        {
            ServiceSummaries = [new() { ServiceName = "roads", FolderId = "public", FolderName = "Public", Title = "Roads" }]
        };
        using var context = PublicationLayoutContext(source);
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        var navigation = context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        navigation.NavigateTo("/operate/services/roads/settings");
        var layout = RenderLayerLayout(context);
        layout.WaitForAssertion(() => Assert.Equal("Public", layout.Find("nav[aria-label='Breadcrumb'] a[href='/operate/folders/public']").TextContent));
        Assert.Equal(["Home", "Public", "Roads"], layout.FindAll("nav[aria-label='Breadcrumb'] ol > li")
            .Select(item => item.QuerySelector("summary")?.TextContent ?? item.TextContent.Trim()));
        navigation.NavigateTo("/operate/folders/public");
        layout.WaitForAssertion(() => Assert.Equal("Public", layout.Find("nav[aria-label='Breadcrumb'] summary").TextContent));
        Assert.Equal(2, layout.FindAll("nav[aria-label='Breadcrumb'] ol > li").Count);
        Assert.Equal(0, source.ChildReads);
    }

    [Fact]
    public void PublicationBreadcrumbFollowsTheAuthorizedFolderServiceAndLayer()
    {
        var source = new PublicationNavigationSource
        {
            ServiceSummaries =
            [
                new() { ServiceName = "internal/parcels", FolderId = "public", FolderName = "Public", Title = "Parcels", LayerCount = 1 },
                new() { ServiceName = "root-parcels", Title = "Root parcels" }
            ]
        };
        using var context = PublicationLayoutContext(source);
        var navigation = context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        navigation.NavigateTo("/operate/services/internal%2Fparcels/settings");
        var layout = RenderLayerLayout(context);
        layout.WaitForAssertion(() => Assert.Contains("Public", layout.Find("nav[aria-label='Breadcrumb']").TextContent));
        Assert.Equal("Parcels", layout.Find("nav[aria-label='Breadcrumb'] summary").TextContent);
        layout.Find("[data-publication-key='service:internal%2Fparcels'] [data-tree-expand]").Click();
        layout.WaitForState(() => layout.FindAll("a[href='/operate/layers/connection-one-layer-7']").Count == 1);
        navigation.NavigateTo("/operate/layers/connection-one-layer-7");
        layout.WaitForAssertion(() => Assert.Equal("Bridges", layout.Find("nav[aria-label='Breadcrumb'] summary").TextContent));
        var ancestors = layout.FindAll("nav[aria-label='Breadcrumb'] ol > li")
            .Select(item => item.QuerySelector("summary")?.TextContent ?? item.TextContent.Trim()).ToArray();
        Assert.Equal(["Public", "Parcels", "Bridges"], ancestors.TakeLast(3));
        Assert.Equal("Home", ancestors[0]);
        Assert.Equal("/operate/services/internal%2Fparcels/settings",
            layout.Find("nav[aria-label='Breadcrumb'] a[href*='/services/']").GetAttribute("href"));
        navigation.NavigateTo("/operate/services/root-parcels/settings");
        layout.WaitForAssertion(() => Assert.Equal("Root parcels", layout.Find("nav[aria-label='Breadcrumb'] summary").TextContent));
        Assert.DoesNotContain("Public", layout.Find("nav[aria-label='Breadcrumb']").TextContent);
        navigation.NavigateTo("/operate/data");
        layout.WaitForAssertion(() => Assert.DoesNotContain("Public", layout.Find("nav[aria-label='Breadcrumb']").TextContent));
        Assert.DoesNotContain("Bridges", layout.Find("nav[aria-label='Breadcrumb']").TextContent);
    }

    [Theory]
    [InlineData("cold")]
    [InlineData("after-render")]
    [InlineData("later-page")]
    public void ColdLayerNavigationResolvesAuthorizedAncestryWithoutExpandingTheSidebar(string entry)
    {
        var service = new ServiceSummary
        {
            ServiceName = "z-parcels",
            FolderId = "public",
            FolderName = "Public",
            Title = "Parcels",
            LayerCount = 1
        };
        var source = new PublicationNavigationSource
        {
            ServiceSummaries = entry == "later-page"
                ? Enumerable.Range(0, 100).Select(index => new ServiceSummary { ServiceName = $"empty-{index:D3}" }).Append(service).ToArray()
                : [service]
        };
        using var context = PublicationLayoutContext(source);
        var navigation = context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        if (entry != "after-render") navigation.NavigateTo("/operate/layers/connection-one-layer-7/style");
        var layout = RenderLayerLayout(context);
        if (entry == "after-render")
        {
            layout.WaitForAssertion(() => Assert.Single(layout.FindAll("[role='tree']")));
            navigation.NavigateTo("/operate/layers/connection-one-layer-7");
        }
        layout.WaitForAssertion(() => Assert.Equal("Bridges", layout.Find("nav[aria-label='Breadcrumb'] summary").TextContent));
        var ancestors = layout.FindAll("nav[aria-label='Breadcrumb'] ol > li").Select(item => item.TextContent.Trim()).ToArray();
        Assert.Contains("Public", ancestors);
        Assert.Contains("Parcels", ancestors);
        Assert.Equal(1, source.LayerPageReads);
        Assert.Empty(source.RequestedServices);
    }

    [Fact]
    public void ColdLayerNavigationBoundsReadsAcrossFiveHundredNonEmptyServices()
    {
        var source = new PublicationNavigationSource
        {
            ServiceSummaries = Enumerable.Range(0, 500).Select(index => new ServiceSummary
            { ServiceName = $"service-{index:D3}", LayerCount = 10 }).Append(new()
            { ServiceName = "z-parcels", FolderId = "public", FolderName = "Public", Title = "Parcels", LayerCount = 1 }).ToArray()
        };
        using var context = PublicationLayoutContext(source);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/operate/layers/connection-one-layer-7");
        var layout = RenderLayerLayout(context);
        layout.WaitForAssertion(() => Assert.Contains("Public", layout.Find("nav[aria-label='Breadcrumb']").TextContent));
        Assert.True(source.ChildReads <= 1, $"Cold navigation read {source.ChildReads} services.");
        Assert.True(source.ServiceReads <= 2, $"Cold navigation read {source.ServiceReads} catalog pages.");
    }

    [Theory]
    [InlineData("filter")]
    [InlineData("show-more")]
    public void ColdLayerAncestrySurvivesSidebarCatalogChanges(string action)
    {
        var source = new PublicationNavigationSource
        {
            ServiceSummaries = Enumerable.Range(0, 100).Select(index => new ServiceSummary
            { ServiceName = $"empty-{index:D3}" }).Append(new()
            { ServiceName = "z-parcels", FolderId = "public", FolderName = "Public", Title = "Parcels", LayerCount = 1 }).ToArray()
        };
        using var context = PublicationLayoutContext(source);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/operate/layers/connection-one-layer-7");
        var layout = RenderLayerLayout(context);
        layout.WaitForAssertion(() => Assert.Contains("Public", layout.Find("nav[aria-label='Breadcrumb']").TextContent));
        var reads = source.ChildReads;
        var serviceReads = source.ServiceReads;
        if (action == "filter") layout.Find("[data-tree-filter]").Input("no-match");
        else for (var click = 0; click < 4; click++) layout.Find("[data-tree-more='environment']").Click();
        layout.WaitForAssertion(() => Assert.Equal(serviceReads + 1, source.ServiceReads));
        layout.WaitForAssertion(() => Assert.Contains("Public", layout.Find("nav[aria-label='Breadcrumb']").TextContent));
        Assert.Equal(reads, source.ChildReads);
        Assert.Contains("Parcels", layout.Find("nav[aria-label='Breadcrumb']").TextContent);
    }

    [Fact]
    public void ColdLayerNavigationDoesNotExposeAncestryWhenLayersAreForbidden()
    {
        var source = new PublicationNavigationSource
        {
            ServiceSummaries = [new() { ServiceName = "parcels", FolderId = "public", FolderName = "Public", LayerCount = 1 }],
            LayerResult = OperateSectionResult<IReadOnlyList<ConsolePublicationLayer>>.Denied(OperateSectionStatus.Forbidden, "Layers denied.")
        };
        using var context = PublicationLayoutContext(source);
        context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>()
            .NavigateTo("/operate/layers/connection-one-layer-7");
        var layout = RenderLayerLayout(context);
        layout.WaitForAssertion(() => Assert.Equal(1, source.LayerPageReads));
        Assert.DoesNotContain("Public", layout.Find("nav[aria-label='Breadcrumb']").TextContent);
        Assert.DoesNotContain("Parcels", layout.Find("nav[aria-label='Breadcrumb']").TextContent);
    }

    [Fact]
    public async Task ColdLayerNavigationRejectsDelayedAncestryAfterAuthenticationChanges()
    {
        var source = new PublicationNavigationSource
        {
            ServiceSummaries = [new() { ServiceName = "parcels", FolderId = "public", FolderName = "Public", LayerCount = 1 }],
            PendingLayers = new(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        using var context = new BunitContext();
        var authorization = context.AddAuthorization();
        authorization.SetAuthorized("synthetic-operator");
        ConfigurePublicationLayout(context, source);
        context.Services.RemoveAll<IAuthorizationService>();
        context.Services.RemoveAll<IAuthorizationPolicyProvider>();
        context.Services.AddAuthorizationCore();
        context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>()
            .NavigateTo("/operate/layers/connection-one-layer-7");
        var layout = RenderLayerLayout(context);
        layout.WaitForAssertion(() => Assert.Equal(1, source.LayerPageReads));
        authorization.SetNotAuthorized();
        source.PendingLayers.SetResult(source.LayerResult);
        await layout.InvokeAsync(() => Task.CompletedTask);
        layout.WaitForAssertion(() => Assert.Empty(layout.FindAll("[role='tree']")));
        Assert.DoesNotContain("Public", layout.Find("nav[aria-label='Breadcrumb']").TextContent);
    }

    [Fact]
    public void PublicationBreadcrumbDropsFolderWhenTheCatalogBecomesForbidden()
    {
        var source = new PublicationNavigationSource
        {
            ServiceSummaries = [new() { ServiceName = "parcels", FolderId = "public", FolderName = "Public", LayerCount = 1 }]
        };
        using var context = PublicationLayoutContext(source);
        context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>()
            .NavigateTo("/operate/services/parcels/settings");
        var layout = RenderLayerLayout(context);
        layout.WaitForAssertion(() => Assert.Contains("Public", layout.Find("nav[aria-label='Breadcrumb']").TextContent));
        source.FailNext = OperateSectionResult<HonuaAdminServicePage>.Denied(OperateSectionStatus.Forbidden, "Catalog access denied.");
        layout.FindAll("button").Single(button => button.TextContent == "Refresh services").Click();
        layout.WaitForAssertion(() => Assert.DoesNotContain("Public", layout.Find("nav[aria-label='Breadcrumb']").TextContent));
    }

    [Fact]
    public async Task PublicationBreadcrumbDropsFolderWhenAuthenticationIsRevoked()
    {
        var source = new PublicationNavigationSource
        {
            ServiceSummaries = [new() { ServiceName = "parcels", FolderId = "public", FolderName = "Public", LayerCount = 1 }]
        };
        using var context = new BunitContext();
        var authorization = context.AddAuthorization();
        authorization.SetAuthorized("synthetic-operator");
        ConfigurePublicationLayout(context, source);
        context.Services.RemoveAll<IAuthorizationService>();
        context.Services.RemoveAll<IAuthorizationPolicyProvider>();
        context.Services.AddAuthorizationCore();
        var authentication = context.Services.GetRequiredService<AuthenticationStateProvider>();
        context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>()
            .NavigateTo("/operate/services/parcels/settings");
        var layout = RenderLayerLayout(context);
        layout.WaitForAssertion(() => Assert.Contains("Public", layout.Find("nav[aria-label='Breadcrumb']").TextContent));
        var publication = layout.FindComponent<CascadingValue<ConsolePublicationTreeContext>>().Instance.Value;
        var previousPath = publication.Tree.PathTo("/operate/services/parcels/settings");
        var publish = layout.FindComponent<ConsolePublicationNavigation>().Instance.SelectionChanged;
        authorization.SetNotAuthorized();
        Assert.False((await authentication.GetAuthenticationStateAsync()).User.Identity?.IsAuthenticated is true);
        layout.WaitForAssertion(() => Assert.Empty(layout.FindAll("[role='tree']")));
        layout.WaitForAssertion(() => Assert.DoesNotContain("Public", layout.Find("nav[aria-label='Breadcrumb']").TextContent));
        await layout.InvokeAsync(() => publish.InvokeAsync(previousPath));
        Assert.DoesNotContain("Public", layout.Find("nav[aria-label='Breadcrumb']").TextContent);
    }

    [Fact]
    public void PublicationBreadcrumbDropsFolderWhenAFilteredReadIsForbidden()
    {
        var source = new PublicationNavigationSource
        {
            ServiceSummaries = [new() { ServiceName = "parcels", FolderId = "public", FolderName = "Public", LayerCount = 1 }]
        };
        using var context = PublicationLayoutContext(source);
        context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>()
            .NavigateTo("/operate/services/parcels/settings");
        var layout = RenderLayerLayout(context);
        layout.WaitForAssertion(() => Assert.Contains("Public", layout.Find("nav[aria-label='Breadcrumb']").TextContent));
        source.FailNext = OperateSectionResult<HonuaAdminServicePage>.Denied(OperateSectionStatus.Forbidden, "Catalog access denied.");
        layout.Find("[data-tree-filter]").Input("parcels");
        layout.WaitForAssertion(() => Assert.DoesNotContain("Public", layout.Find("nav[aria-label='Breadcrumb']").TextContent));
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
        var source = new PublicationNavigationSource();
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
        var source = new PublicationNavigationSource();
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
    public void FailedEnvironmentProfileRetriesTheReadAndRestoresTheCatalog()
    {
        using var context = Context();
        var profiles = new OneShotProfileFailure(new ConsoleEnvironmentProfile
        {
            Id = "field",
            DisplayName = "Field",
            ServerBaseUri = new Uri("https://field.example.test")
        });
        var source = new PublicationNavigationSource();
        context.Services.AddSingleton<IConsoleEnvironmentProfileStore>(profiles);
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(source);
        var tree = context.Render<ConsolePublicationNavigation>();
        tree.WaitForAssertion(() => Assert.Contains("The active environment could not be read.", tree.Markup));
        Assert.Equal(1, profiles.Reads);
        Assert.Equal(0, source.ServiceReads);
        tree.Find("[data-tree-retry]").Click();
        tree.WaitForAssertion(() => Assert.Contains("service-000", tree.Markup));
        Assert.Equal(2, profiles.Reads);
        Assert.Equal(1, source.ServiceReads);
        Assert.DoesNotContain("The active environment could not be read.", tree.Markup);
    }

    [Fact]
    public async Task NewerSearchWinsWhenTheOlderSearchCompletesLast()
    {
        using var context = Context();
        var source = new PublicationNavigationSource();
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(source);
        var tree = context.Render<ConsolePublicationNavigation>();
        tree.WaitForState(() => tree.FindAll("[role='treeitem']").Count == 26);
        source.HoldCount = 1;
        tree.Find("input").Input("service-001");
        tree.WaitForAssertion(() => Assert.Equal(2, source.ServiceReads));
        tree.Find("input").Input("service-499");
        tree.WaitForAssertion(() => Assert.Contains("service-499 10 layers", tree.Markup));
        source.ReleaseNext();
        await Task.Delay(50);
        Assert.Contains("service-499 10 layers", tree.Markup);
        Assert.Equal("service-499", source.LastQuery.NameQuery);
        Assert.Equal(3, source.ServiceReads);
    }

    [Fact]
    public async Task ShowMoreDoesNotPageASearchThatHasNotSettled()
    {
        using var context = Context();
        var source = new PublicationNavigationSource();
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(source);
        var tree = context.Render<ConsolePublicationNavigation>();
        tree.WaitForState(() => tree.FindAll("[role='treeitem']").Count == 26);
        source.HoldCount = 1;
        tree.Find("input").Input("service-499");
        tree.WaitForAssertion(() => Assert.Equal(2, source.ServiceReads));
        tree.Find("[data-tree-more='environment']").Click();
        Assert.Equal(2, source.ServiceReads);
        source.ReleaseNext();
        tree.WaitForAssertion(() => Assert.Contains("service-499 10 layers", tree.Markup));
        Assert.Equal(1, source.LastQuery.Page);
    }

    [Fact]
    public async Task SupersededReloadLeavesTheReplacementLoading()
    {
        using var context = new BunitContext();
        var authorization = context.AddAuthorization();
        authorization.SetAuthorized("first-synthetic-operator");
        var source = new PublicationNavigationSource();
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(source);
        var tree = context.Render<ConsolePublicationNavigation>();
        tree.WaitForState(() => tree.FindAll("[role='treeitem']").Count == 26);
        source.HoldCount = 2;
        authorization.SetAuthorized("second-synthetic-operator");
        tree.WaitForAssertion(() => Assert.Equal(2, source.ServiceReads));
        Assert.True(tree.Find("input").HasAttribute("disabled"));
        authorization.SetAuthorized("third-synthetic-operator");
        tree.WaitForAssertion(() => Assert.Equal(3, source.ServiceReads));
        source.ReleaseNext();
        await Task.Delay(50);
        Assert.True(tree.Find("input").HasAttribute("disabled"));
        source.ReleaseNext();
        tree.WaitForAssertion(() => Assert.False(tree.Find("input").HasAttribute("disabled")));
        Assert.Contains("service-000", tree.Markup);
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
        var source = new PublicationNavigationSource();
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

    private static PublicationNavigationSource DelayedSource() => new()
    {
        PendingLayers = new(TaskCreationOptions.RunContinuationsAsynchronously)
    };

    private sealed class OneShotProfileFailure(ConsoleEnvironmentProfile profile) : IConsoleEnvironmentProfileStore
    {
        public int Reads { get; private set; }
        public event Action? ActiveProfileChanged { add { } remove { } }
        public Task<ConsoleEnvironmentProfile?> GetActiveProfileAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            if (Reads == 1) throw new IOException("environment profile unavailable");
            return Task.FromResult<ConsoleEnvironmentProfile?>(profile);
        }
        public Task<IReadOnlyList<ConsoleEnvironmentProfile>> ListProfilesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ConsoleEnvironmentProfile?> GetProfileAsync(string profileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpsertProfileAsync(ConsoleEnvironmentProfile next, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ActivateProfileAsync(string profileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ConsoleEnvironmentState?> GetStateAsync(string profileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SaveStateAsync(ConsoleEnvironmentState state, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FolderReadAndMutationSignalRefreshTheExistingNavigation(bool denied)
    {
        using var context = Context();
        var source = new PublicationNavigationSource();
        var folders = new FolderClient { Denied = denied };
        var changes = new ConsoleFolderChanges();
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(source);
        context.Services.AddSingleton<IConsoleFolderClient>(folders);
        context.Services.AddSingleton(changes);
        var tree = context.Render<ConsolePublicationNavigation>();
        if (denied) tree.WaitForAssertion(() => Assert.Contains("Folder read refused", tree.Markup));
        else tree.WaitForAssertion(() => Assert.Contains("Empty survey", tree.Markup));
        Assert.Equal(1, folders.Reads);
        await tree.InvokeAsync(changes.Notify);
        tree.WaitForAssertion(() => Assert.Equal(2, folders.Reads));
        Assert.Equal(2, source.ServiceReads);
    }

    private sealed class FolderClient : IConsoleFolderClient
    {
        public bool Denied { get; init; }
        public int Reads { get; private set; }
        public Task<OperateSectionResult<ConsoleFolderCatalog>> ReadAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(Denied
                ? OperateSectionResult<ConsoleFolderCatalog>.Denied(OperateSectionStatus.Forbidden, "Folder read refused")
                : OperateSectionResult<ConsoleFolderCatalog>.Allowed(new([new Folder { Metadata = new() { Id = "empty", Name = "Empty survey" } }], "etag", "authority")));
        }
        public Task<ConsoleFolderMutationResult> CreateAsync(ConsoleFolderCatalog catalog, CreateFolderRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ConsoleFolderMutationResult> UpdateAsync(ConsoleFolderCatalog catalog, string folderId, UpdateFolderRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ConsoleFolderMutationResult> DeleteAsync(ConsoleFolderCatalog catalog, string folderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ConsoleFolderMutationResult> MoveAsync(ConsoleFolderCatalog catalog, string serviceName, MoveServiceRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData("denied", false)]
    [InlineData("changed-before", false)]
    [InlineData("changed-after", false)]
    [InlineData("success", true)]
    [InlineData("not-found", true)]
    public void UnverifiedPublishCanOnlyUnlockAfterAnAllowedReadOfTheOriginalAuthority(string outcome, bool expectedVerified)
    {
        using var context = Context();
        var authority = new VerificationAuthority { Current = outcome != "changed-before" };
        var source = new PublicationNavigationSource { ServiceSummaries = outcome == "not-found" ? [] : null };
        var changes = new ConsoleFolderChanges();
        var reloads = 0;
        changes.Changed += () => reloads++;
        context.Services.AddSingleton(changes);
        if (outcome == "denied") source.FailNext = OperateSectionResult<HonuaAdminServicePage>.Denied(OperateSectionStatus.Forbidden, "Catalog denied");
        if (outcome == "changed-after") source.OnServiceRead = () => authority.Current = false;
        context.Services.AddSingleton<IConsoleAuthorityBindingValidator>(authority);
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(source);
        var verified = false;
        var panel = context.Render<PublishOutcomeVerification>(parameters => parameters
            .Add(component => component.AuthorityBinding, "original")
            .Add(component => component.ServiceName, "service-000")
            .Add(component => component.LayerName, "Parcels")
            .Add(component => component.OnVerified, () => verified = true));
        panel.Find("[data-publish-verify]").Click();
        Assert.Equal(expectedVerified, verified);
        Assert.Equal(expectedVerified ? 1 : 0, reloads);
        if (expectedVerified)
        {
            var readback = panel.Find("[data-publish-readback]").TextContent;
            if (outcome == "not-found") Assert.Contains("not found on the first page", readback);
            else Assert.Contains("10 layers", readback);
            Assert.NotEmpty(panel.Find("[data-publish-readback] a").GetAttribute("href")!);
        }
        Assert.Equal(outcome == "changed-before" ? 0 : 1, source.ServiceReads);
    }

    private sealed class VerificationAuthority : IConsoleAuthorityBindingValidator
    {
        public bool Current { get; set; } = true;
        public Task<bool> IsCurrentAsync(string binding, CancellationToken cancellationToken = default) => Task.FromResult(Current && binding == "original");
    }

    private static BunitContext Context()
    {
        var context = new BunitContext();
        context.AddAuthorization().SetAuthorized("synthetic-operator");
        return context;
    }

}
