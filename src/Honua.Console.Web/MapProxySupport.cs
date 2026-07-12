using System.Text.Json;
using System.Text.Json.Nodes;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Security;
using Honua.Console.Shell.Services;

namespace Honua.Console.Web;

/// <summary>
/// Shared gating + rewrite helpers for the map-preview BFF proxy endpoints.
/// These endpoints inject the honua-server admin API key server-side, so they MUST only be
/// reachable by an authenticated console session — otherwise any client that can reach the
/// console origin can pull layer styles, vector tiles, and full feature rows with admin
/// privileges (honua-console#210, confused-deputy / broken access control).
/// </summary>
public static class MapProxySupport
{
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

    /// <summary>
    /// Resolves the active operator's forwardable bearer for a map-proxy upstream request
    /// (honua-console#233/#210). Returns the operator's real bearer when one exists so honua-server
    /// can scope the proxied style/tile/feature read to that operator's identity; returns
    /// <c>null</c> when only a non-forwardable session sentinel exists (the caller then falls back to
    /// the configured shared admin key). Operator authentication for the endpoint itself is enforced
    /// separately via <c>HttpContext.User</c>.
    /// </summary>
    public static async Task<string?> ResolveOperatorBearerAsync(
        IConsoleEnvironmentProfileStore profiles,
        IConsoleAccountSessionStore sessions,
        CancellationToken cancellationToken)
    {
        var activeProfile = await profiles.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (activeProfile is null || activeProfile.Account.AuthMode == ConsoleAccountAuthMode.Anonymous)
        {
            return null;
        }

        var session = await sessions.GetSessionAsync(activeProfile.Id, cancellationToken).ConfigureAwait(false);
        var token = session?.AccessToken;
        return ConsoleAuthConstants.IsSessionSentinel(token) ? null : token;
    }

    /// <summary>
    /// Attaches the operator's bearer to an upstream map-proxy request when one is available,
    /// otherwise the configured shared admin key (documented fallback). Centralises the
    /// "operator-first, admin-key-fallback" rule across the three proxy endpoints.
    /// </summary>
    public static void ApplyUpstreamCredential(HttpRequestMessage request, string? operatorBearer, string? adminApiKey)
    {
        if (!string.IsNullOrWhiteSpace(operatorBearer))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", operatorBearer);
            return;
        }

        if (!string.IsNullOrWhiteSpace(adminApiKey))
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", adminApiKey);
        }
    }

    // Validator request headers forwarded to the upstream so it can answer 304 Not Modified.
    private static readonly string[] ConditionalRequestHeaders = ["If-None-Match", "If-Modified-Since"];

    // Caching + validator response headers copied from the upstream so the browser can cache tiles.
    private static readonly string[] CacheResponseHeaders =
        ["Cache-Control", "ETag", "Expires", "Last-Modified", "Vary", "Age"];

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
    /// Copies the upstream caching + validator headers onto the proxied response so the browser can cache
    /// vector tiles instead of re-fetching every tile through this admin-keyed proxy on every view. When the
    /// upstream sends no <c>Cache-Control</c>, applies a conservative immutable long max-age (tiles are
    /// content-addressed by layer/z/x/y, so a given tile body is stable between publishes).
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

        if (!browserResponse.Headers.ContainsKey("Cache-Control"))
        {
            browserResponse.Headers["Cache-Control"] = "public, max-age=86400, immutable";
        }
    }

    /// <summary>
    /// Rewrites every vector-tile URL in a MapLibre style document so the browser fetches tiles
    /// back through this proxy (where the admin key is injected) rather than directly from
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
