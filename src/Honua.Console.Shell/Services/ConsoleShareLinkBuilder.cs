using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

// This helper formats routes and does not mint share tokens; a public-link item without a token does not grant anonymous read.
public static class ConsoleShareLinkBuilder
{
    public static string BuildRelativeShareLink(ConsoleContentSummary item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var route = string.Equals(item.Type, "map", StringComparison.Ordinal)
            ? $"/maps/{SlugOrId(item)}"
            : $"/catalog/{SlugOrId(item)}";

        if (string.Equals(item.Access.Sharing, CatalogSharingTiers.PublicLink, StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(item.Access.PublicLinkToken))
            {
                throw new InvalidOperationException(
                    $"Content item '{SlugOrId(item)}' is shared as public-link but carries no public-link token; " +
                    "a tokenless public-link route would not grant anonymous read access.");
            }

            route = $"{route}?token={Uri.EscapeDataString(item.Access.PublicLinkToken)}";
        }

        return route;
    }

    private static string SlugOrId(ConsoleContentSummary item) =>
        string.IsNullOrWhiteSpace(item.Slug) ? item.Id : item.Slug;
}
