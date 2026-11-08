using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Components.Operate;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class ConsoleVocabularyRenderTests
{
    [Fact]
    public void DatasetTree_EmptyAndFiltered_UsesDatasetLabels()
    {
        using var context = new BunitContext();
        var tree = context.Render<ResourcePublicationsTree>();
        Assert.Contains("No datasets yet.", tree.Find("[data-tree-empty]").TextContent);
        Assert.Equal("Filter datasets", tree.Find("input").GetAttribute("aria-label"));

        tree.Render(parameters => parameters.Add(p => p.Nodes,
            [new ResourceTreeNode("roads", "Roads", "PostGIS", "Running", 1, "LineString", [])]));
        tree.Find("input").Input("missing");
        Assert.Contains("No datasets match this filter.", tree.Find("[data-tree-empty]").TextContent);
        tree.Find("button").Click();
        Assert.Equal("Roads", tree.Find(".resource-tree__name").TextContent);
    }

    [Fact]
    public void DatasetTree_Expanded_LabelsLayersAndKeepsProtocolRoute()
    {
        using var context = new BunitContext();
        var tree = context.Render<ResourcePublicationsTree>(parameters => parameters.Add(p => p.Nodes,
            [new ResourceTreeNode("roads", "Roads", "PostGIS", "Running", 1, "LineString",
                [new PublicationTreeNode("wfs", "WFS", true, "/wfs")])]));
        tree.Find(".resource-tree__name").Click();
        Assert.Single(tree.FindAll("ul[aria-label='Layers']"));
        Assert.Equal("/wfs", tree.Find(".resource-tree__publication-route").TextContent);
    }

    [Theory]
    [InlineData(PublishModeBar.PublishMode.Quick)]
    [InlineData(PublishModeBar.PublishMode.AuthorFirst)]
    public void PublishMode_UsesDatasetAndLayerWords(PublishModeBar.PublishMode mode)
    {
        using var context = new BunitContext();
        var bar = context.Render<PublishModeBar>(parameters => parameters.Add(p => p.Mode, mode));
        Assert.Contains("Author dataset first", bar.FindAll("button")[1].TextContent);
        Assert.Contains("dataset", bar.Find(".publish-mode-hint").TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("slot", bar.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("projection", bar.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProtocolPicker_UsesServiceAndDatasetWords()
    {
        using var context = new BunitContext();
        var picker = context.Render<PublishProtocolPicker>(parameters => parameters.Add(p => p.ServiceSlot, "roads"));
        Assert.Equal("Service", picker.Find(".studio-form-label").TextContent);
        Assert.Contains("dataset", picker.Find(".publish-protocol-picker__slot .console-muted").TextContent);
        Assert.Equal("roads", picker.Find("[data-service-slot]").TextContent);
    }

    [Fact]
    public void LayerPreview_NoSelection_AsksForALayer()
    {
        using var context = new BunitContext();
        var preview = context.Render<LayerPreviewPane>();
        Assert.Equal("Select a layer", preview.Find("h2").TextContent);
        Assert.DoesNotContain("publication", preview.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnvironmentSetup_HidesTenantSelectionAndCreatesUnscopedEnvironment()
    {
        using var context = new BunitContext();
        var profiles = new InMemoryConsoleEnvironmentProfileStore([]);
        context.Services.AddSingleton<IConsoleEnvironmentProfileStore>(profiles);
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var page = context.Render<EnvironmentProfileNewPage>();
        Assert.Equal("Add environment", page.Find("h1").TextContent);
        Assert.DoesNotContain("tenant", page.Markup, StringComparison.OrdinalIgnoreCase);
        page.Find("#environment-display-name").Change("Production");
        page.Find("#environment-server-url").Change("https://gis.example.com");
        page.Find("button").Click();
        var profile = Assert.Single(await profiles.ListProfilesAsync());
        Assert.Equal("Production", profile.DisplayName);
        Assert.True(string.IsNullOrEmpty(profile.TenantId));
        Assert.True(string.IsNullOrEmpty(profile.Account.TenantId));
    }

    [Theory]
    [InlineData(PublishingResourceKind.Service, "Service")]
    [InlineData(PublishingResourceKind.Layer, "Layer")]
    [InlineData(PublishingResourceKind.CatalogEntry, "Catalog listing")]
    [InlineData(PublishingResourceKind.StudioArtifact, "Content")]
    public void PublishingReview_PreservesTheReportedKind(PublishingResourceKind kind, string label)
    {
        using var context = new BunitContext();
        var review = new PublishingReview("id", "Roads", kind, "service", [],
            new PublishingCatalogRegistration(false, null, "private"), "private", [], "none", "operator",
            new PublishingReviewLinks(null, null, null, null));
        var card = context.Render<PublishingReviewCard>(parameters => parameters.Add(p => p.Review, review));
        Assert.Equal(label, card.Find(".console-kicker").TextContent);
    }

    [Fact]
    public void Navigation_SeparatesDatasetsConnectionsAndEnvironments()
    {
        var entries = ConsoleNavigation.Groups.SelectMany(group => group.Destinations).ToArray();
        Assert.Equal("Datasets", Assert.Single(entries, entry => entry.Path == "/operate/data").Label);
        Assert.Equal("Connections", Assert.Single(entries, entry => entry.Path == "/operate/connections").Label);
        Assert.Equal("Users", Assert.Single(entries, entry => entry.Path == "/operate/access/members").Label);
        Assert.Equal("Roles", Assert.Single(entries, entry => entry.Path == "/operate/access").Label);
    }
}
