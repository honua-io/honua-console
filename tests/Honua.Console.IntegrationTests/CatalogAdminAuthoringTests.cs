using Bunit;
using Honua.Console.Shell;
using Honua.Console.Shell.DependencyInjection;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class CatalogAdminAuthoringTests
{
    [Fact]
    public void Templates_ReportsMissingProjectionInsteadOfShowingOverview()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton(new CatalogAdminOptions { Enabled = true });
        context.Services.AddSingleton<ICatalogAdminSource>(new EmptyAuthoringSource());
        var page = context.Render<CatalogAdminPage>(parameters => parameters
            .Add(p => p.CatalogId, "open").Add(p => p.Section, "templates"));
        Assert.Contains("Templates are unavailable", page.Markup);
        Assert.DoesNotContain("Admin visibility counts", page.Markup);
    }

    [Fact]
    public void Templates_ShowsReportedVersionsAndFiltersWithoutInventingReleaseActions()
    {
        using var context = CatalogAuthoringFixture.Context(CatalogAuthoringFixture.Read());
        var page = context.Render<CatalogTemplatesPage>(parameters => parameters.Add(p => p.CatalogId, "open"));
        Assert.Equal("Templates", page.Find("[aria-current='page']").TextContent);
        Assert.Equal(2, page.FindAll(".catalog-templates tbody tr").Count);
        Assert.Contains("Draft", page.Find(".catalog-templates").TextContent);
        Assert.Contains("v3", page.Find(".catalog-templates").TextContent);
        Assert.Contains("Not reported", page.Find(".catalog-templates").TextContent);
        Assert.Equal("/catalogs/open/templates/service%20%26%20map", page.Find(".catalog-templates tbody a").GetAttribute("href"));
        Assert.Empty(page.FindAll("button[type='submit']"));
        CatalogAdminPreview.Capture("templates", page.Markup);
        page.Find("#template-query").Input("unmatched");
        Assert.Contains("No matching templates", page.Markup);
        page.Find("#template-query").Input("Home");
        Assert.Single(page.FindAll(".catalog-templates tbody tr"));
    }

    [Fact]
    public void Templates_DistinguishesEmptyFromMissing()
    {
        using var context = CatalogAuthoringFixture.Context(CatalogAuthoringFixture.Read() with { Templates = new("open", []) });
        var page = context.Render<CatalogTemplatesPage>(parameters => parameters.Add(p => p.CatalogId, "open"));
        Assert.Contains("No templates reported", page.Markup);
        Assert.DoesNotContain("Templates are unavailable", page.Markup);
    }

    [Theory]
    [InlineData("templates")]
    [InlineData("template")]
    [InlineData("listing")]
    public void Authoring_RejectsUnrelatedProjections(string section)
    {
        var read = CatalogAuthoringFixture.Read() with
        {
            Templates = new("staff", [new("private", "Private template", "home")]),
            Template = CatalogAuthoringFixture.Template() with { CatalogId = "staff", Title = "Private source" },
            Listing = CatalogAuthoringFixture.Listing() with { ItemId = "private", Title = "Private listing" }
        };
        using var context = CatalogAuthoringFixture.Context(read);
        var page = context.Render<CatalogAdminPage>(parameters => parameters.Add(p => p.CatalogId, "open")
            .Add(p => p.Section, section).Add(p => p.TemplateId, "service & map").Add(p => p.ItemId, "emergency"));
        Assert.Contains("unavailable", page.Markup);
        Assert.DoesNotContain("Private", page.Markup);
        Assert.Empty(page.FindAll("textarea"));
    }

    [Fact]
    public void DisabledAuthoring_DoesNotReadTheSource()
    {
        var source = new CatalogAuthoringFixture(CatalogAuthoringFixture.Read());
        using var context = CatalogAuthoringFixture.Context(source);
        context.Services.AddSingleton(new CatalogAdminOptions());
        var page = context.Render<CatalogTemplatesPage>(parameters => parameters.Add(p => p.CatalogId, "open"));
        Assert.Empty(source.Requests);
        Assert.Contains("not enabled", page.Markup);
        Assert.Empty(page.FindAll(".catalog-templates, textarea"));
    }

    [Theory]
    [InlineData(null, "missing")]
    [InlineData("https://server.example/", "unsupported")]
    public void OrdinaryHostRegistration_NeverEnablesAuthoringFixtures(string? server, string state)
    {
        using var context = new BunitContext();
        context.Services.AddHonuaConsoleShell(new HonuaConsoleShellOptions { ServerBaseUrl = server, CatalogAdminEnabled = true });
        var page = context.Render<CatalogTemplatesPage>(parameters => parameters.Add(p => p.CatalogId, "open").Add(p => p.TemplateId, "service"));
        Assert.Single(page.FindAll($".console-state-{state}"));
        Assert.DoesNotContain("Fixture preview", page.Markup);
        Assert.Empty(page.FindAll("textarea, .catalog-template-editor"));
    }

    [Fact]
    public void TemplateRoute_ForwardsEveryIdentityToThePresentationSource()
    {
        var source = new CatalogAuthoringFixture(CatalogAuthoringFixture.Read());
        using var context = CatalogAuthoringFixture.Context(source);
        context.Render<CatalogTemplatesPage>(parameters => parameters.Add(p => p.CatalogId, "open")
            .Add(p => p.TemplateId, "service & map"));
        var request = Assert.Single(source.Requests);
        Assert.Equal("open", request.CatalogId);
        Assert.Equal("service & map", request.TemplateId);
        Assert.Equal("template", request.Section);
        Assert.Null(request.ItemId);
    }

    [Fact]
    public void ListingRoute_ForwardsTheItemIdentity()
    {
        var source = new CatalogAuthoringFixture(CatalogAuthoringFixture.Read());
        using var context = CatalogAuthoringFixture.Context(source);
        context.Render<CatalogListingPage>(parameters => parameters.Add(p => p.CatalogId, "open").Add(p => p.ItemId, "emergency"));
        var request = Assert.Single(source.Requests);
        Assert.Equal("emergency", request.ItemId);
        Assert.Equal("listing", request.Section);
        Assert.Null(request.TemplateId);
    }

    [Fact]
    public void Listing_IsReadOnlyAndKeepsMembershipAccessSeparateFromEndpointAccess()
    {
        using var context = CatalogAuthoringFixture.Context(CatalogAuthoringFixture.Read());
        var page = context.Render<CatalogListingPage>(parameters => parameters.Add(p => p.CatalogId, "open").Add(p => p.ItemId, "emergency"));
        Assert.Contains("Endpoint access does not change", page.Markup);
        Assert.True(page.Find("#listing-can-appear").HasAttribute("disabled"));
        Assert.Contains("Folder Public Safety", page.Find(".catalog-memberships").TextContent);
        Assert.Contains("Audience-restricted", page.Find(".catalog-memberships").TextContent);
        Assert.Contains("from folder Public Safety", page.Markup);
        Assert.Contains("extent and style", page.Markup);
        Assert.Contains("Evacuation map", page.Markup);
        Assert.Contains("Visitor preview is unavailable", page.Markup);
        Assert.Empty(page.FindAll("iframe, script, img, button[type='submit']"));
        CatalogAdminPreview.Capture("listing", page.Markup);
    }

    [Fact]
    public void Listing_EscapesDescriptionAndDoesNotClaimUnreportedObservations()
    {
        var read = CatalogAuthoringFixture.Read() with
        {
            Listing = CatalogAuthoringFixture.Listing() with { Description = "<img src=x onerror=alert(1)>", Checks = [], RelatedTitles = [] }
        };
        using var context = CatalogAuthoringFixture.Context(read);
        var page = context.Render<CatalogListingPage>(parameters => parameters.Add(p => p.CatalogId, "open").Add(p => p.ItemId, "emergency"));
        Assert.Contains("<img src=x onerror=alert(1)>", page.Find("#listing-description").TextContent);
        Assert.Empty(page.FindAll("img, script, [onerror]"));
        Assert.Contains("Listing checks are not reported", page.Markup);
        Assert.DoesNotContain("Operational", page.Markup);
    }

    private sealed class EmptyAuthoringSource : ICatalogAdminSource
    {
        public Task<CatalogAdminRead> ReadAsync(CatalogAdminRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new CatalogAdminRead([], new("open", "Open", new(0, 0, 0, 0, 0), []), "fixture", null));
    }
}
