using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

public sealed class InMemoryConsoleCatalogClient : IConsoleCatalogClient
{
    private static readonly DateTimeOffset SeedTime = new(2026, 5, 24, 8, 0, 0, TimeSpan.Zero);

    private readonly IReadOnlyList<ConsoleContentDetail> _items = CreateSeedItems();

    public Task<CatalogSearchResult> SearchAsync(
        CatalogListRequest request,
        CatalogReadContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var visibleSummaries = _items
            .Select(item => item.Summary)
            .Where(item => IsVisibleToContext(item, context));
        var filtered = visibleSummaries;

        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            filtered = filtered.Where(item =>
                Contains(item.Title, request.Query)
                || Contains(item.Summary, request.Query)
                || item.Tags.Any(tag => Contains(tag, request.Query)));
        }

        if (!string.IsNullOrWhiteSpace(request.Type))
        {
            filtered = filtered.Where(item => string.Equals(item.Type, request.Type, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(request.Tag))
        {
            filtered = filtered.Where(item => item.Tags.Any(tag => string.Equals(tag, request.Tag, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(request.Owner))
        {
            filtered = filtered.Where(item => string.Equals(item.Owner, request.Owner, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(request.Sharing))
        {
            filtered = filtered.Where(item => string.Equals(item.Access.Sharing, request.Sharing, StringComparison.Ordinal));
        }

        filtered = request.Sort switch
        {
            CatalogSortOptions.ModifiedAsc => filtered.OrderBy(item => item.Modified),
            CatalogSortOptions.TitleAsc => filtered.OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase),
            CatalogSortOptions.TitleDesc => filtered.OrderByDescending(item => item.Title, StringComparer.OrdinalIgnoreCase),
            _ => filtered.OrderByDescending(item => item.Modified)
        };

        var items = filtered.ToArray();
        var typeCounts = visibleSummaries
            .GroupBy(item => item.Type, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        return Task.FromResult(new CatalogSearchResult(items, typeCounts, request));
    }

    public Task<CatalogItemReadResult> GetCatalogItemAsync(
        string idOrSlug,
        CatalogReadContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idOrSlug);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var item = FindItem(idOrSlug);
        if (item is null)
        {
            return Task.FromResult(CatalogItemReadResult.Denied(
                CatalogReadStatus.Missing,
                "The requested catalog item was not found."));
        }

        var authorization = AuthorizeItem(item.Summary, context);
        if (authorization != CatalogReadStatus.Allowed)
        {
            return Task.FromResult(CatalogItemReadResult.Denied(
                authorization,
                "This link is unavailable or no longer grants access."));
        }

        var supportStatus = item.Summary.ViewerSupport.SupportState;
        if (supportStatus == ConsoleContentSupportState.UnsupportedServiceMetadata)
        {
            return Task.FromResult(CatalogItemReadResult.Denied(
                CatalogReadStatus.UnsupportedServiceMetadata,
                item.Summary.ViewerSupport.UnsupportedReason));
        }

        if (supportStatus == ConsoleContentSupportState.UnsupportedPackageBinding)
        {
            return Task.FromResult(CatalogItemReadResult.Denied(
                CatalogReadStatus.UnsupportedPackageBinding,
                item.Summary.ViewerSupport.UnsupportedReason));
        }

        return Task.FromResult(CatalogItemReadResult.Allowed(item, context.Anonymous));
    }

    public Task<CatalogItemReadResult> GetOpenDataItemAsync(
        string idOrSlug,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idOrSlug);
        cancellationToken.ThrowIfCancellationRequested();

        var item = FindItem(idOrSlug);
        if (item is null || !IsOpenDataItem(item.Summary))
        {
            return Task.FromResult(CatalogItemReadResult.Denied(
                CatalogReadStatus.Missing,
                "This public open-data item is not available."));
        }

        return Task.FromResult(CatalogItemReadResult.Allowed(item, anonymousRead: true));
    }

    public Task<MapPackageReadResult> GetMapPackageAsync(
        string mapId,
        CatalogReadContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapId);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var item = FindItem(mapId);
        if (item is null || !string.Equals(item.Summary.Type, "map", StringComparison.Ordinal))
        {
            return Task.FromResult(MapPackageReadResult.Denied(
                CatalogReadStatus.Missing,
                "The requested map was not found."));
        }

        var authorization = AuthorizeItem(item.Summary, context);
        if (authorization != CatalogReadStatus.Allowed)
        {
            return Task.FromResult(MapPackageReadResult.Denied(
                authorization,
                "This map link is unavailable or no longer grants access."));
        }

        if (item.Summary.ViewerSupport.SupportState == ConsoleContentSupportState.UnsupportedPackageBinding)
        {
            return Task.FromResult(MapPackageReadResult.Denied(
                CatalogReadStatus.UnsupportedPackageBinding,
                item.Summary.ViewerSupport.UnsupportedReason));
        }

        return Task.FromResult(MapPackageReadResult.Allowed(ToMapPackage(item), context.Anonymous));
    }

    public Task<MapPackageReadResult> GetDraftMapAsync(
        string sourceItemId,
        CatalogReadContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceItemId);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (context.Anonymous)
        {
            return Task.FromResult(MapPackageReadResult.Denied(
                CatalogReadStatus.Unavailable,
                "Sign in to hydrate a draft map from catalog content."));
        }

        var item = FindItem(sourceItemId);
        if (item is null)
        {
            return Task.FromResult(MapPackageReadResult.Denied(
                CatalogReadStatus.Missing,
                "The source item for this draft map was not found."));
        }

        if (!IsDraftMapSourceType(item.Summary.Type))
        {
            return Task.FromResult(MapPackageReadResult.Denied(
                CatalogReadStatus.UnsupportedServiceMetadata,
                "Draft maps can only be hydrated from supported catalog services or layers."));
        }

        if (!item.Summary.ViewerSupport.CanOpenInViewer
            || item.Summary.ViewerSupport.SupportState != ConsoleContentSupportState.Supported)
        {
            return Task.FromResult(MapPackageReadResult.Denied(
                CatalogReadStatus.UnsupportedServiceMetadata,
                "The selected item cannot hydrate a map draft."));
        }

        var draft = new ConsoleMapPackage
        {
            Summary = item.Summary with
            {
                Id = $"draft-{item.Summary.Id}",
                Slug = string.Empty,
                Type = "map",
                Title = $"{item.Summary.Title} draft map",
                Summary = "Unsaved map draft hydrated from a catalog item.",
                Access = item.Summary.Access with { Embeddable = false, PublicLinkToken = string.Empty, EmbedToken = string.Empty }
            },
            Basemap = "Honua Streets",
            InitialExtent = "-158.3,21.1,-157.6,21.8",
            Layers = [item.Summary.Title]
        };

        return Task.FromResult(MapPackageReadResult.Allowed(draft));
    }

    public Task<MapPackageReadResult> AuthorizeEmbedAsync(
        string mapId,
        EmbedRouteOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapId);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        if (options.QueryStringCarriedToken)
        {
            return Task.FromResult(MapPackageReadResult.Denied(
                CatalogReadStatus.Unavailable,
                "Embed bearer tokens must be supplied in the URL fragment."));
        }

        var item = FindItem(mapId);
        if (item is null || !string.Equals(item.Summary.Type, "map", StringComparison.Ordinal))
        {
            return Task.FromResult(MapPackageReadResult.Denied(
                CatalogReadStatus.Missing,
                "The embedded map was not found."));
        }

        if (item.Summary.ViewerSupport.SupportState == ConsoleContentSupportState.UnsupportedPackageBinding)
        {
            return Task.FromResult(MapPackageReadResult.Denied(
                CatalogReadStatus.UnsupportedPackageBinding,
                item.Summary.ViewerSupport.UnsupportedReason));
        }

        if (!item.Summary.Access.Embeddable)
        {
            return Task.FromResult(MapPackageReadResult.Denied(
                CatalogReadStatus.Unavailable,
                "This map is not embeddable."));
        }

        var publicMap = string.Equals(item.Summary.Access.Sharing, CatalogSharingTiers.Public, StringComparison.Ordinal);
        var tokenMatches = !string.IsNullOrWhiteSpace(options.EmbedToken)
            && string.Equals(options.EmbedToken, item.Summary.Access.EmbedToken, StringComparison.Ordinal);

        if (!publicMap && !tokenMatches)
        {
            return Task.FromResult(MapPackageReadResult.Denied(
                CatalogReadStatus.Unavailable,
                "The embed token is missing or invalid."));
        }

        return Task.FromResult(MapPackageReadResult.Allowed(ToMapPackage(item), anonymousRead: true));
    }

    private ConsoleContentDetail? FindItem(string idOrSlug) =>
        _items.FirstOrDefault(item =>
            string.Equals(item.Summary.Id, idOrSlug, StringComparison.Ordinal)
            || string.Equals(item.Summary.Slug, idOrSlug, StringComparison.Ordinal));

    private static CatalogReadStatus AuthorizeItem(ConsoleContentSummary item, CatalogReadContext context)
    {
        return IsVisibleToContext(item, context)
            ? CatalogReadStatus.Allowed
            : CatalogReadStatus.Unavailable;
    }

    private static bool IsVisibleToContext(ConsoleContentSummary item, CatalogReadContext context)
    {
        if (!context.Anonymous)
        {
            return true;
        }

        if (string.Equals(item.Access.Sharing, CatalogSharingTiers.Public, StringComparison.Ordinal))
        {
            return true;
        }

        return string.Equals(item.Access.Sharing, CatalogSharingTiers.PublicLink, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(context.PublicLinkToken)
            && string.Equals(context.PublicLinkToken, item.Access.PublicLinkToken, StringComparison.Ordinal);
    }

    private static bool IsOpenDataItem(ConsoleContentSummary item) =>
        item.Access.OpenData
        && string.Equals(item.Access.Sharing, CatalogSharingTiers.Public, StringComparison.Ordinal)
        && (string.Equals(item.Type, "service", StringComparison.Ordinal)
            || string.Equals(item.Type, "layer", StringComparison.Ordinal)
            || string.Equals(item.Type, "document", StringComparison.Ordinal));

    private static bool IsDraftMapSourceType(string type) =>
        string.Equals(type, "service", StringComparison.Ordinal)
        || string.Equals(type, "layer", StringComparison.Ordinal);

    private static bool Contains(string value, string query) =>
        value.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static ConsoleMapPackage ToMapPackage(ConsoleContentDetail item) =>
        new()
        {
            Summary = item.Summary,
            Basemap = "Honua Streets",
            InitialExtent = "-158.3,21.1,-157.6,21.8",
            Layers = item.Bindings
                .Where(binding => string.Equals(binding.Kind, "layer", StringComparison.Ordinal))
                .Select(binding => binding.Target)
                .DefaultIfEmpty(item.Summary.Title)
                .ToArray()
        };

    private static IReadOnlyList<ConsoleContentDetail> CreateSeedItems()
    {
        var coastalService = CreateDetail(
            id: "svc-coastal-flood",
            slug: "coastal-flood-service",
            type: "service",
            title: "Coastal Flood Service",
            summary: "Public open-data service with flood depth and evacuation overlays.",
            owner: "resilience-team",
            tags: ["flood", "open-data", "coastal"],
            formats: ["GeoServices:FeatureService", "OGC:API:Features"],
            modifiedDaysAgo: 1,
            access: new ConsoleShareAccess
            {
                Sharing = CatalogSharingTiers.Public,
                OpenData = true,
                Embeddable = true
            },
            role: "owner",
            viewerSupport: new ConsoleViewerSupport
            {
                CanOpenInViewer = true,
                CanEditInStudio = true
            },
            capabilities: ["query", "tiles", "metadata-v2"],
            bindings: [new("Flood polygons", "layer", "Flood depth forecast", "ready")],
            publications: [new("/share/public/items/coastal-flood-service", "public", true, "published")],
            usage: [new("map-storm-response", "Storm Response Map", "map", "depends on this service for operational layers")]);

        var utilitiesLayer = CreateDetail(
            id: "lyr-utilities",
            slug: "utilities-layer",
            type: "layer",
            title: "Utilities Critical Layer",
            summary: "Public-link layer used by emergency planning partners.",
            owner: "infrastructure",
            tags: ["utilities", "response"],
            formats: ["GeoJSON", "MVT"],
            modifiedDaysAgo: 3,
            access: new ConsoleShareAccess
            {
                Sharing = CatalogSharingTiers.PublicLink,
                PublicLinkToken = "pl-utilities",
                Embeddable = false
            },
            role: "editor",
            viewerSupport: new ConsoleViewerSupport
            {
                CanOpenInViewer = true,
                CanEditInStudio = true
            },
            capabilities: ["query", "style"],
            bindings: [new("Primary layer", "layer", "Critical utilities", "ready")],
            publications: [new("/catalog/utilities-layer?token=pl-utilities", "public-link", false, "active")],
            usage: [new("map-storm-response", "Storm Response Map", "map", "included as an operational layer")]);

        var stormMap = CreateDetail(
            id: "map-storm-response",
            slug: "storm-response-map",
            type: "map",
            title: "Storm Response Map",
            summary: "Saved map package for public-link operations and iframe embeds.",
            owner: "resilience-team",
            tags: ["storm", "operations", "map"],
            formats: ["honua-webmap/v1"],
            modifiedDaysAgo: 0,
            access: new ConsoleShareAccess
            {
                Sharing = CatalogSharingTiers.PublicLink,
                PublicLinkToken = "pl-storm-map",
                EmbedToken = "embed-storm-map",
                Embeddable = true
            },
            role: "owner",
            viewerSupport: new ConsoleViewerSupport
            {
                CanOpenInViewer = true,
                CanEditInStudio = true
            },
            capabilities: ["webmap-doc/v1", "embed-token/v1"],
            bindings:
            [
                new("Flood service", "layer", "Coastal Flood Service", "ready"),
                new("Utilities", "layer", "Utilities Critical Layer", "ready")
            ],
            publications:
            [
                new("/maps/storm-response-map?token=pl-storm-map", "public-link", true, "active"),
                new("/embed/maps/storm-response-map#embedToken=<redacted>", "public-link", true, "active")
            ],
            usage: [new("app-response-board", "Response Board App", "app", "launches this map in a dashboard panel")]);

        var publicFieldMap = CreateDetail(
            id: "map-public-field",
            slug: "public-field-map",
            type: "map",
            title: "Public Field Map",
            summary: "Public saved map package for tokenless viewer-read parity.",
            owner: "field-team",
            tags: ["public", "field", "map"],
            formats: ["honua-webmap/v1"],
            modifiedDaysAgo: 2,
            access: new ConsoleShareAccess
            {
                Sharing = CatalogSharingTiers.Public,
                Embeddable = true
            },
            role: "viewer",
            viewerSupport: new ConsoleViewerSupport
            {
                CanOpenInViewer = true
            },
            capabilities: ["webmap-doc/v1"],
            bindings: [new("Field observations", "layer", "Public field observations", "ready")],
            publications: [new("/maps/public-field-map", "public", true, "published")],
            usage: []);

        var dashboard = CreateDetail(
            id: "dash-capital-projects",
            slug: "capital-projects-dashboard",
            type: "dashboard",
            title: "Capital Projects Dashboard",
            summary: "Organization dashboard with budget and delivery metrics.",
            owner: "planning",
            tags: ["capital", "projects"],
            formats: ["dashboard-package/v1"],
            modifiedDaysAgo: 5,
            access: new ConsoleShareAccess
            {
                Sharing = CatalogSharingTiers.Organization
            },
            role: "viewer",
            viewerSupport: new ConsoleViewerSupport
            {
                CanEditInStudio = true
            },
            capabilities: ["dashboard"],
            bindings: [new("Projects dataset", "dataset", "Capital Projects", "ready")],
            publications: [new("/catalog/capital-projects-dashboard", "org", false, "active")],
            usage: []);

        var legacyService = CreateDetail(
            id: "svc-legacy-parcels",
            slug: "legacy-parcels-service",
            type: "service",
            title: "Legacy Parcels Service",
            summary: "Older service metadata that needs a server-side metadata upgrade.",
            owner: "land-records",
            tags: ["parcels", "legacy"],
            formats: ["GeoServices:MapServer"],
            modifiedDaysAgo: 8,
            access: new ConsoleShareAccess
            {
                Sharing = CatalogSharingTiers.Public
            },
            role: "owner",
            viewerSupport: new ConsoleViewerSupport
            {
                CanOpenInViewer = true,
                SupportState = ConsoleContentSupportState.UnsupportedServiceMetadata,
                UnsupportedReason = "Metadata v1 services are listed but require the Metadata v2 projection before Console can render detail."
            },
            capabilities: ["metadata-v1"],
            bindings: [],
            publications: [new("/catalog/legacy-parcels-service", "public", false, "needs metadata upgrade")],
            usage: []);

        var futureMap = CreateDetail(
            id: "map-future-package",
            slug: "future-response-map",
            type: "map",
            title: "Future Response Map",
            summary: "Map package saved by a newer runtime than this Console build understands.",
            owner: "resilience-team",
            tags: ["storm", "future"],
            formats: ["honua-webmap/vNext"],
            modifiedDaysAgo: 10,
            access: new ConsoleShareAccess
            {
                Sharing = CatalogSharingTiers.Public,
                Embeddable = true,
                EmbedToken = "embed-future-map"
            },
            role: "owner",
            viewerSupport: new ConsoleViewerSupport
            {
                CanOpenInViewer = true,
                SupportState = ConsoleContentSupportState.UnsupportedPackageBinding,
                UnsupportedReason = "This saved-map package targets a newer package binding than this Console runtime."
            },
            capabilities: ["webmap-doc/vNext"],
            bindings: [],
            publications: [new("/maps/future-response-map", "public", true, "unsupported package")],
            usage: []);

        return [coastalService, utilitiesLayer, stormMap, publicFieldMap, dashboard, legacyService, futureMap];
    }

    private static ConsoleContentDetail CreateDetail(
        string id,
        string slug,
        string type,
        string title,
        string summary,
        string owner,
        string[] tags,
        string[] formats,
        int modifiedDaysAgo,
        ConsoleShareAccess access,
        string role,
        ConsoleViewerSupport viewerSupport,
        string[] capabilities,
        ConsoleContentBinding[] bindings,
        ConsoleContentPublication[] publications,
        ConsoleContentUsage[] usage)
    {
        var modified = SeedTime.AddDays(-modifiedDaysAgo);
        var itemSummary = new ConsoleContentSummary
        {
            Id = id,
            Slug = slug,
            Type = type,
            Title = title,
            Summary = summary,
            Owner = owner,
            Tags = tags,
            Formats = formats,
            Modified = modified,
            Access = access,
            ResolvedRole = role,
            ViewerSupport = viewerSupport
        };

        return new ConsoleContentDetail
        {
            Summary = itemSummary,
            Description = $"{summary} Metadata, access policy, dependencies, and publication history are shown from the shared content projection.",
            Capabilities = capabilities,
            Bindings = bindings,
            Publications = publications,
            Usage = usage,
            Versions =
            [
                new("v3", "Current", modified, owner),
                new("v2", "Metadata refresh", modified.AddDays(-14), owner),
                new("v1", "Initial publish", modified.AddDays(-42), owner)
            ],
            Lineage =
            [
                new("source", "src-admin-publish", "Legacy admin publish handoff", "published from"),
                new("consumer", "studio-draft", "Studio generated artifact", "can generate")
            ],
            Permissions =
            [
                new(owner, role, "workspace policy"),
                new("Emergency Response", "viewer", "group share")
            ],
            Activity =
            [
                new(modified, owner, "Updated metadata and publication policy"),
                new(modified.AddDays(-3), "console-smoke", "Verified catalog-detail route")
            ]
        };
    }
}
