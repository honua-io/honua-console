using System.Globalization;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Maps the honua-server content publication registry wire records (honua-server#1183) into the
/// Console publishing workspace view models. Pure and side-effect free so it is unit-tested directly:
/// it derives the matrix row, the review (slot, generated endpoints, catalog registration, policy,
/// warnings, rollback class, provenance, evidence deep links), and the version history from one
/// <see cref="HonuaContentPublicationDetail"/>. No data is fabricated; everything is projected from the
/// server document graph.
/// </summary>
public static class PublishingWorkspaceMapper
{
    /// <summary>Projects a publication detail into a review record for the review surface.</summary>
    public static PublishingReview ToReview(HonuaContentPublicationDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        // Route/Versions are non-null-typed with new()/[] initializers but deserialize to null on an explicit
        // server JSON null, so coalesce before every deref.
        var route = detail.Route ?? new();
        var activeVersion = (detail.Versions ?? [])
            .FirstOrDefault(v => string.Equals(v.VersionId, route.ActiveVersionId, StringComparison.Ordinal));

        var kind = MapKind(route.Kind);
        var resourceName = activeVersion?.Title is { Length: > 0 } title ? title : route.RouteSlug;

        return new PublishingReview(
            ResourceId: route.PublicationId,
            ResourceName: resourceName,
            ResourceKind: kind,
            Slot: BuildSlot(route),
            GeneratedEndpoints: BuildEndpoints(route),
            CatalogRegistration: BuildCatalogRegistration(route),
            Policy: BuildPolicy(route.Policy ?? new()),
            Warnings: BuildWarnings(route),
            RollbackClass: BuildRollbackClass(route),
            Provenance: BuildProvenance(route, activeVersion),
            Links: BuildLinks(route, activeVersion));
    }

    /// <summary>Projects a publication detail into a single matrix row (one publishable resource).</summary>
    public static PublishingMatrixRow ToMatrixRow(HonuaContentPublicationDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        var route = detail.Route ?? new();
        var kind = MapKind(route.Kind);
        var activeVersion = (detail.Versions ?? [])
            .FirstOrDefault(v => string.Equals(v.VersionId, route.ActiveVersionId, StringComparison.Ordinal));
        var resourceName = activeVersion?.Title is { Length: > 0 } title ? title : route.RouteSlug;

        return new PublishingMatrixRow(
            ResourceId: route.PublicationId,
            ResourceName: resourceName,
            ResourceKind: kind,
            Targets: BuildTargets(route));
    }

    /// <summary>Projects the immutable version history (newest first) for the version/rollback panel.</summary>
    public static IReadOnlyList<PublishingVersion> ToVersions(HonuaContentPublicationDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        var activeVersionId = (detail.Route ?? new()).ActiveVersionId;
        return (detail.Versions ?? [])
            .Select(v => new PublishingVersion(
                VersionId: v.VersionId,
                Revision: v.Revision,
                Title: v.Title,
                ContentHash: v.ContentHash,
                IsActive: string.Equals(v.VersionId, activeVersionId, StringComparison.Ordinal),
                CreatedBy: v.CreatedBy,
                CreatedAt: v.CreatedAt))
            .OrderByDescending(v => v.Revision)
            .ToArray();
    }

    private static PublishingResourceKind MapKind(string kind) => kind switch
    {
        HonuaContentPublicationKinds.Map => PublishingResourceKind.StudioArtifact,
        HonuaContentPublicationKinds.Dashboard => PublishingResourceKind.StudioArtifact,
        HonuaContentPublicationKinds.Report => PublishingResourceKind.StudioArtifact,
        HonuaContentPublicationKinds.GeneratedApp => PublishingResourceKind.StudioArtifact,
        _ => PublishingResourceKind.CatalogEntry
    };

    private static string BuildSlot(HonuaContentPublicationRouteState route) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0} · {1} (rev {2})",
            route.RouteSlug,
            route.Lifecycle,
            route.ActiveRevision);

    // The published route path is the generated public endpoint the registry claims on publish.
    private static IReadOnlyList<PublishingEndpoint> BuildEndpoints(HonuaContentPublicationRouteState route)
    {
        if (string.IsNullOrWhiteSpace(route.RoutePath))
        {
            return [];
        }

        var protocol = route.Kind switch
        {
            HonuaContentPublicationKinds.Map => "Published map",
            HonuaContentPublicationKinds.Dashboard => "Published dashboard",
            HonuaContentPublicationKinds.Report => "Published report",
            HonuaContentPublicationKinds.GeneratedApp => "Generated app",
            _ => "Published route"
        };

        return [new PublishingEndpoint(protocol, route.RoutePath)];
    }

    // A publication is "catalog registered" once its route resolves a public/org/team-visible path.
    private static PublishingCatalogRegistration BuildCatalogRegistration(HonuaContentPublicationRouteState route)
    {
        var visibility = (route.Policy ?? new()).Visibility;
        var registered = !string.Equals(visibility, HonuaContentPublicationVisibilities.Private, StringComparison.Ordinal)
            && string.Equals(route.Lifecycle, HonuaContentPublicationLifecycles.Active, StringComparison.Ordinal);

        // The catalog entry id for a published artifact is its publication id (the registry owns the route).
        return new PublishingCatalogRegistration(
            Registered: registered,
            CatalogEntryId: registered ? route.PublicationId : null,
            Visibility: visibility);
    }

    private static string BuildPolicy(HonuaContentPublicationPolicy policy)
    {
        // Share/Embed/Service are non-null-typed with new() initializers but deserialize to null on an
        // explicit server JSON null, so coalesce before each deref.
        var share = policy.Share ?? new();
        var embed = policy.Embed ?? new();
        var service = policy.Service ?? new();

        var parts = new List<string> { $"visibility: {policy.Visibility}" };
        if (share.AllowSharing)
        {
            parts.Add(share.AllowAnonymous ? "sharing: anonymous" : "sharing: authenticated");
        }

        if (embed.AllowEmbedding)
        {
            parts.Add("embedding: allowed");
        }

        if (service.RequireAuthenticatedServices)
        {
            parts.Add("services: authenticated");
        }

        return string.Join(", ", parts);
    }

    private static IReadOnlyList<string> BuildWarnings(HonuaContentPublicationRouteState route)
    {
        var warnings = new List<string>();
        // Policy and its nested Share/Embed are non-null-typed with new() initializers but deserialize to null
        // on an explicit server JSON null, so coalesce before each deref.
        var policy = route.Policy ?? new();
        var share = policy.Share ?? new();
        var embed = policy.Embed ?? new();

        if (string.Equals(policy.Visibility, HonuaContentPublicationVisibilities.Public, StringComparison.Ordinal))
        {
            warnings.Add("Public visibility exposes this route to anonymous callers.");
        }

        if (share.AllowAnonymous)
        {
            warnings.Add("Anonymous sharing is enabled; review the share policy before publishing.");
        }

        if (embed.AllowEmbedding
            && (embed.AllowedOrigins is null || embed.AllowedOrigins.Length == 0))
        {
            warnings.Add("Embedding is allowed without an allowed-origins list.");
        }

        if (string.Equals(route.Lifecycle, HonuaContentPublicationLifecycles.Suspended, StringComparison.Ordinal))
        {
            warnings.Add("Route is suspended; reads are denied until it is reactivated.");
        }

        return warnings;
    }

    // Rollback class is derived from the registry's route pointers: a route that has moved at least
    // once is reversible (a previous version exists to roll back to); a brand-new route is not yet
    // reversible; a route already pointed at a rollback target is in a rolled-back state.
    private static string BuildRollbackClass(HonuaContentPublicationRouteState route)
    {
        if (!string.IsNullOrWhiteSpace(route.RollbackTargetVersionId))
        {
            return "rolled-back";
        }

        return string.IsNullOrWhiteSpace(route.PreviousVersionId) ? "irreversible" : "reversible";
    }

    private static string BuildProvenance(
        HonuaContentPublicationRouteState route,
        HonuaContentPublicationVersion? activeVersion)
    {
        var actor = activeVersion?.CreatedBy is { Length: > 0 } createdBy ? createdBy : route.UpdatedBy;
        var at = activeVersion?.CreatedAt ?? route.UpdatedAt;
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0} at {1:O}",
            actor,
            at);
    }

    private static PublishingReviewLinks BuildLinks(
        HonuaContentPublicationRouteState route,
        HonuaContentPublicationVersion? activeVersion)
    {
        string? jobHref = null;
        string? eventsHref = null;
        string? auditHref = null;

        foreach (var provenance in activeVersion?.Provenance ?? [])
        {
            switch (provenance.Kind)
            {
                case "job" when jobHref is null && !string.IsNullOrWhiteSpace(provenance.RefId):
                    jobHref = $"/operate/jobs/{provenance.RefId}";
                    break;
                case "event" when eventsHref is null && !string.IsNullOrWhiteSpace(provenance.RefId):
                    eventsHref = $"/operate/events/{provenance.RefId}";
                    break;
                case "audit" when auditHref is null && !string.IsNullOrWhiteSpace(provenance.RefId):
                    auditHref = $"/operate/audit/{provenance.RefId}";
                    break;
            }
        }

        // Rollback evidence is available whenever the route can be rolled back to a prior version.
        var rollbackHref = string.IsNullOrWhiteSpace(route.PreviousVersionId)
            && string.IsNullOrWhiteSpace(route.RollbackTargetVersionId)
                ? null
                : $"/operate/publishing?publicationId={Uri.EscapeDataString(route.PublicationId)}#rollback";

        return new PublishingReviewLinks(jobHref, eventsHref, auditHref, rollbackHref);
    }

    private static IReadOnlyList<PublishingMatrixTarget> BuildTargets(HonuaContentPublicationRouteState route)
    {
        var format = route.Kind switch
        {
            HonuaContentPublicationKinds.Map => "Published map route",
            HonuaContentPublicationKinds.Dashboard => "Published dashboard route",
            HonuaContentPublicationKinds.Report => "Published report route",
            HonuaContentPublicationKinds.GeneratedApp => "Generated app route",
            _ => "Published route"
        };

        // A suspended/archived route renders a blocker before any republish/rollback is attempted
        // (acceptance criteria: unsupported targets render blockers before execution).
        var (support, blocker) = route.Lifecycle switch
        {
            HonuaContentPublicationLifecycles.Active => (PublishingTargetSupport.Supported, (string?)null),
            HonuaContentPublicationLifecycles.Suspended => (
                PublishingTargetSupport.Blocked,
                "Route is suspended; reactivate it before republishing."),
            HonuaContentPublicationLifecycles.Archived => (
                PublishingTargetSupport.Blocked,
                "Route is archived; it is retained for history only and cannot be republished."),
            _ => (PublishingTargetSupport.Unsupported, $"Unknown lifecycle '{route.Lifecycle}'.")
        };

        return [new PublishingMatrixTarget(format, support, blocker)];
    }
}
