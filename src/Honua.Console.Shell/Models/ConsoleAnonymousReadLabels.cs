using Honua.Console.Contracts;

namespace Honua.Console.Shell.Models;

public static class ConsoleAnonymousReadLabels
{
    public static string ReadLabel(ConsoleContentSummary item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return IsPublicLinkRead(item)
            ? "Public-link read"
            : "Public read";
    }

    public static string PermissionsHiddenMessage(ConsoleContentSummary item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return IsPublicLinkRead(item)
            ? "Anonymous public-link reads do not expose workspace permission details."
            : "Anonymous public reads do not expose workspace permission details.";
    }

    private static bool IsPublicLinkRead(ConsoleContentSummary item) =>
        string.Equals(item.Access.Sharing, CatalogSharingTiers.PublicLink, StringComparison.Ordinal);
}
