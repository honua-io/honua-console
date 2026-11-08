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
        Assert.Equal("Emergency management", read.Listing!.Title);
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
        Assert.Equal("Not reported", page.Find("#listing-can-appear").GetAttribute("value"));
        Assert.Equal("160", page.Find("#listing-summary").GetAttribute("maxlength"));
        page.Find("#listing-can-appear").Change("No");
        page.Find("#membership-category-1").Input("Local category");
        page.Find("#membership-featured-1").Change("Yes");
        page.Find("#membership-hidden-1").Change("Yes");
        Assert.Contains("Audience-restricted", page.Find(".catalog-memberships").TextContent);
        Assert.Null(read.Listing!.CanAppear);
        Assert.Null(read.Listing.Memberships[1].Featured);
        Assert.True(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
        page.Find("[data-listing-discard]").Click();
        Assert.Equal("Not reported", page.Find("#listing-can-appear").GetAttribute("value"));
        Assert.Equal("Not reported", page.Find("#membership-featured-1").GetAttribute("value"));
        Assert.False(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
    }

}
