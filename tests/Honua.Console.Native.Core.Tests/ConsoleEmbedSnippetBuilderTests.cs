using Honua.Console.Contracts;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

public sealed class ConsoleEmbedSnippetBuilderTests
{
    private static readonly Uri Origin = new("https://console.honua.example");

    private static ConsoleContentSummary MapItem(
        string slug = "storm-response-map",
        bool embeddable = true,
        string token = "embed-storm-map") =>
        new()
        {
            Id = "m-1",
            Slug = slug,
            Title = "Storm Response Map",
            Type = "map",
            Access = new ConsoleShareAccess
            {
                Sharing = CatalogSharingTiers.PublicLink,
                Embeddable = embeddable,
                EmbedToken = token
            }
        };

    [Fact]
    public void RelativeEmbedUrlPlacesBearerInFragmentOnly()
    {
        var url = ConsoleEmbedSnippetBuilder.BuildRelativeEmbedUrl(MapItem());

        Assert.StartsWith("/embed/maps/storm-response-map?", url);
        Assert.Contains("#embedToken=embed-storm-map", url);

        // The bearer must never appear in the query string.
        var fragmentIndex = url.IndexOf('#', StringComparison.Ordinal);
        var query = url[..fragmentIndex];
        Assert.DoesNotContain("embedToken", query);
        Assert.DoesNotContain("token=", query);
    }

    [Fact]
    public void EmbedUrlRoundTripsThroughEmbedRouteOptions()
    {
        var options = new EmbedRouteOptions
        {
            Chrome = false,
            Legend = false,
            Zoom = true,
            Extent = "-158.3,21.1,-157.6,21.8"
        };

        var absolute = ConsoleEmbedSnippetBuilder.BuildAbsoluteEmbedUrl(MapItem(), Origin, options);
        var parsed = EmbedRouteOptions.FromUri(absolute);

        Assert.False(parsed.Chrome);
        Assert.False(parsed.Legend);
        Assert.True(parsed.Zoom);
        Assert.Equal("-158.3,21.1,-157.6,21.8", parsed.Extent);
        Assert.Equal("embed-storm-map", parsed.EmbedToken);
        Assert.False(parsed.QueryStringCarriedToken);
    }

    [Fact]
    public void AbsoluteEmbedUrlResolvesAgainstOrigin()
    {
        var url = ConsoleEmbedSnippetBuilder.BuildAbsoluteEmbedUrl(MapItem(), Origin);

        Assert.StartsWith("https://console.honua.example/embed/maps/storm-response-map?", url);
    }

    [Fact]
    public void IframeSnippetEmbedsResolvedUrlAndTitle()
    {
        var snippet = ConsoleEmbedSnippetBuilder.BuildIframeSnippet(MapItem(), Origin);

        Assert.Contains("<iframe", snippet);
        Assert.Contains("src=\"https://console.honua.example/embed/maps/storm-response-map?", snippet);
        Assert.Contains("title=\"Storm Response Map\"", snippet);
        Assert.Contains($"width=\"{ConsoleEmbedSnippetBuilder.DefaultWidth}\"", snippet);
        Assert.Contains($"height=\"{ConsoleEmbedSnippetBuilder.DefaultHeight}\"", snippet);
        Assert.Contains("referrerpolicy=\"no-referrer\"", snippet);
    }

    [Fact]
    public void IframeSnippetIsEmptyWhenNotEmbeddable()
    {
        var snippet = ConsoleEmbedSnippetBuilder.BuildIframeSnippet(MapItem(embeddable: false), Origin);

        Assert.Equal(string.Empty, snippet);
    }

    [Fact]
    public void RelativeEmbedUrlOmitsFragmentWhenNoToken()
    {
        var url = ConsoleEmbedSnippetBuilder.BuildRelativeEmbedUrl(MapItem(token: string.Empty));

        Assert.DoesNotContain("#", url);
    }

    [Fact]
    public void NonMapItemsAreRejected()
    {
        var layer = MapItem() with { Type = "layer" };

        Assert.Throws<ArgumentException>(() => ConsoleEmbedSnippetBuilder.BuildRelativeEmbedUrl(layer));
    }
}
