using System.Text.Json.Nodes;
using Honua.Console.Web;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free coverage for the map-preview BFF proxy's style tile-URL rewrite
/// (<see cref="MapProxySupport.RewriteTileUrls"/>). The proxy must route every vector-tile URL in a
/// MapLibre style back through the console origin so the admin key is injected server-side and the
/// internal honua-server origin never leaks to the browser — including absolute URLs the server may
/// emit, not just root-relative "/tiles/..." (honua-console#213).
/// </summary>
public sealed class MapProxyTileRewriteTests
{
    private const string ProxyBase = "https://console.example/map-proxy/tiles/";

    [Fact]
    public void RewriteTileUrls_RootRelativeTileUrl_RoutesThroughProxy()
    {
        var style = """
            {"version":8,"sources":{"layer":{"type":"vector","tiles":["/tiles/7/{z}/{x}/{y}.mvt"]}},"layers":[]}
            """;

        var rewritten = MapProxySupport.RewriteTileUrls(style, ProxyBase);

        var tile = TileUrl(rewritten, "layer");
        Assert.Equal($"{ProxyBase}7/{{z}}/{{x}}/{{y}}.mvt", tile);
    }

    [Fact]
    public void RewriteTileUrls_AbsoluteServerTileUrl_RoutesThroughProxy()
    {
        // honua-server may emit fully-qualified tile URLs; these must not leak through to the browser.
        var style = """
            {"version":8,"sources":{"layer":{"type":"vector","tiles":["http://honua-server:8080/tiles/7/{z}/{x}/{y}.mvt"]}},"layers":[]}
            """;

        var rewritten = MapProxySupport.RewriteTileUrls(style, ProxyBase);

        var tile = TileUrl(rewritten, "layer");
        Assert.Equal($"{ProxyBase}7/{{z}}/{{x}}/{{y}}.mvt", tile);
        Assert.DoesNotContain("honua-server", rewritten, StringComparison.Ordinal);
    }

    [Fact]
    public void RewriteTileUrls_MultipleSources_RewritesEach()
    {
        var style = """
            {"version":8,"sources":{
              "a":{"type":"vector","tiles":["/tiles/1/{z}/{x}/{y}.mvt"]},
              "b":{"type":"vector","tiles":["https://srv/tiles/2/{z}/{x}/{y}.mvt"]}
            },"layers":[]}
            """;

        var rewritten = MapProxySupport.RewriteTileUrls(style, ProxyBase);

        Assert.Equal($"{ProxyBase}1/{{z}}/{{x}}/{{y}}.mvt", TileUrl(rewritten, "a"));
        Assert.Equal($"{ProxyBase}2/{{z}}/{{x}}/{{y}}.mvt", TileUrl(rewritten, "b"));
    }

    [Fact]
    public void RewriteTileUrls_NonTileSources_LeftUntouched()
    {
        var style = """
            {"version":8,"sources":{"img":{"type":"raster","url":"https://srv/wmts"}},"layers":[]}
            """;

        var rewritten = MapProxySupport.RewriteTileUrls(style, ProxyBase);

        Assert.Contains("https://srv/wmts", rewritten, StringComparison.Ordinal);
    }

    [Fact]
    public void RewriteTileUrls_NonJson_ReturnedVerbatim()
    {
        const string garbage = "not json at all";
        Assert.Equal(garbage, MapProxySupport.RewriteTileUrls(garbage, ProxyBase));
    }

    private static string TileUrl(string styleJson, string sourceId)
    {
        var node = JsonNode.Parse(styleJson)!;
        return (string)node["sources"]![sourceId]!["tiles"]![0]!;
    }
}
