using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class CatalogAdminOverviewTests
{
    [Fact]
    public void Counts_AssociateEachValueWithItsKindAndVisibility()
    {
        using var context = Context(Overview([]));
        var page = context.Render<CatalogAdminPage>(parameters => parameters.Add(p => p.CatalogId, "open"));
        Assert.Equal(["6", "2", "1", "1", "10"], page.FindAll("dl dd").Select(element => element.TextContent));
        var row = page.Find(".catalog-kind-counts tbody tr");
        Assert.Equal(["Service", "3", "1", "0", "0", "4"], row.Children.Select(element => element.TextContent));
        Assert.Equal(["Kind", "Listed", "Audience-restricted", "Hidden here", "Opted out", "Matched"],
            page.FindAll(".catalog-kind-counts thead th").Select(element => element.TextContent));
    }

    [Fact]
    public void Attention_EqualDescriptionsRenderWithoutDuplicateKeys()
    {
        using var context = Context(Overview([new("Roads", "Missing description"), new("Roads", "Missing description")]));
        var page = context.Render<CatalogAdminPage>(parameters => parameters.Add(p => p.CatalogId, "open"));
        Assert.Equal(2, page.FindAll(".catalog-attention-list li").Count);
    }

    [Fact]
    public void Attention_PagesFiltersAndSortsWithoutMutations()
    {
        var attention = Enumerable.Range(1, 25).Reverse().Select(index => new CatalogAdminAttention(
            $"Item {index:00}", index == 25 ? "Missing owner" : "Missing description")).ToArray();
        using var context = Context(Overview(attention));
        var page = context.Render<CatalogAdminPage>(parameters => parameters.Add(p => p.CatalogId, "open"));
        Assert.Equal(12, page.FindAll(".catalog-attention-list li").Count);
        Assert.StartsWith("Item 01", page.Find(".catalog-attention-list li").TextContent);
        CatalogAdminPreview.Capture("overview", page.Markup);
        page.FindAll("nav[aria-label='Attention pages'] button")[1].Click();
        page.FindAll("nav[aria-label='Attention pages'] button")[1].Click();
        Assert.Single(page.FindAll(".catalog-attention-list li"));
        Assert.True(page.FindAll("nav[aria-label='Attention pages'] button")[1].HasAttribute("disabled"));
        page.Find("nav[aria-label='Attention pages'] button").Click();
        Assert.StartsWith("Item 13", page.Find(".catalog-attention-list li").TextContent);
        page.Find("#attention-sort").Change("issue");
        Assert.True(page.Find("nav[aria-label='Attention pages'] button").HasAttribute("disabled"));
        page.Find("#attention-issue").Change("Missing owner");
        Assert.Single(page.FindAll(".catalog-attention-list li"));
        Assert.Contains("Item 25", page.Find(".catalog-attention-list").TextContent);
        page.Find("#attention-query").Input("absent");
        Assert.Contains("No matching attention items", page.Markup);
        Assert.Empty(page.FindAll(".catalog-attention-list li"));
    }

    [Fact]
    public void Attention_IssueOrderingDiffersFromTitleOrdering()
    {
        using var context = Context(Overview([new("Alpha", "Zebra"), new("Beta", "Aardvark"), new("Gamma", "Middle")]));
        var page = context.Render<CatalogAdminPage>(parameters => parameters.Add(p => p.CatalogId, "open"));
        page.Find("#attention-sort").Change("issue");
        Assert.Equal(["Beta · Aardvark", "Gamma · Middle", "Alpha · Zebra"],
            page.FindAll(".catalog-attention-list li").Select(element => element.TextContent));
        page.Find("#attention-sort").Change("title");
        Assert.Equal(["Alpha · Zebra", "Beta · Aardvark", "Gamma · Middle"],
            page.FindAll(".catalog-attention-list li").Select(element => element.TextContent));
    }

    [Fact]
    public void Attention_EachFilterResetsItsOwnLaterPage()
    {
        var attention = Enumerable.Range(1, 36).Select(index => new CatalogAdminAttention(
            $"Item {index:00}", index <= 18 ? "Missing description" : "Missing owner")).ToArray();
        using var context = Context(Overview(attention));
        var page = context.Render<CatalogAdminPage>(parameters => parameters.Add(p => p.CatalogId, "open"));
        page.FindAll("nav[aria-label='Attention pages'] button")[1].Click();
        page.Find("#attention-issue").Change("Missing description");
        Assert.StartsWith("Item 01", page.Find(".catalog-attention-list li").TextContent);
        Assert.True(page.Find("nav[aria-label='Attention pages'] button").HasAttribute("disabled"));
        page.FindAll("nav[aria-label='Attention pages'] button")[1].Click();
        page.Find("#attention-query").Input("Item 0");
        Assert.Equal(9, page.FindAll(".catalog-attention-list li").Count);
        Assert.StartsWith("Item 01", page.Find(".catalog-attention-list li").TextContent);
        Assert.True(page.Find("nav[aria-label='Attention pages'] button").HasAttribute("disabled"));
    }

    private static CatalogAdminOverview Overview(IReadOnlyList<CatalogAdminAttention> attention) =>
        new("open", "Open", new(6, 2, 1, 1, 10), attention)
        {
            KindVisibility = [new("service", new(3, 1, 0, 0, 4))]
        };

    private static BunitContext Context(CatalogAdminOverview overview)
    {
        var context = new BunitContext();
        context.Services.AddSingleton(new CatalogAdminOptions { Enabled = true });
        context.Services.AddSingleton<ICatalogAdminSource>(new OverviewSource(overview));
        return context;
    }

    private sealed class OverviewSource(CatalogAdminOverview overview) : ICatalogAdminSource
    {
        public Task<CatalogAdminRead> ReadAsync(string? catalogId, CancellationToken cancellationToken) =>
            Task.FromResult(new CatalogAdminRead([], overview, "fixture", null));
    }
}
