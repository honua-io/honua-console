using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Pure unit coverage for <see cref="PublishingWorkspaceMapper"/>. Asserts the projection from the
/// honua-server content publication registry (honua-server#1183) route/version graph into the
/// publishing workspace review + matrix + version models: slot, generated endpoints, catalog
/// registration, policy summary, warnings, rollback class, provenance, and evidence deep links.
/// </summary>
public sealed class PublishingWorkspaceMapperTests
{
    [Fact]
    public void ToReview_ProjectsSlotEndpointsCatalogPolicyAndProvenance()
    {
        var detail = PublicDetail(rollbackTarget: null, previousVersion: "ver-1");

        var review = PublishingWorkspaceMapper.ToReview(detail);

        Assert.Equal("pub-parcels", review.ResourceId);
        Assert.Equal("Parcels map", review.ResourceName);
        Assert.Equal(PublishingResourceKind.StudioArtifact, review.ResourceKind);
        // Slot carries the route slug, lifecycle, and active revision.
        Assert.Contains("parcels", review.Slot, StringComparison.Ordinal);
        Assert.Contains("active", review.Slot, StringComparison.Ordinal);
        Assert.Contains("rev 2", review.Slot, StringComparison.Ordinal);
        // Generated endpoint is the registry-claimed public route path.
        var endpoint = Assert.Single(review.GeneratedEndpoints);
        Assert.Equal("/published/parcels", endpoint.Url);
        // Public visibility on an active route is catalog-registered to the publication id.
        Assert.True(review.CatalogRegistration.Registered);
        Assert.Equal("pub-parcels", review.CatalogRegistration.CatalogEntryId);
        Assert.Equal("public", review.CatalogRegistration.Visibility);
        Assert.Contains("visibility: public", review.Policy, StringComparison.Ordinal);
        Assert.Contains("operator@honua.test", review.Provenance, StringComparison.Ordinal);
        // A route that has moved at least once is reversible.
        Assert.Equal("reversible", review.RollbackClass);
        // Public visibility raises a warning before publish.
        Assert.Contains(review.Warnings, w => w.Contains("Public visibility", StringComparison.Ordinal));
        // Provenance refs become evidence deep links.
        Assert.Equal("/operate/jobs/job-9", review.Links.JobHref);
        Assert.Equal("/operate/audit/aud-9", review.Links.AuditHref);
    }

    [Fact]
    public void ToReview_PrivateBrandNewRoute_IsNotRegisteredAndIrreversible()
    {
        var detail = PrivateBrandNewDetail();

        var review = PublishingWorkspaceMapper.ToReview(detail);

        Assert.False(review.CatalogRegistration.Registered);
        Assert.Null(review.CatalogRegistration.CatalogEntryId);
        Assert.Equal("irreversible", review.RollbackClass);
        Assert.Null(review.Links.JobHref);
    }

    [Fact]
    public void ToReview_RolledBackRoute_ReportsRolledBackClass()
    {
        var detail = PublicDetail(rollbackTarget: "ver-1", previousVersion: "ver-2");

        var review = PublishingWorkspaceMapper.ToReview(detail);

        Assert.Equal("rolled-back", review.RollbackClass);
    }

