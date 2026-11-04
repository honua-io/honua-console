using System.Globalization;
using System.Net;
using System.Text;
using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

// The embed bearer travels in the embedToken URL fragment and never the query string, so intermediaries and referrers do not record it.
public static class ConsoleEmbedSnippetBuilder
{
    public const int DefaultWidth = 800;

    public const int DefaultHeight = 600;

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

    public static string BuildAbsoluteEmbedUrl(ConsoleContentSummary item, Uri origin, EmbedRouteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(origin);

        var relative = BuildRelativeEmbedUrl(item, options);
        return new Uri(origin, relative).ToString();
    }

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
