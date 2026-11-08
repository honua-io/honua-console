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
                + CatalogComponentMarkup.Element("honua-catalog-search", CatalogId);
        }
    }
}

public sealed record CatalogAdminEmbed(string BundleHref, string? Integrity);

public static class CatalogAdminLinks
{
    public static string? TemplateHref(string catalogId, string templateId) =>
        catalogId is "" or "." or ".." || templateId is "" or "." or ".." ? null
        : $"/catalogs/{Uri.EscapeDataString(catalogId)}/templates/{Uri.EscapeDataString(templateId)}";

    public static string? SafeHref(string href) =>
        Uri.TryCreate(href, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0
        ? uri.AbsoluteUri : null;
}

public static class CatalogComponentMarkup
{
    public static string Element(string tag, string catalogId, string? identityAttribute = null)
    {
        var catalog = HtmlEncoder.Default.Encode(catalogId);
        var identity = identityAttribute is null ? string.Empty : $" {identityAttribute}=\"{{{{ item.id }}}}\"";
        return $"<{tag} catalog=\"{catalog}\"{identity}></{tag}>";
    }
}

public static class CatalogAdminLabels
{
    public static string Reported(string? value) => string.IsNullOrWhiteSpace(value) ? "Not reported" : value;
    public static string Join(IReadOnlyList<string> values) => values.Count == 0 ? "Not reported" : string.Join(", ", values);
    public static string YesNo(bool? value) => value switch { true => "Yes", false => "No", _ => "Not reported" };
    public static string Visibility(string? value) => value switch
    {
        "listed" => "Listed",
        "restricted" => "Audience-restricted",
        "hidden" => "Hidden here",
        "opted-out" => "Opted out",
        _ => "Not reported"
    };
    public static string TemplateState(string? state) => state switch
    {
        "draft" => "Draft",
        "released" => "Released",
        "in-review" => "In review",
        _ => "Not reported"
    };

    public static string Audience(string? audience) => audience switch
    {
        "public" => "Public",
        "signed-in" => "Signed-in users",
        "roles" => "Roles",
        _ => "Not reported"
    };
}
