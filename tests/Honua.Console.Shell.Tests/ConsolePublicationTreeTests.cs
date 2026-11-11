using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Tests;

public sealed class ConsolePublicationTreeTests
{
    [Fact]
    public void EmptyFoldersAndRootServicesShareTheExistingPagedTree()
    {
        var tree = new ConsolePublicationTree();
        tree.Load(Page([new() { ServiceName = "roads" }]), "Environment",
            [new() { Metadata = new() { Id = "empty-folder", Name = "Transport", Title = "Transport data" } }]);

        Assert.Equal(1, tree.ServiceCount);
        var folder = Assert.Single(tree.Root.Children, node => node.Kind == ConsolePublicationNodeKind.Folder);
        Assert.Equal("folder:empty-folder", folder.Key);
        Assert.Equal("Transport data (Transport)", folder.Label);
        Assert.Empty(folder.Children);
        Assert.Null(folder.ChildCount);
        Assert.Contains(tree.Root.Children, node => node.ServiceName == "roads");
        tree.SetFilter("transport");
        Assert.Equal(folder, Assert.Single(tree.VisibleChildren(tree.Root)));
        tree.Append(Page([new() { ServiceName = "moved", FolderId = "empty-folder", FolderName = "Transport" }]));
        Assert.Equal("moved", Assert.Single(folder.Children).ServiceName);
        Assert.Equal("Transport data (Transport)", folder.Label);
    }

    [Fact]
    public void FolderIdResolvesThroughTheCatalogWhenTheServiceProjectionHasNoFolderName()
    {
        var tree = new ConsolePublicationTree();
        tree.Load(Page([new() { ServiceName = "logical", FolderId = "folder" }]), "Environment",
            [new() { Metadata = new() { Id = "folder", Name = "Survey" } }]);
        var folder = Assert.Single(tree.Root.Children);
        Assert.Equal(ConsolePublicationNodeKind.Folder, folder.Kind);
        Assert.Equal("logical", Assert.Single(folder.Children).ServiceName);
    }

    [Fact]
    public void CatalogFoldersNeverClaimZeroWhenTheirServicesMayBeHiddenOrOnAnotherPage()
    {
        var tree = new ConsolePublicationTree();
        tree.Load(new() { Total = 100, Items = [] }, "Environment",
            [new() { Metadata = new() { Id = "folder", Name = "Survey" } }]);
        Assert.True(tree.HasUnloadedServices);
        Assert.Null(Assert.Single(tree.Root.Children).ChildCount);
    }

    [Fact]
    public void FiveHundredFlatServicesUseBoundedPagesAndFilterTheEntireSummaryRead()
    {
        var tree = CreateLargeTree(200);
        Assert.Equal(500, tree.ServiceCount);
        Assert.Equal(25, tree.VisibleChildren(tree.Root).Count);
        Assert.Equal(0, tree.LoadedLayerCount);
        Assert.All(tree.Root.Children, node => Assert.Empty(node.Children));
        tree.ShowMore(tree.Root);
        Assert.Equal(50, tree.VisibleChildren(tree.Root).Count);
        tree.SetFilter("service-499");
        Assert.Equal("service-499", Assert.Single(tree.VisibleChildren(tree.Root)).Label);
        tree.SetFilter("");
        Assert.Equal(25, tree.VisibleChildren(tree.Root).Count);
    }

    [Fact]
    public void DeepLinkedServiceIsRevealedWithoutRenderingThePrecedingFourHundredRows()
    {
        var tree = CreateLargeTree(1);
        tree.RevealPath("/operate/services/service-450/settings");
        Assert.Equal(25, tree.VisibleChildren(tree.Root).Count);
        Assert.Equal("service-450", tree.VisibleChildren(tree.Root)[0].Label);
        Assert.Equal(450, tree.Root.VisibleOffset);
        tree.ShowFirstPage(tree.Root);
        Assert.Equal("service-000", tree.VisibleChildren(tree.Root)[0].Label);
    }

    [Fact]
    public void FilteredDeepLinkKeepsTheMatchingServiceOnItsFilteredPage()
    {
        var tree = CreateLargeTree(200);
        tree.SetFilter("service-499");
        tree.RevealPath("/operate/services/service-499/settings");
        Assert.Equal("service-499", tree.Filter);
        Assert.Equal("service-499", Assert.Single(tree.VisibleChildren(tree.Root)).Label);
        Assert.Equal(0, tree.Root.VisibleOffset);
    }

    [Fact]
    public void DeepLinkOutsideTheFilterClearsItAndRevealsABoundedSelectedPage()
    {
        var tree = CreateLargeTree(200);
        tree.SetFilter("service-001");
        tree.RevealPath("/operate/services/service-499/settings");
        Assert.Empty(tree.Filter);
        Assert.Equal(25, tree.VisibleChildren(tree.Root).Count);
        Assert.Contains(tree.VisibleChildren(tree.Root), node => node.Label == "service-499");
        Assert.Equal(475, tree.Root.VisibleOffset);
    }

