using Honua.Console.Contracts;

namespace Honua.Console.Shell.Models;

public static class ConsoleCatalogActionPolicy
{
    /// <param name="studioAuthoringAvailable">
    /// Whether the "Edit In Studio" action should be offered. The Console's non-realtime Studio
    /// builder surfaces are shelved behind the <c>studio-builders</c> capability, so the caller
    /// supplies the advertised state (<c>IConsoleCapabilityManifest</c>) rather than this pure policy
    /// reaching for DI. Defaults to <see langword="false"/> to match the shipped default: no caller
    /// silently re-exposes a shelved route by forgetting the argument.
    /// </param>
    public static IReadOnlyList<ConsoleCatalogAction> Resolve(
        ConsoleContentSummary item,
        bool isAuthenticated,
        string publicLinkToken = "",
        bool studioAuthoringAvailable = false)
    {
        ArgumentNullException.ThrowIfNull(item);

        var actions = new List<ConsoleCatalogAction>
        {
            new(
                "detail",
                "Open Detail",
                WithPublicLinkToken($"/catalog/{item.SlugOrId()}", item, isAuthenticated, publicLinkToken),
                true)
        };

        if (item.ViewerSupport.CanOpenInViewer
            && item.ViewerSupport.SupportState == ConsoleContentSupportState.Supported)
        {
            if (string.Equals(item.Type, "map", StringComparison.Ordinal))
            {
                var href = WithPublicLinkToken($"/maps/{item.SlugOrId()}", item, isAuthenticated, publicLinkToken);
                actions.Add(new("viewer", "Open Map", href, true));
            }
            else if (isAuthenticated)
            {
                actions.Add(new("viewer", "Open Map", $"/maps/new?from={Uri.EscapeDataString(item.Id)}", true));
            }
        }

        if (!isAuthenticated)
        {
            return actions;
        }

        var canEdit = IsOwnerOrEditor(item.ResolvedRole);
        if (studioAuthoringAvailable
            && canEdit
            && item.ViewerSupport.CanEditInStudio
            && item.ViewerSupport.SupportState == ConsoleContentSupportState.Supported)
        {
            actions.Add(new(
                "studio",
                "Edit In Studio",
                $"/studio?source=catalog&itemId={Uri.EscapeDataString(item.Id)}",
                true));
        }

        if (canEdit)
        {
            actions.Add(new("share", "Share", $"/catalog/{item.SlugOrId()}?tab=publication", true));
        }

        if (string.Equals(item.ResolvedRole, "owner", StringComparison.Ordinal))
        {
            actions.Add(new("retire", "Review Usage", $"/catalog/{item.SlugOrId()}?tab=usage", true));
        }

        return actions;
    }

    private static bool IsOwnerOrEditor(string role) =>
        string.Equals(role, "owner", StringComparison.Ordinal)
        || string.Equals(role, "editor", StringComparison.Ordinal);

    private static string WithPublicLinkToken(
        string href,
        ConsoleContentSummary item,
        bool isAuthenticated,
        string publicLinkToken)
    {
        if (isAuthenticated
            || string.IsNullOrWhiteSpace(publicLinkToken)
            || !string.Equals(item.Access.Sharing, CatalogSharingTiers.PublicLink, StringComparison.Ordinal)
            || !string.Equals(publicLinkToken, item.Access.PublicLinkToken, StringComparison.Ordinal))
        {
            return href;
        }

        var separator = href.Contains("?", StringComparison.Ordinal) ? '&' : '?';
        return $"{href}{separator}token={Uri.EscapeDataString(publicLinkToken)}";
    }

    private static string SlugOrId(this ConsoleContentSummary item) =>
        string.IsNullOrWhiteSpace(item.Slug) ? item.Id : item.Slug;
}

public sealed record ConsoleCatalogAction(string Id, string Label, string Href, bool Enabled);
