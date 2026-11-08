using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;

namespace Honua.Console.IntegrationTests;

public sealed class CatalogArrangementInteractionTests
{
    [Fact]
    public void HomeChoice_AloneMarksDirtyAndRestoresUnreported()
    {
        using var context = CatalogAuthoringFixture.Context(CatalogCategoriesTests.Read());
        var page = context.Render<CatalogCategoriesPage>(p => p.Add(x => x.CatalogId, "open"));
        page.Find("#category-home-1").Change("false");
        page.Find("#category-home-0").Change("true");
        Assert.Equal("false", page.Find("#category-home-1").GetAttribute("value"));
        Assert.True(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
        page.Find("#category-home-1").Change("");
        Assert.False(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
        page.Find("#category-home-1").Change("false");
        page.Find("[data-arrangement-discard]").Click();
        Assert.Equal("", page.Find("#category-home-1").GetAttribute("value"));
        Assert.False(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
    }

    [Theory]
    [InlineData("up", 1, "down")]
    [InlineData("down", 3, "up")]
    [InlineData("up", 3, "up")]
    [InlineData("down", 1, "down")]
    public void Move_PreservesAnEnabledButtonAndAnnouncesPosition(string direction, int index, string focusDirection)
    {
        using var context = CatalogAuthoringFixture.Context(CatalogCategoriesTests.Read());
        var page = context.Render<CatalogCategoriesPage>(p => p.Add(x => x.CatalogId, "open"));
        var row = page.FindAll(".catalog-featured-order tbody tr")[index];
        var target = (AngleSharp.Dom.IElement)row.QuerySelector($"[data-move-{focusDirection}]")!.Clone();
        var previousCalls = context.JSInterop.Invocations.Count(call => call.Identifier == "Blazor._internal.domWrapper.focus");
        row.QuerySelector($"[data-move-{direction}]")!.Click();
        var destination = index + (direction == "up" ? -1 : 1);
        Assert.Equal($"Item {index + 1}", page.FindAll(".catalog-featured-order tbody tr")[destination].QuerySelector("th")!.TextContent);
        Assert.Contains($"Item {index + 1} moved to position {destination + 1} of 5", page.Find(".catalog-arrangement-status").TextContent);
        var focus = Assert.Single(context.JSInterop.Invocations.Where(call => call.Identifier == "Blazor._internal.domWrapper.focus").Skip(previousCalls));
        focus.Arguments[0].ShouldBeElementReferenceTo(target);
    }

    [Fact]
    public void Discard_ReturnsFocusToTheHeading()
    {
        using var context = CatalogAuthoringFixture.Context(CatalogCategoriesTests.Read());
        var page = context.Render<CatalogCategoriesPage>(p => p.Add(x => x.CatalogId, "open"));
        var target = (AngleSharp.Dom.IElement)page.Find("#catalog-categories-heading").Clone();
        page.Find(".catalog-category-order tbody tr:last-child [data-move-up]").Click();
        Assert.Equal("Roads", page.Find(".catalog-category-order tbody tr:first-child th").TextContent);
        var previousCalls = context.JSInterop.Invocations.Count(call => call.Identifier == "Blazor._internal.domWrapper.focus");
        page.Find("[data-arrangement-discard]").Click();
        var focus = Assert.Single(context.JSInterop.Invocations.Where(call => call.Identifier == "Blazor._internal.domWrapper.focus").Skip(previousCalls));
        focus.Arguments[0].ShouldBeElementReferenceTo(target);
    }

    [Theory]
    [InlineData("duplicate-category")]
    [InlineData("duplicate-featured")]
    [InlineData("blank-category")]
    [InlineData("blank-featured")]
    public void AmbiguousIdentities_HaveNoOrderingControls(string scenario)
    {
        var arrangement = scenario switch
        {
            "duplicate-category" => new CatalogArrangementDocument("open", [new("same", "One", "Rule"), new("same", "Two", "Rule")], []),
            "duplicate-featured" => new("open", [], [new("same", "One"), new("same", "Two")]),
            "blank-category" => new("open", [new(" ", "One", "Rule")], []),
            _ => new("open", [], [new(" ", "One")])
        };
        using var context = CatalogAuthoringFixture.Context(CatalogCategoriesTests.Read() with { Arrangement = arrangement });
        var page = context.Render<CatalogCategoriesPage>(p => p.Add(x => x.CatalogId, "open"));
        Assert.Contains("Ordering is unavailable", page.Markup);
        Assert.Empty(page.FindAll(".catalog-order-controls"));
    }

    [Fact]
    public void HostileIdentity_UsesAStableLabelledControl()
    {
        using var context = CatalogAuthoringFixture.Context(CatalogCategoriesTests.Read() with
        { Arrangement = new("open", [new("public safety & maps", "Public safety", "Rule")], []) });
        var page = context.Render<CatalogCategoriesPage>(p => p.Add(x => x.CatalogId, "open"));
        Assert.Equal("Show Public safety on home page", page.Find("#category-home-0").GetAttribute("aria-label"));
        Assert.DoesNotContain(" ", page.Find("#category-home-0").Id);
    }
}