    [Fact]
    public void FolderFixtureUsesReportedMembershipAndKeepsInternalServiceIdentity()
    {
        using var stream = typeof(ConsolePublicationTreeTests).Assembly.GetManifestResourceStream(
            "Honua.Console.Shell.Tests.Fixtures.service-navigation-folders.json")!;
        var summaries = JsonSerializer.Deserialize<Honua.Sdk.Admin.Models.ServiceSummary[]>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var tree = new ConsolePublicationTree();
        tree.Load(Page(summaries), "Synthetic environment");
        var folders = tree.Root.Children.Where(node => node.Kind == ConsolePublicationNodeKind.Folder).ToArray();
        Assert.Equal(["Folder unavailable", "Internal", "Public"], folders.Select(node => node.Label));
        var internalFolder = folders.Single(folder => folder.Label == "Internal");
        var publicFolder = folders.Single(folder => folder.Label == "Public");
        Assert.Equal("Parcels", Assert.Single(internalFolder.Children).Label);
        Assert.Equal("/operate/services/internal-parcels/settings", internalFolder.Children[0].Href);
        Assert.Equal("/operate/services/public-parcels/settings", publicFolder.Children[0].Href);
        Assert.Contains(tree.Root.Children, node => node.Label == "roads/legacy" && node.Href == "/operate/services/roads%2Flegacy/settings");
        Assert.Equal("unresolved-folder", Assert.Single(folders.Single(folder => folder.Label == "Folder unavailable").Children).Label);
        Assert.DoesNotContain(tree.Root.Children, node => node.Label == "unresolved-folder" && node.Kind == ConsolePublicationNodeKind.Service);
        Assert.All(folders, folder => Assert.Equal("/operate/folders/" + folder.Key["folder:".Length..], folder.Href));
    }

    [Fact]
    public void OpaqueServiceNamesCannotCollideWithDescendantKeySeparators()
    {
        var tree = new ConsolePublicationTree();
        tree.Load(Page([new() { ServiceName = "roads:layer:connection-layer-7", LayerCount = 1 }]), "Services");
        var service = Assert.Single(tree.Root.Children);
        Assert.Equal("service:roads%3Alayer%3Aconnection-layer-7", service.Key);
        Assert.Equal("roads:layer:connection-layer-7", service.ServiceName);
        Assert.Equal("/operate/services/roads%3Alayer%3Aconnection-layer-7/settings", service.Href);
    }

    [Fact]
    public void TreeCountsComeFromThePageRatherThanTheLoadedItems()
    {
        var tree = new ConsolePublicationTree();
        tree.Load(new HonuaAdminServicePage
        {
            Items = [new() { ServiceName = "roads", LayerCount = 4 }],
            Page = 1,
            PageSize = 100,
            Total = 2000,
            LayerTotal = 2000
        }, "Services");
        Assert.Equal(2000, tree.ServiceCount);
        Assert.Equal(2000, tree.LayerTotal);
        Assert.Equal(2000, tree.Root.ReportedCount);
        Assert.Single(tree.Root.Children);
    }

    [Fact]
    public void FilteringLoadedLayersKeepsTheirAncestorsWithoutInventingUnloadedMatches()
    {
        var tree = new ConsolePublicationTree();
        tree.Load(Page([new() { ServiceName = "roads", LayerCount = 10 }, new() { ServiceName = "water", LayerCount = 40 }]), "Services");
        tree.Root.Children[0].Children.Add(new() { Key = "layer-one", Label = "Bridges", Kind = ConsolePublicationNodeKind.Layer });
        tree.SetFilter("bridge");
        Assert.Equal("roads", Assert.Single(tree.VisibleChildren(tree.Root)).Label);
        Assert.Equal(1, tree.LoadedLayerCount);
        Assert.True(tree.Root.Children[0].Expanded);
        tree.Root.Children[0].Expanded = false;
        Assert.False(tree.Root.Children[0].Expanded);
        tree.SetFilter("");
        Assert.False(tree.Root.Children[0].Expanded);
        tree.SetFilter("pipes");
        Assert.Empty(tree.VisibleChildren(tree.Root));
        Assert.Equal(40, tree.Root.Children[1].ReportedCount);
        Assert.Empty(tree.Root.Children[1].Children);
    }
    private static ConsolePublicationTree CreateLargeTree(int layerCount)
    {
        var tree = new ConsolePublicationTree();
        tree.Load(Page(Enumerable.Range(0, 500).Select(index => new Honua.Sdk.Admin.Models.ServiceSummary
        {
            ServiceName = $"service-{index:D3}",
            LayerCount = layerCount
        }).ToArray()), "Synthetic environment");
        return tree;
    }

    private static HonuaAdminServicePage Page(IReadOnlyList<Honua.Sdk.Admin.Models.ServiceSummary> services) => new()
    {
        Items = services.ToArray(),
        Page = 1,
        PageSize = HonuaAdminCatalogQuery.MaxPageSize,
        Total = services.Count,
        LayerTotal = services.Sum(service => service.LayerCount),
    };

}
