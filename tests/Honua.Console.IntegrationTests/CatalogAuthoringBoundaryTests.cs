using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Microsoft.AspNetCore.Components;

namespace Honua.Console.IntegrationTests;

public sealed class CatalogAuthoringBoundaryTests
{
    [Theory]
    [InlineData("template", "Template is unavailable")]
    [InlineData("listing", "Item listing is unavailable")]
    public void MissingDocument_RemainsUnavailable(string section, string message)
    {
        using var context = CatalogAuthoringFixture.Context(CatalogAuthoringFixture.Read() with { Template = null, Listing = null });
        var page = context.Render<CatalogAdminPage>(parameters => parameters.Add(p => p.CatalogId, "open")
            .Add(p => p.Section, section).Add(p => p.TemplateId, "service & map").Add(p => p.ItemId, "emergency"));
        Assert.Contains(message, page.Markup);
        Assert.Empty(page.FindAll("textarea, .catalog-listing, .catalog-template-editor"));
    }

    [Fact]
    public void Listing_RejectsAnotherCatalogWithTheRequestedItemIdentity()
    {
        using var context = CatalogAuthoringFixture.Context(CatalogAuthoringFixture.Read() with
        { Listing = CatalogAuthoringFixture.Listing() with { CatalogId = "staff", Title = "Private listing" } });
        var page = context.Render<CatalogListingPage>(parameters => parameters.Add(p => p.CatalogId, "open").Add(p => p.ItemId, "emergency"));
        Assert.Contains("Item listing is unavailable", page.Markup);
        Assert.DoesNotContain("Private listing", page.Markup);
        Assert.Empty(page.FindAll(".catalog-listing"));
    }

    [Theory]
    [InlineData(true, "Yes")]
    [InlineData(false, "No")]
    [InlineData(null, "Not reported")]
    public void Listing_DistinguishesUnknownAndReportedBooleans(bool? canAppear, string label)
    {
        using var context = CatalogAuthoringFixture.Context(CatalogAuthoringFixture.Read() with
        { Listing = CatalogAuthoringFixture.Listing() with { CanAppear = canAppear } });
        var page = context.Render<CatalogListingPage>(parameters => parameters.Add(p => p.CatalogId, "open").Add(p => p.ItemId, "emergency"));
        Assert.Equal($"Can appear in catalogs: {label}", page.Find(".catalog-can-appear").TextContent.Trim());
        Assert.Empty(page.FindAll("#listing-can-appear"));
    }

    [Theory]
    [InlineData(".", "service")]
    [InlineData("..", "service")]
    [InlineData("open", ".")]
    [InlineData("open", "..")]
    public void Templates_DoNotLinkDotSegmentIdentities(string catalogId, string templateId)
    {
        using var context = new BunitContext();
        var page = context.Render<CatalogTemplatesView>(parameters => parameters.Add(p => p.Templates,
            new CatalogTemplateList(catalogId, [new(templateId, "Unaddressable template", "service")])));
        Assert.Empty(page.FindAll("tbody a"));
        Assert.Contains("Template destination is unavailable", page.Markup);
    }

    [Theory]
    [InlineData("parks & roads", "service / map", "/catalogs/parks%20%26%20roads/templates/service%20%2F%20map")]
    [InlineData("%2E%2E", "%2F", "/catalogs/%252E%252E/templates/%252F")]
    public void Templates_PreserveReservedCharactersAndLiteralPercentSequences(string catalogId, string templateId, string destination)
    {
        using var context = new BunitContext();
        var page = context.Render<CatalogTemplatesView>(parameters => parameters.Add(p => p.Templates,
            new CatalogTemplateList(catalogId, [new(templateId, "Service page", "service")])));
        Assert.Equal(destination, page.Find("tbody a").GetAttribute("href"));
    }

    [Fact]
    public void Templates_RepeatedReportedIdentitiesDoNotCrashTheList()
    {
        using var context = new BunitContext();
        var page = context.Render<CatalogTemplatesView>(parameters => parameters.Add(p => p.Templates,
            new CatalogTemplateList("open", [new("home", "Home", "home"), new("home", "Repeated home", "home")])));
        Assert.Equal(2, page.FindAll("tbody tr").Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Listing_MissingDescriptionIsReported(string? description)
    {
        using var context = CatalogAuthoringFixture.Context(CatalogAuthoringFixture.Read() with
        { Listing = CatalogAuthoringFixture.Listing() with { Description = description } });
        var page = context.Render<CatalogListingPage>(parameters => parameters.Add(p => p.CatalogId, "open").Add(p => p.ItemId, "emergency"));
        Assert.Contains("Description is not reported", page.Markup);
        Assert.Empty(page.FindAll("#listing-description"));
    }

    [Fact]
    public void TemplateListRoute_ForwardsOnlyItsCatalogAndSection()
    {
        var source = new CatalogAuthoringFixture(CatalogAuthoringFixture.Read());
        using var context = CatalogAuthoringFixture.Context(source);
        context.Render<CatalogTemplatesPage>(parameters => parameters.Add(p => p.CatalogId, "open"));
        Assert.Equal(new CatalogAdminRequest("open") { Section = "templates" }, Assert.Single(source.Requests));
        Assert.Equal(new[] { "/catalogs/{CatalogId}/templates", "/catalogs/{CatalogId}/templates/{TemplateId}" },
            typeof(CatalogTemplatesPage).GetCustomAttributes(typeof(RouteAttribute), false).Cast<RouteAttribute>().Select(route => route.Template));
        Assert.Equal("/catalogs/{CatalogId}/items/{ItemId}", typeof(CatalogListingPage)
            .GetCustomAttributes(typeof(RouteAttribute), false).Cast<RouteAttribute>().Single().Template);
    }
}
