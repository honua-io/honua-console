using System.Text.Json;
using System.Text.Json.Nodes;
using Honua.Console.Shell.Models;
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
