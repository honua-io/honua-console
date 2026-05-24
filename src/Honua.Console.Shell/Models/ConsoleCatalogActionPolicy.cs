using Honua.Console.Contracts;

namespace Honua.Console.Shell.Models;

public static class ConsoleCatalogActionPolicy
{
    public static IReadOnlyList<ConsoleCatalogAction> Resolve(ConsoleContentSummary item, bool isAuthenticated)
    {
        ArgumentNullException.ThrowIfNull(item);

        var actions = new List<ConsoleCatalogAction>
        {
            new("detail", "Open Detail", $"/catalog/{item.SlugOrId()}", true)
        };

        if (item.ViewerSupport.CanOpenInViewer
            && item.ViewerSupport.SupportState == ConsoleContentSupportState.Supported)
        {
            var href = string.Equals(item.Type, "map", StringComparison.Ordinal)
                ? $"/maps/{item.SlugOrId()}"
                : $"/maps/new?from={Uri.EscapeDataString(item.Id)}";
            actions.Add(new("viewer", "Open Map", href, true));
        }

        if (!isAuthenticated)
        {
            return actions;
        }

        var canEdit = IsOwnerOrEditor(item.ResolvedRole);
        if (canEdit
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

    private static string SlugOrId(this ConsoleContentSummary item) =>
        string.IsNullOrWhiteSpace(item.Slug) ? item.Id : item.Slug;
}

public sealed record ConsoleCatalogAction(string Id, string Label, string Href, bool Enabled);
