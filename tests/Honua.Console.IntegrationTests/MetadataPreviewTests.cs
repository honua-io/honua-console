using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class MetadataPreviewTests
{
    [Fact]
    public void MetadataFieldsNavigation_FollowsTheExistingFlag()
    {
        var enabled = ConsoleNavigation.Definitions(new CatalogAdminOptions { Enabled = true });
        var disabled = ConsoleNavigation.Definitions(new CatalogAdminOptions());
        Assert.Single(enabled.SelectMany(group => group.Destinations).Where(entry => entry.Path == "/metadata/fields"));
        Assert.DoesNotContain(disabled.SelectMany(group => group.Destinations), entry => entry.Path == "/metadata/fields");
    }

    [Fact]
    public void DisabledFlag_DoesNotReadMetadata()
    {
        using var context = new BunitContext();
        var source = new MetadataFixtureSource();
        context.Services.AddSingleton(new CatalogAdminOptions());
        context.Services.AddSingleton<IMetadataPreviewSource>(source);
        var page = context.Render<MetadataPreviewPage>();
        Assert.Contains("Metadata preview is disabled", page.Markup);
        Assert.Empty(source.Requests);
    }

    [Fact]
    public void DatasetEditor_UseInheritedChangesOnlyTheLocalPreview()
    {
        using var context = MetadataFixtureSource.Context();
        var page = context.Render<MetadataPreviewPage>(parameters => parameters.Add(p => p.DatasetId, "flood"));
        page.Find("[data-override='frequency']").Input("daily");
        Assert.Contains("Unsaved local changes", page.Markup);
        page.Find("[data-inherit='frequency']").Click();
        Assert.Equal("monthly", page.Find("[data-effective='frequency']").TextContent);
        Assert.Contains("Local fields unchanged", page.Markup);
        Assert.Contains("Example history", page.Markup);
        Assert.Contains("Console", page.Markup);
        CatalogAdminPreview.Capture("metadata-dataset", page.Markup);
    }

    [Fact]
    public void ImportSelection_RespectsTheSuppliedChoices_AndCanKeepEveryCurrentValue()
    {
        using var context = MetadataFixtureSource.Context();
        var page = context.Render<MetadataPreviewPage>(parameters => parameters.Add(p => p.DatasetId, "flood").Add(p => p.Section, "import"));
        Assert.False(page.Find("[data-import-row='frequency']").HasAttribute("checked"));
        Assert.True(page.Find("[data-import-row='title']").HasAttribute("checked"));
        page.Find("[data-import-row='title']").Change(false);
        Assert.Contains("0 of 2 mapped fields selected", page.Markup);
        Assert.Contains("No file has been uploaded", page.Markup);
        CatalogAdminPreview.Capture("metadata-import", page.Markup);
    }
}
