using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

public static class ConsoleShareLinkBuilder
{
    public static string BuildRelativeShareLink(ConsoleContentSummary item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var route = string.Equals(item.Type, "map", StringComparison.Ordinal)
            ? $"/maps/{SlugOrId(item)}"
            : $"/catalog/{SlugOrId(item)}";

        if (string.Equals(item.Access.Sharing, CatalogSharingTiers.PublicLink, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(item.Access.PublicLinkToken))
        {
            route = $"{route}?token={Uri.EscapeDataString(item.Access.PublicLinkToken)}";
        }

        return route;
    }

    private static string SlugOrId(ConsoleContentSummary item) =>
        string.IsNullOrWhiteSpace(item.Slug) ? item.Id : item.Slug;
}
