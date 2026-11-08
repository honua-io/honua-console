using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class CatalogCategoriesTests
{
    [Fact]
    public void MissingCategories_DoNotFallBackToOverview()
    {
        using var context = CatalogAuthoringFixture.Context(CatalogAuthoringFixture.Read());
        var page = context.Render<CatalogCategoriesPage>(p => p.Add(x => x.CatalogId, "open"));
        Assert.Contains("Categories and featured are unavailable", page.Markup);
        Assert.DoesNotContain("Admin visibility counts", page.Markup);
    }

    [Fact]
    public void Route_ForwardsIdentityAndMarksFixturePreview()
    {
        var source = new CatalogAuthoringFixture(Read());
        using var context = CatalogAuthoringFixture.Context(source);
        var page = context.Render<CatalogCategoriesPage>(p => p.Add(x => x.CatalogId, "open"));
        var request = Assert.Single(source.Requests);
        Assert.Equal("open", request.CatalogId);
        Assert.Equal("categories", request.Section);
        Assert.Equal("Categories and featured", page.Find("[aria-current=page]").TextContent);
        Assert.Contains("Fixture preview", page.Markup);
        CatalogAdminPreview.Capture("categories", page.Markup);
    }

    [Fact]
    public void DisabledCategories_DoNotReadOrShowControls()
    {
        var source = new CatalogAuthoringFixture(Read());
        using var context = CatalogAuthoringFixture.Context(source);
        context.Services.AddSingleton(new CatalogAdminOptions());
        var page = context.Render<CatalogCategoriesPage>(p => p.Add(x => x.CatalogId, "open"));
        Assert.Empty(source.Requests);
        Assert.Empty(page.FindAll(".catalog-categories"));
    }

    [Fact]
    public void EmptyCategories_AreDifferentFromUnsupported()
    {
        using var context = CatalogAuthoringFixture.Context(Read() with { Arrangement = new("open", [], []) });
        var page = context.Render<CatalogCategoriesPage>(p => p.Add(x => x.CatalogId, "open"));
        Assert.Contains("No categories reported", page.Markup);
        Assert.Contains("No featured items reported", page.Markup);
        Assert.DoesNotContain("are unavailable", page.Markup);
    }

    [Fact]
    public void WrongCatalog_DoesNotExposeArrangement()
    {
        using var context = CatalogAuthoringFixture.Context(Read() with { Arrangement = new("staff", [new("secret", "Private category", "Secret rule")], []) });
        var page = context.Render<CatalogCategoriesPage>(p => p.Add(x => x.CatalogId, "open"));
        Assert.DoesNotContain("Private", page.Markup);
        Assert.Contains("are unavailable", page.Markup);
    }

    [Fact]
    public void ReorderingAndHomeChoices_AreLocalGuardedAndDiscardable()
    {
        var read = Read();
        using var context = CatalogAuthoringFixture.Context(read);
        var page = context.Render<CatalogCategoriesPage>(p => p.Add(x => x.CatalogId, "open"));
        Assert.True(page.Find(".catalog-category-order tbody tr:first-child button[data-move-up]").HasAttribute("disabled"));
        Assert.True(page.Find(".catalog-category-order tbody tr:last-child button[data-move-down]").HasAttribute("disabled"));
        page.Find(".catalog-category-order tbody tr:first-child button[data-move-down]").Click();
        Assert.Equal("Roads", page.Find(".catalog-category-order tbody tr:first-child th").TextContent);
        page.Find("#category-home-0").Change("false");
        page.Find(".catalog-featured-order tbody tr:first-child button[data-move-down]").Click();
        Assert.Equal("Item 2", page.Find(".catalog-featured-order tbody tr:first-child th").TextContent);
        Assert.Equal("Safety", read.Arrangement!.Categories[0].Title);
        Assert.Equal("Item 1", read.Arrangement.Featured[0].Title);
        Assert.True(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
        Assert.Contains("Unsaved local changes", page.Find(".catalog-arrangement-status").TextContent);
        CatalogAdminPreview.Capture("categories-dirty", page.Markup);
        page.Find("[data-arrangement-discard]").Click();
        Assert.Equal("Safety", page.Find(".catalog-category-order tbody tr:first-child th").TextContent);
        Assert.Equal("Item 1", page.Find(".catalog-featured-order tbody tr:first-child th").TextContent);
        Assert.False(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
        Assert.Empty(page.FindAll("button[type=submit]"));
    }

    [Fact]
    public void FeaturedPositions_DoNotClaimAudienceVisibilityOrInventHealth()
    {
        using var context = CatalogAuthoringFixture.Context(Read());
        var page = context.Render<CatalogCategoriesPage>(p => p.Add(x => x.CatalogId, "open"));
        Assert.Equal(4, page.FindAll("[data-home-position]").Count);
        Assert.Contains("All featured", page.Find(".catalog-featured-order tbody tr:last-child").TextContent);
        Assert.Contains("Not reported", page.Find(".catalog-featured-order tbody tr:first-child").TextContent);
        Assert.Contains("visitor's access", page.Markup);
        Assert.DoesNotContain("Operational", page.Markup);
    }

    [Fact]
    public void HostileTitlesAndRules_RemainText()
    {
        using var context = CatalogAuthoringFixture.Context(Read() with
        { Arrangement = new("open", [new("safe", "<img src=x onerror=alert(1)>", "<script>alert(2)</script>")], []) });
        var page = context.Render<CatalogCategoriesPage>(p => p.Add(x => x.CatalogId, "open"));
        Assert.Contains("<img", page.Find(".catalog-category-order th[scope=row]").TextContent);
        Assert.Empty(page.FindAll("script, img, [onerror]"));
    }

    [Fact]
    public void DuplicateRowIdentity_IsUnavailableInsteadOfCrashing()
    {
        using var context = CatalogAuthoringFixture.Context(Read() with
        { Arrangement = new("open", [new("same", "One", "Rule"), new("same", "Two", "Rule")], []) });
        var page = context.Render<CatalogCategoriesPage>(p => p.Add(x => x.CatalogId, "open"));
        Assert.Contains("Ordering is unavailable", page.Markup);
        Assert.Empty(page.FindAll(".catalog-category-order"));
    }

    internal static CatalogAdminRead Read() => CatalogAuthoringFixture.Read() with
    {
        Arrangement = new("open", [new("safety", "Safety", "Folder Public Safety") { ItemCount = 12, ShowOnHome = true }, new("roads", "Roads", "By hand")],
            Enumerable.Range(1, 5).Select(index => new CatalogFeaturedRow($"item-{index}", $"Item {index}") { Kind = "App" }).ToArray())
    };
}
