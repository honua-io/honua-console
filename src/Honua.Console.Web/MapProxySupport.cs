using System.Text.Json;
using System.Text.Json.Nodes;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Security;
using Honua.Console.Shell.Services;

namespace Honua.Console.Web;

/// <summary>
/// Shared gating + rewrite helpers for the map-preview BFF proxy endpoints.
/// These endpoints forward only the active operator bearer through the browser transport boundary.
/// Their responses must not be cached across operator sessions.
/// </summary>
public static class MapProxySupport
{
    /// <summary>Canonicalizes a relative asset path under a server-owned 3D Tiles scene.</summary>
    public static string? NormalizeSceneAssetPath(string? assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return null;
        }

        var segments = assetPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return null;
        }

        var encoded = new string[segments.Length];
        for (var index = 0; index < segments.Length; index++)
        {
            string segment;
            try
            {
                segment = Uri.UnescapeDataString(segments[index]).Trim();
            }
            catch (UriFormatException)
            {
                return null;
            }
            if (segment.Length == 0 || segment is "." or ".." || segment.Contains('/') || segment.Contains('\\'))
            {
                return null;
            }
            encoded[index] = Uri.EscapeDataString(segment);
        }
        return string.Join('/', encoded);
    }

    /// <summary>Builds a scene asset URI against the selected environment's server.</summary>
    public static Uri BuildSceneAssetUri(Uri serverBaseUri, string sceneId, string assetPath)
    {
        ArgumentNullException.ThrowIfNull(serverBaseUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(sceneId);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);

        return ConsoleServerHttp.BuildUri(
            serverBaseUri,
            $"scenes/{sceneId}/{assetPath}");
    }

    /// <summary>
    /// Neutralizes CR/LF in a user-provided value before it reaches a log entry, so a crafted
    /// route/query value cannot forge additional log lines (CodeQL cs/log-forging). Structured
    /// logging keeps the value as a property, but text-rendering sinks (console formatter, file
    /// exporters) would still emit embedded newlines verbatim.
    /// </summary>
    public static string LogSafe(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.ReplaceLineEndings(" ");

    /// <summary>
    /// True when there is an authenticated console session, using the same definition as
    /// <see cref="ConsoleCatalogReadContextResolver"/>: an active environment profile whose
    /// account is not Anonymous and that has a non-empty access token.
    /// </summary>
    public static async Task<bool> HasAuthenticatedConsoleSessionAsync(
        IConsoleEnvironmentProfileStore profiles,
        IConsoleAccountSessionStore sessions,
        CancellationToken cancellationToken)
    {
        var activeProfile = await profiles.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (activeProfile is null || activeProfile.Account.AuthMode == ConsoleAccountAuthMode.Anonymous)
        {
            return false;
        }

        var session = await sessions.GetSessionAsync(activeProfile.Id, cancellationToken).ConfigureAwait(false);
        return !string.IsNullOrWhiteSpace(session?.AccessToken);
    }

    /// <summary>Preserves server denial status without disclosing its response body.</summary>
    public static IResult UpstreamFailure(HttpContext context, System.Net.HttpStatusCode status)
    {
        if (status == System.Net.HttpStatusCode.Unauthorized)
        {
            context.Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\"";
            return Results.Json(new { message = "Sign in to honua-server again.", signIn = "/auth/signin" },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        return Results.StatusCode((int)status);
    }

    // Validator request headers forwarded to the upstream so it can answer 304 Not Modified.
    private static readonly string[] ConditionalRequestHeaders = ["If-None-Match", "If-Modified-Since"];

    // Caching + validator response headers copied from the upstream so the browser can cache tiles.
    private static readonly string[] CacheResponseHeaders =
        ["ETag", "Expires", "Last-Modified", "Vary", "Age"];

    /// <summary>
    /// Forwards the browser's cache-validation headers (<c>If-None-Match</c> / <c>If-Modified-Since</c>) onto
    /// the upstream tile request so honua-server can answer <c>304 Not Modified</c> and the browser
    /// revalidates cheaply instead of always re-downloading the tile body.
    /// </summary>
    public static void ForwardConditionalHeaders(HttpRequest browserRequest, HttpRequestMessage upstreamRequest)
    {
        foreach (var name in ConditionalRequestHeaders)
        {
            if (browserRequest.Headers.TryGetValue(name, out var values))
            {
                upstreamRequest.Headers.TryAddWithoutValidation(name, (IEnumerable<string?>)values);
            }
        }
    }

    /// <summary>
    /// Copies upstream validators onto the proxied response while forcing a private revalidation policy.
    /// These endpoints can fetch bytes using an operator identity, so a shared intermediary must not cache
    /// the response as public content.
    /// </summary>
    public static void ApplyTileCacheHeaders(HttpResponseMessage upstream, HttpResponse browserResponse)
    {
        foreach (var name in CacheResponseHeaders)
        {
            if (upstream.Headers.TryGetValues(name, out var values) ||
                upstream.Content.Headers.TryGetValues(name, out values))
            {
                browserResponse.Headers[name] = values.ToArray();
            }
        }

        // These proxy endpoints require an operator identity. Never preserve or invent a public
        // cache policy for bytes fetched with that identity; a shared intermediary must not reuse
        // one operator's response for another operator.
        browserResponse.Headers["Cache-Control"] = "private, no-cache, must-revalidate";
    }

    /// <summary>
    /// Rewrites every vector-tile URL in a MapLibre style document so the browser fetches tiles
    /// back through this proxy (where the operator bearer is attached) rather than directly from
    /// honua-server. Parses the style's <c>sources[*].tiles[]</c> entries and rewrites both
    /// root-relative (<c>/tiles/...</c>) and absolute (<c>http(s)://server/tiles/...</c>) URL
    /// shapes, replacing everything up to and including the <c>/tiles/</c> segment with the
    /// proxy base. Falls back to the verbatim input if the document is not parseable JSON.
    /// </summary>
    /// <param name="styleJson">The raw style document from honua-server.</param>
    /// <param name="proxyTileBase">Absolute proxy tile base, e.g. <c>https://host/map-proxy/tiles/</c>.</param>
    public static string RewriteTileUrls(string styleJson, string proxyTileBase)
    {
        if (string.IsNullOrWhiteSpace(styleJson))
        {
            return styleJson;
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(styleJson);
        }
        catch (JsonException)
        {
            // Not JSON we can reason about — leave it untouched rather than risk corruption.
            return styleJson;
        }

        if (root is not JsonObject styleObject ||
            styleObject["sources"] is not JsonObject sources)
        {
            return styleJson;
        }

        var rewrote = false;
        foreach (var source in sources)
        {
            if (source.Value is not JsonObject sourceObject ||
                sourceObject["tiles"] is not JsonArray tiles)
            {
                continue;
            }

            for (var i = 0; i < tiles.Count; i++)
            {
                if (tiles[i] is not JsonValue value || !value.TryGetValue<string>(out var url) ||
                    string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                if (TryRewriteTileUrl(url, proxyTileBase, out var rewritten))
                {
                    tiles[i] = rewritten;
                    rewrote = true;
                }
            }
        }

        return rewrote ? root!.ToJsonString() : styleJson;
    }

    private static bool TryRewriteTileUrl(string url, string proxyTileBase, out string rewritten)
    {
        rewritten = url;

        // Find the "/tiles/" segment regardless of whether the URL is root-relative or absolute.
        var marker = url.IndexOf("/tiles/", StringComparison.Ordinal);
        if (marker < 0)
        {
            return false;
        }

        // Everything after "/tiles/" is the tile path/template (e.g. "{layerId}/{z}/{x}/{y}.mvt").
        var suffix = url[(marker + "/tiles/".Length)..];
        rewritten = proxyTileBase + suffix;
        return true;
    }
}
