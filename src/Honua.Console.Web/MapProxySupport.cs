using System.Text.Json;
using System.Text.Json.Nodes;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Security;
using Honua.Console.Shell.Services;

namespace Honua.Console.Web;

// These endpoints forward only the active operator bearer, and a response must not be cached across operator sessions.
public static class MapProxySupport
{
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

    public static Uri BuildSceneAssetUri(Uri serverBaseUri, string sceneId, string assetPath)
    {
        ArgumentNullException.ThrowIfNull(serverBaseUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(sceneId);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);

        return ConsoleServerHttp.BuildUri(
            serverBaseUri,
            $"scenes/{sceneId}/{assetPath}");
    }

    // Neutralize CR/LF so a route or query value cannot forge extra log lines.
    public static string LogSafe(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.ReplaceLineEndings(" ");

    // Same session test the catalog read uses.
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

    // A mismatch is 409 so a view pinned to the previous profile cannot render the new profile's assets.
    public static async Task<(ConsoleEnvironmentProfile? Profile, IResult? Failure)> ResolvePinnedProfileAsync(
        IConsoleEnvironmentProfileStore profiles,
        string? environmentId,
        CancellationToken cancellationToken)
    {
        var activeProfile = await profiles.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (activeProfile is null)
        {
            return (null, Results.StatusCode(StatusCodes.Status503ServiceUnavailable));
        }

        if (string.IsNullOrWhiteSpace(environmentId)
            || !string.Equals(activeProfile.Id, environmentId, StringComparison.Ordinal))
        {
            return (null, Results.Json(
                new { message = "The active Honua environment changed. Reload this view.", reload = true },
                statusCode: StatusCodes.Status409Conflict));
        }

        return (activeProfile, null);
    }

    // Preserve the denial status and do not disclose the upstream body.
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

    // Rewrite style tile URLs through this proxy so the browser does not fetch honua-server directly.
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
