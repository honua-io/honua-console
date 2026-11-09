using Bunit;
using Bunit.TestDoubles;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class MetadataPreviewEditingTests
{
    [Fact]
    public void OverrideAndDiscard_RestoreReportedValueAndHistory()
    {
        var source = new MetadataFixtureSource();
        using var context = MetadataFixtureSource.Context(source);
        var page = context.Render<MetadataPreviewPage>(p => p.Add(view => view.DatasetId, "flood"));
        var history = page.Find(".metadata-history").TextContent;
        page.Find("[data-override='title']").Input("</textarea><script>alert('example')</script>");
        Assert.Equal("</textarea><script>alert('example')</script>", page.Find("[data-effective='title']").TextContent);
        Assert.Empty(page.FindAll("script, img, iframe"));
        Assert.Equal(history, page.Find(".metadata-history").TextContent);
        Assert.Equal("Flood hazard areas", source.Read.Dataset!.Fields[0].LocalValue);
        Assert.True(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
        CatalogAdminPreview.Capture("metadata-dirty", page.Markup);
        page.Find("[data-metadata-discard]").Click();
        Assert.Equal("Flood hazard areas", page.Find("[data-effective='title']").TextContent);
        Assert.False(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
        Assert.Contains(context.JSInterop.Invocations, call => call.Identifier == "Blazor._internal.domWrapper.focus");
        Assert.Single(source.Requests);
    }

    [Fact]
    public void Inheritance_ClearAndExplicitEmptyRemainDistinct()
    {
        using var context = MetadataFixtureSource.Context();
        var page = context.Render<MetadataPreviewPage>(p => p.Add(view => view.DatasetId, "flood"));
        page.Find("[data-override='lineage']").Input("");
        Assert.Equal("", page.Find("[data-effective='lineage']").TextContent);
        page.Find("[data-inherit='lineage']").Click();
        Assert.Equal("Maintained by Emergency GIS.", page.Find("[data-effective='lineage']").TextContent);
        Assert.Contains("Proposed", page.FindAll("details")[1].TextContent);
        Assert.True(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
        page.Find("[data-metadata-discard]").Click();
        Assert.Equal("Reviewed data.", page.Find("[data-effective='lineage']").TextContent);
        Assert.Empty(page.FindAll("[data-override='extent'], [data-inherit='extent']"));
        page.Find("details").SetAttribute("open", "");
        CatalogAdminPreview.Capture("metadata-history", page.Markup);
    }

    [Theory]
    [InlineData("dataset")]
    [InlineData("fields")]
    [InlineData("import")]
    public void DuplicateKeys_RenderUnavailable(string section)
    {
        var source = new MetadataFixtureSource();
        var example = source.Read;
        source.Read = example with
        {
            Dataset = example.Dataset! with { Fields = [example.Dataset.Fields[0], example.Dataset.Fields[0]] },
            Schemas = [example.Schemas[0], example.Schemas[0]],
            Import = example.Import! with { Rows = [example.Import.Rows[0], example.Import.Rows[0]] }
        };
        using var context = MetadataFixtureSource.Context(source);
        var page = context.Render<MetadataPreviewPage>(p => p.Add(view => view.DatasetId, section == "fields" ? null : "flood").Add(view => view.Section, section == "dataset" ? null : section));
        Assert.Contains("identities must be unique", page.Markup);
        Assert.Empty(page.FindAll("input, textarea"));
    }

    [Fact]
    public void SchemaDraft_PreservesChoicesAndAccess_AndDiscardsRetirement()
    {
        using var context = MetadataFixtureSource.Context();
        var page = context.Render<MetadataPreviewPage>(p => p.Add(view => view.Section, "fields"));
        Assert.True(page.Find("[data-schema-add]").HasAttribute("disabled"));
        CatalogAdminPreview.Capture("metadata-fields", page.Markup);
        page.Find("#metadata-schema-name").Input("Review status");
        page.Find("#metadata-schema-key").Input("county:review");
        page.Find("#metadata-schema-type").Change("Choice list");
        page.Find("#metadata-schema-choices").Input("Ready\nReady");
        page.Find("[data-schema-kind='Dataset']").Change(true);
        Assert.True(page.Find("[data-schema-add]").HasAttribute("disabled"));
        page.Find("#metadata-schema-choices").Input("Ready\nPending");
        page.Find("#metadata-schema-required").Change(true);
        page.Find("#metadata-schema-inherits").Change(true);
        page.Find("#metadata-schema-visibility").Change("Signed-in only");
        page.Find("[data-schema-add]").Click();
        var row = page.FindAll("tbody tr").Last();
        Assert.Contains("county:review", row.TextContent);
        Assert.Contains("Ready", row.TextContent);
        Assert.Contains("Pending", row.TextContent);
        Assert.Contains("Yes", row.TextContent);
        Assert.Contains("Dataset → service → folder", row.TextContent);
        Assert.Contains("Signed-in only", row.TextContent);
        page.Find("[data-retire='county:retention']").Change(true);
        Assert.Contains("Would retire locally", page.Markup);
        CatalogAdminPreview.Capture("metadata-fields-dirty", page.Markup);
        page.Find("[data-schema-discard]").Click();
        Assert.Equal(3, page.FindAll("tbody tr").Count);
        Assert.False(page.Find("[data-retire='county:retention']").HasAttribute("checked"));
        Assert.False(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
    }

    [Theory]
    [InlineData("county:retention")]
    [InlineData("bad key")]
    [InlineData(":missing")]
    [InlineData("missing:")]
    public void SchemaDraft_RejectsDuplicateOrMalformedKey(string key)
    {
        using var context = MetadataFixtureSource.Context();
        var page = context.Render<MetadataPreviewPage>(p => p.Add(view => view.Section, "fields"));
        page.Find("#metadata-schema-name").Input("Example");
        page.Find("#metadata-schema-key").Input(key);
        page.Find("[data-schema-kind='Dataset']").Change(true);
        Assert.True(page.Find("[data-schema-add]").HasAttribute("disabled"));
        Assert.Equal(3, page.FindAll("tbody tr").Count);
    }

    [Fact]
    public void ImportFilter_KeepsHiddenSelections_AndResetRestoresSuppliedDefault()
    {
        var source = new MetadataFixtureSource();
        source.Read = source.Read with { Import = source.Read.Import! with { Rows = [source.Read.Import.Rows[0], source.Read.Import.Rows[1] with { Changed = false }] } };
        using var context = MetadataFixtureSource.Context(source);
        var page = context.Render<MetadataPreviewPage>(p => p.Add(view => view.DatasetId, "flood").Add(view => view.Section, "import"));
        page.Find("[data-import-row='frequency']").Change(true);
        page.Find("[data-import-changes]").Change(true);
        Assert.Single(page.FindAll("tbody tr"));
        Assert.Contains("2 of 2 mapped fields selected", page.Markup);
        Assert.True(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
        CatalogAdminPreview.Capture("metadata-import-dirty", page.Markup);
        page.Find("[data-import-reset]").Click();
        page.Find("[data-import-changes]").Change(false);
        Assert.False(page.Find("[data-import-row='frequency']").HasAttribute("checked"));
        Assert.True(page.Find("[data-import-row='title']").HasAttribute("checked"));
        Assert.False(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
        Assert.False(source.Read.Import!.Rows[1].Selected);
        Assert.Single(source.Requests);
    }

    [Theory]
    [InlineData("dataset")]
    [InlineData("fields")]
    [InlineData("import")]
    public void UnsavedChanges_BlockNavigationUntilDiscarded(string section)
    {
        using var context = MetadataFixtureSource.Context();
        context.JSInterop.Setup<bool>("confirm", _ => true).SetResult(false);
        var page = context.Render<MetadataPreviewPage>(p => p.Add(view => view.DatasetId, section == "fields" ? null : "flood").Add(view => view.Section, section == "dataset" ? null : section));
        var edit = section switch { "fields" => "#metadata-schema-name", "import" => "[data-import-row='title']", _ => "[data-override='title']" };
        if (section == "import") page.Find(edit).Change(false);
        else page.Find(edit).Input("Unsaved");
        var navigation = (BunitNavigationManager)context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/catalogs");
        page.WaitForAssertion(() => Assert.Equal(NavigationState.Prevented, navigation.History.First().State));
        var discard = section switch { "fields" => "[data-schema-discard]", "import" => "[data-import-reset]", _ => "[data-metadata-discard]" };
        page.Find(discard).Click();
        Assert.False(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
        navigation.NavigateTo("/catalogs");
        page.WaitForAssertion(() => Assert.Equal(NavigationState.Succeeded, navigation.History.First().State));
    }
}
