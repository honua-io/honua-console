using System.Net.Http.Headers;
using Honua.Console.Web;
using Microsoft.AspNetCore.Http;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free coverage for the map-preview tile proxy's caching behavior
/// (<see cref="MapProxySupport.ApplyTileCacheHeaders"/> and
/// <see cref="MapProxySupport.ForwardConditionalHeaders"/>). The proxy is the hottest path, so it must
/// forward the upstream caching/validator headers (and the browser's conditional headers) rather than
/// stripping them and forcing every tile to be re-fetched through the admin-keyed proxy (honua-console#236).
/// </summary>
public sealed class MapProxyCacheHeaderTests
{
    [Fact]
    public void ApplyTileCacheHeaders_CopiesUpstreamCachingAndValidators()
    {
        using var upstream = new HttpResponseMessage();
        upstream.Headers.CacheControl = new CacheControlHeaderValue { Public = true, MaxAge = TimeSpan.FromHours(2) };
        upstream.Headers.ETag = new EntityTagHeaderValue("\"abc123\"");

        var response = new DefaultHttpContext().Response;
        MapProxySupport.ApplyTileCacheHeaders(upstream, response);

        Assert.Equal("private, no-cache, must-revalidate", response.Headers.CacheControl.ToString());
        Assert.Equal("\"abc123\"", response.Headers.ETag.ToString());
    }

    [Fact]
    public void ApplyTileCacheHeaders_NoUpstreamCacheControl_AppliesPrivateDefault()
    {
        using var upstream = new HttpResponseMessage();

        var response = new DefaultHttpContext().Response;
        MapProxySupport.ApplyTileCacheHeaders(upstream, response);

        Assert.Equal("private, no-cache, must-revalidate", response.Headers.CacheControl.ToString());
    }

    [Fact]
    public void ForwardConditionalHeaders_CopiesBrowserValidatorsOntoUpstreamRequest()
    {
        var browser = new DefaultHttpContext().Request;
        browser.Headers["If-None-Match"] = "\"abc123\"";

        using var upstreamRequest = new HttpRequestMessage();
        MapProxySupport.ForwardConditionalHeaders(browser, upstreamRequest);

        Assert.True(upstreamRequest.Headers.TryGetValues("If-None-Match", out var values));
        Assert.Contains("\"abc123\"", values!);
    }
}
