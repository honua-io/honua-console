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
    public void EmbedRouteOptionsPreservePortalSnippetControls()
    {
        var portalDefault = EmbedRouteOptions.FromUri(
            "https://console.example/embed/maps/storm-response-map?chrome=minimal&legend=on&zoom=on#embedToken=abc-123");
        var noChrome = EmbedRouteOptions.FromUri(
            "https://console.example/embed/maps/storm-response-map?chrome=none&legend=off&zoom=off#embedToken=abc-123");

        Assert.True(portalDefault.Chrome);
        Assert.True(portalDefault.Legend);
        Assert.True(portalDefault.Zoom);
        Assert.False(noChrome.Chrome);
        Assert.False(noChrome.Legend);
        Assert.False(noChrome.Zoom);
    }

    [Theory]
    [InlineData("999,999,1000,1000")]
    [InlineData("10,0,5,5")]
    [InlineData("-158.3,21.8,-157.6,21.1")]
    [InlineData("-181,21,-157,22")]
    [InlineData("-158,-91,-157,22")]
    [InlineData("-158.3,21.1,-157.6,21.8,")]
    [InlineData("NaN,21,-157,22")]
    public void EmbedRouteOptionsRejectInvalidWgs84Extents(string extent)
    {
        var options = EmbedRouteOptions.FromUri(
            $"https://console.example/embed/maps/storm-response-map?extent={Uri.EscapeDataString(extent)}#embedToken=abc-123");

        Assert.Equal(string.Empty, options.Extent);
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
