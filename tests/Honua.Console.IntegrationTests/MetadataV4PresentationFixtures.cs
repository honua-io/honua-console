using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class MetadataV4PresentationFixtures
{
    [Theory]
    [InlineData(null, "flood", "Dataset metadata", "metadata-dataset-v4")]
    [InlineData("fields", null, "Metadata fields", "metadata-fields-v4")]
    [InlineData("import", "flood", "Import metadata", "metadata-import-v4")]
    public void CaptureExistingPreviewForDesignComparison(string? section, string? dataset, string heading, string name)
    {
        using var context = MetadataFixtureSource.Context();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        var page = context.Render<MetadataPreviewPage>(p => p
            .Add(x => x.DatasetId, dataset)
            .Add(x => x.Section, section));

        Assert.Equal(heading, page.Find("h1").TextContent);
        Assert.Contains("Fixture preview · synthetic data. Local changes are not saved or proposed.", page.Markup);
        Assert.Single(page.FindAll(".console-page-heading-v4"));
        Assert.NotEmpty(page.FindAll(".metadata-preview-nav a"));
        CatalogAdminPreview.CaptureV4(name, page.Markup);
    }
}
