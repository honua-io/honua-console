using Honua.Console.Contracts;

namespace Honua.Console.Native.Core.Tests;

public sealed class CatalogSearchStateTests
{
    [Fact]
    public void CatalogSearchAcceptsOnlyPortalQueryKeys()
    {
        var state = CatalogSearchState.FromUri(
            "https://console.example/catalog?q=flood&type=service&tag=coastal&owner=resilience&visibility=public-link&sort=modified-desc&cursor=abc&sharing=public&foo=bar");

        Assert.Equal("flood", state.Query);
        Assert.Equal("service", state.Type);
        Assert.Equal("coastal", state.Tag);
        Assert.Equal("resilience", state.Owner);
        Assert.Equal("public-link", state.Visibility);
        Assert.Equal("modified-desc", state.Sort);
        Assert.Equal("abc", state.Cursor);
        Assert.Equal(["foo", "sharing"], state.IgnoredQueryKeys);
        Assert.DoesNotContain("sharing", state.ToPortalQueryParameters().Keys);
        Assert.DoesNotContain("foo", state.ToPortalQueryParameters().Keys);
    }

    [Fact]
    public void CatalogSearchMapsVisibilityToSdkSharingField()
    {
        var request = CatalogSearchState
            .FromUri("https://console.example/catalog?visibility=public&type=map&sort=title-asc")
            .ToListRequest();

        Assert.Equal("public", request.Sharing);
        Assert.Equal("map", request.Type);
        Assert.Equal("title-asc", request.Sort);

        var sdkParameters = request.ToSdkParameters();
        Assert.Equal("public", sdkParameters["sharing"]);
        Assert.Equal("map", sdkParameters["type"]);
        Assert.DoesNotContain("visibility", sdkParameters.Keys);
    }

    [Fact]
    public void CatalogSearchNormalizesInvalidEnumValues()
    {
        var state = CatalogSearchState.FromUri(
            "https://console.example/catalog?type=unknown&visibility=internet&sort=random&q=water");

        Assert.Equal("water", state.Query);
        Assert.Equal(string.Empty, state.Type);
        Assert.Equal(string.Empty, state.Visibility);
        Assert.Equal(CatalogSortOptions.Relevance, state.Sort);
        Assert.Equal(["q"], state.ToPortalQueryParameters().Keys);
    }

    [Fact]
    public void EmbedRouteOptionsKeepBearerInFragmentOnly()
    {
        var options = EmbedRouteOptions.FromUri(
            "https://console.example/embed/maps/storm-response-map?chrome=false&legend=0&zoom=true&extent=-158.3,21.1,-157.6,21.8#embedToken=abc-123");

        Assert.False(options.Chrome);
        Assert.False(options.Legend);
        Assert.True(options.Zoom);
        Assert.Equal("-158.3,21.1,-157.6,21.8", options.Extent);
        Assert.Equal("abc-123", options.EmbedToken);
        Assert.False(options.QueryStringCarriedToken);
    }

    [Fact]
    public void EmbedRouteOptionsDetectQueryStringBearerRegression()
    {
        var options = EmbedRouteOptions.FromUri(
            "https://console.example/embed/maps/storm-response-map?embedToken=abc-123#embedToken=fragment-token");

        Assert.True(options.QueryStringCarriedToken);
        Assert.Equal("fragment-token", options.EmbedToken);
    }
}
