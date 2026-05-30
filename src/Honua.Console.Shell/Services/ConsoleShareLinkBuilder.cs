using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Builds the relative, same-origin share link Console exposes for a content item.
/// </summary>
/// <remarks>
/// Pure, server-independent routing helper for the Share slice (honua-console#34). It only
/// formats routes from a projected <see cref="ConsoleContentSummary"/>; it does not mint or
/// validate share tokens (that is server-owned). The public-link token contract is binding:
/// a <see cref="CatalogSharingTiers.PublicLink"/> item must carry a non-empty
/// <see cref="ConsoleShareAccess.PublicLinkToken"/>, since a tokenless public-link route
/// would not grant anonymous read access.
/// </remarks>
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
