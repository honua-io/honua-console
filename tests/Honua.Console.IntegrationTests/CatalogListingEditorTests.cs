using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class CatalogListingEditorTests
{
    [Fact]
    public void Listing_AllowsLocalEditsWithoutSavingOrChangingReportedState()
    {
        var read = CatalogAuthoringFixture.Read();
        using var context = CatalogAuthoringFixture.Context(read);
        var page = context.Render<CatalogListingPage>(p => p.Add(x => x.CatalogId, "open").Add(x => x.ItemId, "emergency"));
        page.Find("#listing-title").Input("Edited title");
        page.Find("#listing-summary").Input("Local summary");
        page.Find("#listing-description").Input("</textarea><script>alert(1)</script>");
        Assert.Contains("Unsaved local changes", page.Find(".catalog-listing-status").TextContent);
        Assert.True(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
        Assert.Equal("Emergency management", page.Find("[data-reported-title]").TextContent);
        Assert.Equal("Local summary".Length.ToString(), page.Find("[data-summary-count]").TextContent);
        Assert.Empty(page.FindAll(".catalog-listing script, .catalog-listing iframe, button[type=submit]"));
        CatalogAdminPreview.Capture("listing-dirty", page.Markup);
        page.Find("[data-listing-discard]").Click();
        Assert.Equal("Emergency management", page.Find("#listing-title").GetAttribute("value"));
        Assert.False(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
        Assert.DoesNotContain("saved", page.Find(".catalog-listing-status").TextContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Listing_ProtectsLocalEditsDuringNavigation()
    {
        using var context = CatalogAuthoringFixture.Context(CatalogAuthoringFixture.Read());
        var page = context.Render<CatalogListingPage>(p => p.Add(x => x.CatalogId, "open").Add(x => x.ItemId, "emergency"));
        page.Find("#listing-title").Input("Do not lose this");
        context.JSInterop.Setup<bool>("confirm", _ => true).SetResult(false);
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        await page.InvokeAsync(() => navigation.NavigateTo("/catalogs"));
        Assert.DoesNotContain("/catalogs", navigation.Uri);
        Assert.Equal("Do not lose this", page.Find("#listing-title").GetAttribute("value"));
    }
    [Theory]
    [InlineData("listing-tags")]
    [InlineData("listing-frequency")]
    [InlineData("listing-alt")]
    [InlineData("listing-owner")]
    [InlineData("listing-contact")]
    [InlineData("listing-review")]
    [InlineData("listing-deprecation")]
    [InlineData("listing-replacement")]
    public void EveryLocalTextField_IsTrackedAndRestored(string inputId)
    {
        using var context = CatalogAuthoringFixture.Context(CatalogAuthoringFixture.Read());
        var page = context.Render<CatalogListingPage>(p => p.Add(x => x.CatalogId, "open").Add(x => x.ItemId, "emergency"));
        var original = page.Find($"#{inputId}").GetAttribute("value") ?? string.Empty;
        page.Find($"#{inputId}").Input("Local value");
        Assert.True(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
        Assert.Equal("Local value", page.Find($"#{inputId}").GetAttribute("value"));
        page.Find("[data-listing-discard]").Click();
        Assert.Equal(original, page.Find($"#{inputId}").GetAttribute("value") ?? string.Empty);
        Assert.False(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
    }

    [Fact]
    public void UnknownFlagsAndMembershipEdits_StaySeparateFromReportedVisibility()
    {
        var read = CatalogAuthoringFixture.Read() with { Listing = CatalogAuthoringFixture.Listing() with { CanAppear = null } };
        using var context = CatalogAuthoringFixture.Context(read);
        var page = context.Render<CatalogListingPage>(p => p.Add(x => x.CatalogId, "open").Add(x => x.ItemId, "emergency"));
        Assert.Equal("", page.Find("#listing-can-appear").GetAttribute("value"));
        Assert.Equal("160", page.Find("#listing-summary").GetAttribute("maxlength"));
        page.Find("#listing-can-appear").Change("false");
        page.Find("#membership-category-1").Input("Local category");
        page.Find("#membership-featured-1").Change("true");
        page.Find("#membership-hidden-1").Change("true");
        Assert.Contains("Audience-restricted", page.Find(".catalog-memberships").TextContent);
        Assert.Equal("Can appear in catalogs: Not reported", page.Find(".catalog-can-appear").TextContent.Trim());
        Assert.Equal(new[] { "Not reported", "Not reported", "Not reported" }, page.FindAll(".catalog-memberships tbody tr")[1].Children.Skip(4).Select(cell => cell.TextContent.Trim()));
        Assert.True(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
        page.Find("[data-listing-discard]").Click();
        Assert.Equal("", page.Find("#listing-can-appear").GetAttribute("value"));
        Assert.Equal("", page.Find("#membership-featured-1").GetAttribute("value"));
        Assert.Equal("", page.Find("#membership-category-1").GetAttribute("value") ?? string.Empty);
        Assert.Equal("", page.Find("#membership-hidden-1").GetAttribute("value"));
        Assert.False(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
    }

    [Theory]
    [InlineData("listing-can-appear")]
    [InlineData("membership-featured-1")]
    [InlineData("membership-hidden-1")]
    public void EachNullableChoice_WorksIndependentlyAndCanReturnToUnreported(string id)
    {
        using var context = CatalogAuthoringFixture.Context(CatalogAuthoringFixture.Read() with
        { Listing = CatalogAuthoringFixture.Listing() with { CanAppear = null } });
        var page = context.Render<CatalogListingPage>(p => p.Add(x => x.CatalogId, "open").Add(x => x.ItemId, "emergency"));
        foreach (var choice in new[] { "true", "false" })
        {
            page.Find($"#{id}").Change(choice);
            page.Find("#listing-title").Input("Emergency management");
            Assert.Equal(choice, page.Find($"#{id}").GetAttribute("value"));
            Assert.True(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
            page.Find("[data-listing-discard]").Click();
            Assert.Equal("", page.Find($"#{id}").GetAttribute("value"));
            Assert.False(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
        }
        page.Find($"#{id}").Change("true");
        page.Find($"#{id}").Change("");
        Assert.False(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
    }

    [Fact]
    public void ClearingAnUnreportedCategory_RemovesDirtyState()
    {
        using var context = CatalogAuthoringFixture.Context(CatalogAuthoringFixture.Read());
        var page = context.Render<CatalogListingPage>(p => p.Add(x => x.CatalogId, "open").Add(x => x.ItemId, "emergency"));
        page.Find("#membership-category-1").Input("Local category");
        page.Find("#listing-title").Input("Emergency management");
        Assert.Equal("Local category", page.Find("#membership-category-1").GetAttribute("value"));
        Assert.True(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
        page.Find("#membership-category-1").Input("");
        Assert.False(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
    }

    [Fact]
    public void Discard_ReturnsFocusToTitleAndDescriptionHasAStableLabel()
    {
        using var context = CatalogAuthoringFixture.Context(CatalogAuthoringFixture.Read());
        var page = context.Render<CatalogListingPage>(p => p.Add(x => x.CatalogId, "open").Add(x => x.ItemId, "emergency"));
        var title = (AngleSharp.Dom.IElement)page.Find("#listing-title").Clone();
        page.Find("#listing-description").Input("</textarea><script>alert(1)</script>");
        Assert.Equal("Description source (Markdown)", page.Find("label[for=listing-description]").TextContent.Trim());
        var calls = context.JSInterop.Invocations.Count(call => call.Identifier == "Blazor._internal.domWrapper.focus");
        page.Find("[data-listing-discard]").Click();
        var focus = Assert.Single(context.JSInterop.Invocations.Where(call => call.Identifier == "Blazor._internal.domWrapper.focus").Skip(calls));
        focus.Arguments[0].ShouldBeElementReferenceTo(title);
    }

    [Fact]
    public void PresentReportedDescription_StaysReadOnlyAndInertDuringLocalEdits()
    {
        const string reported = "</textarea><script>alert(1)</script><img src=x onerror=alert(2)>";
        using var context = CatalogAuthoringFixture.Context(CatalogAuthoringFixture.Read() with
        { Listing = CatalogAuthoringFixture.Listing() with { Description = reported } });
        var page = context.Render<CatalogListingPage>(p => p.Add(x => x.CatalogId, "open").Add(x => x.ItemId, "emergency"));
        Assert.Equal(reported, page.Find("#reported-listing-description").TextContent);
        Assert.True(page.Find("#reported-listing-description").HasAttribute("readonly"));
        Assert.Equal(reported, page.Find("#listing-description").GetAttribute("value"));
        page.Find("#listing-description").Input("Local draft description");
        Assert.Equal(reported, page.Find("#reported-listing-description").TextContent);
        Assert.True(page.Find("#reported-listing-description").HasAttribute("readonly"));
        Assert.Equal("Local draft description", page.Find("#listing-description").GetAttribute("value"));
        Assert.Empty(page.FindAll("script, img, iframe, [onerror]"));
    }

}
