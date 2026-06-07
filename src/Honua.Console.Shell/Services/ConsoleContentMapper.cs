using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Maps honua-server Console metadata v2 content + RBAC wire records (honua-server#1162) onto the
/// Console catalog/content view models (issue #7). Centralises the itemType/visibility/action
/// translation so the server-bound <see cref="HonuaServerConsoleCatalogClient"/> never duplicates the
/// projection. The server is the single source of truth for visibility, sharing, and RBAC verbs; this
/// mapper only re-shapes the wire contract — it never fabricates content or permissions.
/// </summary>
internal static class ConsoleContentMapper
{
    public static HonuaConsoleContentListQuery ToListQuery(CatalogListRequest request)
    {
        return new HonuaConsoleContentListQuery
        {
            ItemType = ToServerItemType(request.Type),
            Visibility = ToServerVisibility(request.Sharing),
            Owner = NullIfBlank(request.Owner),
            SearchTerm = NullIfBlank(request.Query),
            Cursor = NullIfBlank(request.Cursor)
        };
    }

    public static CatalogReadStatus ToReadStatus(HonuaAdminEndpointIssue? issue)
    {
        if (issue is null)
        {
            return CatalogReadStatus.Unavailable;
        }

        return issue.StatusCode switch
        {
            404 => CatalogReadStatus.Missing,
            401 or 403 => CatalogReadStatus.Forbidden,
            _ => CatalogReadStatus.Unavailable
        };
    }

    public static bool IsVisibleToContext(HonuaConsoleContentItem item, CatalogReadContext context)
    {
        // The server already enforces RBAC for the authenticated principal, so authenticated reads
        // trust the server result set. Anonymous Console reads only expose public items; public-link
        // token resolution is served by the Console Share facet, so an anonymous read without server
        // public visibility stays hidden rather than fabricating a token grant.
        if (!context.Anonymous)
        {
            return true;
        }

        return IsPublic(item);
    }

    public static bool IsPublic(HonuaConsoleContentItem item) =>
        string.Equals(item.Visibility, HonuaConsoleVisibilities.Public, StringComparison.Ordinal);

    public static bool IsOpenDataItem(HonuaConsoleContentItem item) =>
        IsPublic(item)
        && (string.Equals(item.ItemType, HonuaConsoleContentItemTypes.OpenData, StringComparison.Ordinal)
            || string.Equals(item.ItemType, HonuaConsoleContentItemTypes.Service, StringComparison.Ordinal)
            || string.Equals(item.ItemType, HonuaConsoleContentItemTypes.Layer, StringComparison.Ordinal));

    public static bool IsSavedMap(HonuaConsoleContentItem item) =>
        string.Equals(item.ItemType, HonuaConsoleContentItemTypes.SavedMap, StringComparison.Ordinal);

    public static bool IsDraftMapSource(HonuaConsoleContentItem item) =>
        string.Equals(item.ItemType, HonuaConsoleContentItemTypes.Service, StringComparison.Ordinal)
        || string.Equals(item.ItemType, HonuaConsoleContentItemTypes.Layer, StringComparison.Ordinal);

    public static bool HasEmbedAction(HonuaConsoleContentItem item) =>
        (item.Actions ?? []).Contains(HonuaConsoleContentActions.Embed, StringComparer.Ordinal);

    public static ConsoleContentSummary ToSummary(HonuaConsoleContentItem item)
    {
        var consoleType = ToConsoleType(item.ItemType);
        var actions = item.Actions ?? [];
        var canEdit = actions.Contains(HonuaConsoleContentActions.Edit, StringComparer.Ordinal);
        var canView = actions.Contains(HonuaConsoleContentActions.View, StringComparer.Ordinal);

        return new ConsoleContentSummary
        {
            Id = item.Id,
            Slug = NullIfBlank(item.Name) ?? item.Id,
            Type = consoleType,
            Title = NullIfBlank(item.Title) ?? item.Name,
            Summary = item.Description ?? string.Empty,
            Owner = item.OwnerId ?? string.Empty,
            Tags = item.Tags ?? [],
            Formats = ItemTypeFormats(item.ItemType),
            Modified = item.UpdatedAt ?? item.CreatedAt ?? DateTimeOffset.UtcNow,
            Access = ToAccess(item),
            ResolvedRole = ResolveRole(actions),
            ViewerSupport = new ConsoleViewerSupport
            {
                CanOpenInViewer = canView && CanOpenInViewer(consoleType),
                CanEditInStudio = canEdit && CanEditInStudio(consoleType)
            }
        };
    }

    public static ConsoleContentDetail ToDetail(HonuaConsoleContentItem item)
    {
        var summary = ToSummary(item);
        var capabilities = new List<string> { "metadata-v2" };
        if (!string.IsNullOrWhiteSpace(item.Lifecycle))
        {
            capabilities.Add($"lifecycle:{item.Lifecycle}");
        }

        if (!string.IsNullOrWhiteSpace(item.OperationalState))
        {
            capabilities.Add($"operational:{item.OperationalState}");
        }

        return new ConsoleContentDetail
        {
            Summary = summary,
            Description = item.Description ?? string.Empty,
            Capabilities = [.. capabilities],
            Versions = item.Generation is { } generation
                ? [new ConsoleContentVersion(
                    $"gen-{generation}",
                    "Current",
                    summary.Modified,
                    item.UpdatedById ?? item.OwnerId ?? string.Empty)]
                : [],
            Lineage = (item.Provenance ?? [])
                .Select(reference => new ConsoleContentLineageLink(
                    LineageDirection(reference.Rel),
                    reference.ItemId,
                    reference.Kind,
                    reference.Rel))
                .ToArray(),
            Bindings = [],
            Publications = [],
            Permissions = ToPermissions(item),
            Activity = ToActivity(item),
            Usage = []
        };
    }

    public static ConsoleMapPackage ToMapPackage(HonuaConsoleContentItem item)
    {
        return new ConsoleMapPackage
        {
            Summary = ToSummary(item)
        };
    }

    public static ConsoleMapPackage ToDraftMapPackage(HonuaConsoleContentItem item)
    {
        var sourceSummary = ToSummary(item);
        return new ConsoleMapPackage
        {
            Summary = sourceSummary with
            {
                Id = $"draft-{sourceSummary.Id}",
                Slug = string.Empty,
                Type = "map",
                Title = $"{sourceSummary.Title} draft map",
                Summary = "Unsaved map draft hydrated from a catalog item.",
                Access = sourceSummary.Access with
                {
                    Embeddable = false,
                    PublicLinkToken = string.Empty,
                    EmbedToken = string.Empty
                }
            },
            Layers = [sourceSummary.Title]
        };
    }

    private static ConsoleShareAccess ToAccess(HonuaConsoleContentItem item)
    {
        var sharing = ToConsoleSharing(item.Visibility);
        return new ConsoleShareAccess
        {
            Sharing = sharing,
            OpenData = IsOpenDataItem(item),
            Embeddable = HasEmbedAction(item)
        };
    }

    private static ConsoleContentPermission[] ToPermissions(HonuaConsoleContentItem item)
    {
        var permissions = new List<ConsoleContentPermission>();
        if (!string.IsNullOrWhiteSpace(item.OwnerId))
        {
            permissions.Add(new ConsoleContentPermission(item.OwnerId, ResolveRole(item.Actions ?? []), "workspace policy"));
        }

        if (!string.IsNullOrWhiteSpace(item.TeamScopeId))
        {
            permissions.Add(new ConsoleContentPermission(item.TeamScopeId, "viewer", "team scope"));
        }

        return [.. permissions];
    }

    private static ConsoleContentActivity[] ToActivity(HonuaConsoleContentItem item)
    {
        var activity = new List<ConsoleContentActivity>();
        if (item.UpdatedAt is { } updated)
        {
            activity.Add(new ConsoleContentActivity(updated, item.UpdatedById ?? item.OwnerId ?? string.Empty, "Updated content metadata"));
        }

        if (item.CreatedAt is { } created && created != item.UpdatedAt)
        {
            activity.Add(new ConsoleContentActivity(created, item.CreatedById ?? item.OwnerId ?? string.Empty, "Created content item"));
        }

        return [.. activity];
    }

    private static string ResolveRole(IReadOnlyList<string> actions)
    {
        if (actions.Contains(HonuaConsoleContentActions.Administer, StringComparer.Ordinal))
        {
            return "owner";
        }

        if (actions.Contains(HonuaConsoleContentActions.Edit, StringComparer.Ordinal)
            || actions.Contains(HonuaConsoleContentActions.Publish, StringComparer.Ordinal))
        {
            return "editor";
        }

        return "viewer";
    }

    private static string LineageDirection(string rel) => rel switch
    {
        "derived-from" or "input-of" => "source",
        "publishes" or "generated-by" => "consumer",
        _ => "related"
    };

    private static bool CanOpenInViewer(string consoleType) =>
        consoleType is "service" or "layer" or "map";

    private static bool CanEditInStudio(string consoleType) =>
        consoleType is "map" or "dashboard" or "report" or "app";

    private static string ToConsoleType(string itemType) => itemType switch
    {
        HonuaConsoleContentItemTypes.SavedMap => "map",
        HonuaConsoleContentItemTypes.GeneratedApp => "app",
        HonuaConsoleContentItemTypes.OpenData => "document",
        _ => itemType
    };

    private static string? ToServerItemType(string? consoleType)
    {
        if (string.IsNullOrWhiteSpace(consoleType))
        {
            return null;
        }

        return consoleType switch
        {
            "map" => HonuaConsoleContentItemTypes.SavedMap,
            "app" => HonuaConsoleContentItemTypes.GeneratedApp,
            "document" => HonuaConsoleContentItemTypes.OpenData,
            "service" => HonuaConsoleContentItemTypes.Service,
            "layer" => HonuaConsoleContentItemTypes.Layer,
            "dashboard" => HonuaConsoleContentItemTypes.Dashboard,
            "report" => HonuaConsoleContentItemTypes.Report,
            _ => null
        };
    }

    private static string ToConsoleSharing(string visibility) => visibility switch
    {
        HonuaConsoleVisibilities.Public => CatalogSharingTiers.Public,
        HonuaConsoleVisibilities.Organization => CatalogSharingTiers.Organization,
        HonuaConsoleVisibilities.Team => CatalogSharingTiers.Group,
        _ => CatalogSharingTiers.Private
    };

    private static string? ToServerVisibility(string? consoleSharing)
    {
        if (string.IsNullOrWhiteSpace(consoleSharing))
        {
            return null;
        }

        return consoleSharing switch
        {
            CatalogSharingTiers.Public => HonuaConsoleVisibilities.Public,
            CatalogSharingTiers.Organization => HonuaConsoleVisibilities.Organization,
            CatalogSharingTiers.Group => HonuaConsoleVisibilities.Team,
            CatalogSharingTiers.Private => HonuaConsoleVisibilities.Personal,
            // public-link is a Share-facet tier with no direct visibility scope; do not constrain the
            // server list by it.
            _ => null
        };
    }

    private static string[] ItemTypeFormats(string itemType) => itemType switch
    {
        HonuaConsoleContentItemTypes.SavedMap => ["honua-webmap/v1"],
        HonuaConsoleContentItemTypes.Dashboard => ["dashboard-package/v1"],
        HonuaConsoleContentItemTypes.Report => ["report-package/v1"],
        HonuaConsoleContentItemTypes.GeneratedApp => ["app-package/v1"],
        _ => []
    };

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
