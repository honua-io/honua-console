using System.Globalization;
using System.Net;
using System.Text;
using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Builds the Console embed URL and copy-paste iframe snippet for an embeddable
/// map item. This mirrors the Portal <c>buildEmbedSnippet</c> contract
/// (<c>honua-portal:src/share/snippet.ts</c>): the embed bearer always travels in
/// the URL fragment (<c>#embedToken=</c>) and never the query string, so the token
/// is not logged by intermediaries or carried in referrers. The emitted URL
/// round-trips through <see cref="EmbedRouteOptions.FromUri(string)"/>.
/// </summary>
public static class ConsoleEmbedSnippetBuilder
{
    /// <summary>Default iframe width used by Portal embed snippets.</summary>
    public const int DefaultWidth = 800;

    /// <summary>Default iframe height used by Portal embed snippets.</summary>
    public const int DefaultHeight = 600;

    /// <summary>
    /// Builds the relative embed URL (path + query + fragment) for a map item.
    /// The embed token is placed in the fragment only.
    /// </summary>
    public static string BuildRelativeEmbedUrl(ConsoleContentSummary item, EmbedRouteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (!string.Equals(item.Type, "map", StringComparison.Ordinal))
        {
            throw new ArgumentException("Only map items are embeddable.", nameof(item));
        }

        var slugOrId = string.IsNullOrWhiteSpace(item.Slug) ? item.Id : item.Slug;
        var resolved = options ?? new EmbedRouteOptions();

        var query = new List<string>
        {
            $"chrome={(resolved.Chrome ? "true" : "false")}",
            $"legend={(resolved.Legend ? "on" : "off")}",
            $"zoom={(resolved.Zoom ? "on" : "off")}"
        };

        if (!string.IsNullOrWhiteSpace(resolved.Extent))
        {
            query.Add($"extent={Uri.EscapeDataString(resolved.Extent)}");
        }

        var url = new StringBuilder("/embed/maps/")
            .Append(Uri.EscapeDataString(slugOrId))
            .Append('?')
            .Append(string.Join('&', query));

        // The bearer is intentionally in the fragment, never the query string.
        var token = item.Access.EmbedToken;
        if (!string.IsNullOrWhiteSpace(token))
        {
            url.Append("#embedToken=").Append(Uri.EscapeDataString(token));
        }

        return url.ToString();
    }

    /// <summary>
    /// Builds the absolute embed URL by resolving the relative embed URL against
    /// the supplied Console origin (e.g. <c>https://console.honua.example</c>).
    /// </summary>
    public static string BuildAbsoluteEmbedUrl(ConsoleContentSummary item, Uri origin, EmbedRouteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(origin);

        var relative = BuildRelativeEmbedUrl(item, options);
        return new Uri(origin, relative).ToString();
    }

    /// <summary>
    /// Builds the copy-paste iframe HTML snippet recipients embed in a host page.
    /// Returns an empty string when the item is not embeddable.
    /// </summary>
    public static string BuildIframeSnippet(
        ConsoleContentSummary item,
        Uri origin,
        EmbedRouteOptions? options = null,
        int width = DefaultWidth,
        int height = DefaultHeight)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (!item.Access.Embeddable)
        {
            return string.Empty;
        }

        var src = BuildAbsoluteEmbedUrl(item, origin, options);
        var encodedSrc = WebUtility.HtmlEncode(src);
        var encodedTitle = WebUtility.HtmlEncode(
            string.IsNullOrWhiteSpace(item.Title) ? "Honua map embed" : item.Title);
        var w = width.ToString(CultureInfo.InvariantCulture);
        var h = height.ToString(CultureInfo.InvariantCulture);

        return $"<iframe src=\"{encodedSrc}\" title=\"{encodedTitle}\" width=\"{w}\" height=\"{h}\" "
            + "style=\"border:0\" loading=\"lazy\" referrerpolicy=\"no-referrer\" "
            + "allow=\"fullscreen\"></iframe>";
    }
}
