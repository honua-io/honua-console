using System.Text.Encodings.Web;
using System.Text.RegularExpressions;

namespace Honua.Console.Shell.Models;

public sealed record CatalogAdminSettings(string CatalogId, string Title, string Audience)
{
    public string? Description { get; init; }
    public IReadOnlyList<string> RoleIds { get; init; } = [];
    public IReadOnlyList<string> FolderIds { get; init; } = [];
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<string> Kinds { get; init; } = [];
    public string? DefaultListing { get; init; }
    public string? LightAccent { get; init; }
    public string? DarkAccent { get; init; }
    public string? HostName { get; init; }
    public IReadOnlyList<string> AllowedEmbedOrigins { get; init; } = [];
    public IReadOnlyList<string> Sections { get; init; } = [];
    public IReadOnlyList<string> FeedFormats { get; init; } = [];
    public bool? Indexing { get; init; }
    public CatalogAdminEmbed? Embed { get; init; }

    public string? EmbedSnippet
    {
        get
        {
            if (Embed is not { } embed || CatalogAdminLinks.SafeHref(embed.BundleHref) is not { } href
                || embed.Integrity is not { } integrity || !Regex.IsMatch(integrity, @"\Asha384-[A-Za-z0-9+/]{64}\z")) return null;
            var encoder = HtmlEncoder.Default;
            return $"<script type=\"module\" src=\"{encoder.Encode(href)}\" integrity=\"{integrity}\" crossorigin=\"anonymous\"></script>\n"
                + $"<honua-catalog-search catalog=\"{encoder.Encode(CatalogId)}\"></honua-catalog-search>";
        }
    }
}

public sealed record CatalogAdminEmbed(string BundleHref, string? Integrity);

public static class CatalogAdminLinks
{
    public static string? SafeHref(string href) =>
        Uri.TryCreate(href, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0
        ? uri.AbsoluteUri : null;
}

public static class CatalogAdminLabels
{
    public static string Audience(string audience) => audience switch
    {
        "public" => "Public",
        "signed-in" => "Signed-in users",
        "roles" => "Roles",
        _ => "Not reported"
    };
}
