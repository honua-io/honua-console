using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Tests;

public sealed class ConsolePublicationTreeTests
{
    [Fact]
    public void FiveHundredFlatServicesUseBoundedPagesAndFilterTheEntireSummaryRead()
    {
        var tree = new ConsolePublicationTree();
        tree.Load(Enumerable.Range(0, 500).Select(index => new HonuaAdminServiceSummary
        {
            ServiceName = $"service-{index:D3}",
            LayerCount = 200
        }).ToArray(), "Synthetic environment");
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
        var tree = new ConsolePublicationTree();
        tree.Load(Enumerable.Range(0, 500).Select(index => new HonuaAdminServiceSummary
        {
            ServiceName = $"service-{index:D3}",
            LayerCount = 1
        }).ToArray(), "Services");
        tree.RevealPath("/operate/services/service-450/settings");
        Assert.Equal(25, tree.VisibleChildren(tree.Root).Count);
        Assert.Equal("service-450", tree.VisibleChildren(tree.Root)[0].Label);
        Assert.Equal(450, tree.Root.VisibleOffset);
        tree.ShowFirstPage(tree.Root);
        Assert.Equal("service-000", tree.VisibleChildren(tree.Root)[0].Label);
    }

    [Fact]
    public void FolderFixtureUsesReportedMembershipAndKeepsInternalServiceIdentity()
    {
        using var stream = typeof(ConsolePublicationTreeTests).Assembly.GetManifestResourceStream(
            "Honua.Console.Shell.Tests.Fixtures.service-navigation-folders.json")!;
        var summaries = JsonSerializer.Deserialize<HonuaAdminServiceSummary[]>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var tree = new ConsolePublicationTree();
        tree.Load(summaries, "Synthetic environment");
        var folders = tree.Root.Children.Where(node => node.Kind == ConsolePublicationNodeKind.Folder).ToArray();
        Assert.Equal(["Internal", "Public"], folders.Select(node => node.Label));
        Assert.Equal("Parcels", Assert.Single(folders[0].Children).Label);
        Assert.Equal("/operate/services/internal-parcels/settings", folders[0].Children[0].Href);
        Assert.Equal("/operate/services/public-parcels/settings", folders[1].Children[0].Href);
        Assert.Contains(tree.Root.Children, node => node.Label == "roads/legacy" && node.Href == "/operate/services/roads%2Flegacy/settings");
        Assert.Contains(tree.Root.Children, node => node.Label == "unresolved-folder" && node.Kind == ConsolePublicationNodeKind.Service);
        Assert.All(folders, folder => Assert.Null(folder.Href));
    }

    [Fact]
    public void FilteringLoadedLayersKeepsTheirAncestorsWithoutInventingUnloadedMatches()
    {
        var tree = new ConsolePublicationTree();
        tree.Load([new() { ServiceName = "roads", LayerCount = 10 }, new() { ServiceName = "water", LayerCount = 40 }], "Services");
        tree.Root.Children[0].Children.Add(new() { Key = "layer-one", Label = "Bridges", Kind = ConsolePublicationNodeKind.Layer });
        tree.SetFilter("bridge");
        Assert.Equal("roads", Assert.Single(tree.VisibleChildren(tree.Root)).Label);
        Assert.Equal(1, tree.LoadedLayerCount);
        tree.SetFilter("pipes");
        Assert.Empty(tree.VisibleChildren(tree.Root));
        Assert.Equal(40, tree.Root.Children[1].ReportedCount);
        Assert.Empty(tree.Root.Children[1].Children);
    }
}