    [Fact]
    public void ToMatrixRow_SuspendedRoute_RendersBlockerBeforeExecution()
    {
        var detail = PublicDetail(rollbackTarget: null, previousVersion: "ver-1") with
        {
            Route = PublicDetail(null, "ver-1").Route with { Lifecycle = HonuaContentPublicationLifecycles.Suspended }
        };

        var row = PublishingWorkspaceMapper.ToMatrixRow(detail);

        var target = Assert.Single(row.Targets);
        Assert.Equal(PublishingTargetSupport.Blocked, target.Support);
        Assert.Contains("suspended", target.Blocker!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToVersions_OrdersNewestFirstAndMarksActive()
    {
        var detail = PublicDetail(rollbackTarget: null, previousVersion: "ver-1");

        var versions = PublishingWorkspaceMapper.ToVersions(detail);

        Assert.Equal(2, versions.Count);
        Assert.Equal(2, versions[0].Revision);
        Assert.True(versions[0].IsActive);
        Assert.False(versions[1].IsActive);
    }

    private static HonuaContentPublicationDetail PublicDetail(string? rollbackTarget, string? previousVersion) =>
        new()
        {
            Route = new HonuaContentPublicationRouteState
            {
                PublicationId = "pub-parcels",
                RouteSlug = "parcels",
                RoutePath = "/published/parcels",
                Kind = HonuaContentPublicationKinds.Map,
                ActiveVersionId = "ver-2",
                ActiveRevision = 2,
                PreviousVersionId = previousVersion,
                RollbackTargetVersionId = rollbackTarget,
                Lifecycle = HonuaContentPublicationLifecycles.Active,
                Policy = new HonuaContentPublicationPolicy
                {
                    Visibility = HonuaContentPublicationVisibilities.Public,
                    Share = new HonuaContentSharePolicy { AllowSharing = true, AllowAnonymous = true }
                },
                Etag = "etag-2",
                UpdatedBy = "operator@honua.test",
                UpdatedAt = DateTimeOffset.Parse("2026-05-29T12:00:00Z")
            },
            Versions =
            [
                new HonuaContentPublicationVersion
                {
                    PublicationId = "pub-parcels",
                    VersionId = "ver-2",
                    Revision = 2,
                    Kind = HonuaContentPublicationKinds.Map,
                    RouteSlug = "parcels",
                    RoutePath = "/published/parcels",
                    Title = "Parcels map",
                    CreatedBy = "operator@honua.test",
                    CreatedAt = DateTimeOffset.Parse("2026-05-29T12:00:00Z"),
                    Provenance =
                    [
                        new HonuaContentPublicationProvenanceRef { Kind = "job", RefId = "job-9" },
                        new HonuaContentPublicationProvenanceRef { Kind = "audit", RefId = "aud-9" }
                    ]
                },
                new HonuaContentPublicationVersion
                {
                    PublicationId = "pub-parcels",
                    VersionId = "ver-1",
                    Revision = 1,
                    Kind = HonuaContentPublicationKinds.Map,
                    RouteSlug = "parcels",
                    RoutePath = "/published/parcels",
                    Title = "Parcels map",
                    CreatedBy = "operator@honua.test",
                    CreatedAt = DateTimeOffset.Parse("2026-05-28T12:00:00Z")
                }
            ]
        };

    private static HonuaContentPublicationDetail PrivateBrandNewDetail() =>
        new()
        {
            Route = new HonuaContentPublicationRouteState
            {
                PublicationId = "pub-draft",
                RouteSlug = "draft",
                RoutePath = "/published/draft",
                Kind = HonuaContentPublicationKinds.Dashboard,
                ActiveVersionId = "ver-1",
                ActiveRevision = 1,
                PreviousVersionId = null,
                RollbackTargetVersionId = null,
                Lifecycle = HonuaContentPublicationLifecycles.Active,
                Policy = new HonuaContentPublicationPolicy
                {
                    Visibility = HonuaContentPublicationVisibilities.Private
                },
                Etag = "etag-1",
                UpdatedBy = "operator@honua.test",
                UpdatedAt = DateTimeOffset.Parse("2026-05-29T12:00:00Z")
            },
            Versions =
            [
                new HonuaContentPublicationVersion
                {
                    PublicationId = "pub-draft",
                    VersionId = "ver-1",
                    Revision = 1,
                    Kind = HonuaContentPublicationKinds.Dashboard,
                    RouteSlug = "draft",
                    RoutePath = "/published/draft",
                    CreatedBy = "operator@honua.test",
                    CreatedAt = DateTimeOffset.Parse("2026-05-29T12:00:00Z")
                }
            ]
        };
}
