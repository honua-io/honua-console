using Honua.Console.Contracts;

namespace Honua.Console.Shell.Models;

// Console-side projections of the honua-server content publication registry (honua-server#1183),
// scoped to the report builder read path. These are immutable view records the report builder
// renders; they carry no local authoring/mutation state and never fabricate publication data —
// when the server is unbound or the publication is missing, the surface renders a capability state
// instead (Console Patterns Charter section 11).

public sealed record StudioReportCapabilityState(
    string Surface,
    string State,
    string Contract,
    string Detail);

public sealed record StudioReportPublicationView(
    string PublicationId,
    string RouteSlug,
    string RoutePath,
    string Kind,
    string Lifecycle,
    string Visibility,
    bool Embeddable,
    string? ActiveTitle,
    string ActiveVersionId,
    long ActiveRevision,
    string? PreviousVersionId,
    string? RollbackTargetVersionId,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<StudioReportPublicationVersionView> Versions);

public sealed record StudioReportPublicationVersionView(
    string VersionId,
    long Revision,
    string? Title,
    string? ContentHash,
    int DependencyCount,
    bool IsActive,
    string CreatedBy,
    DateTimeOffset CreatedAt);

public sealed record StudioReportPublicationLoad(
    StudioReportPublicationView? Publication,
    IReadOnlyList<StudioReportCapabilityState> CapabilityStates)
{
    public bool HasPublication => Publication is not null;
}

public static class StudioReportPublicationMapper
{
    public static StudioReportPublicationView ToView(HonuaContentPublicationDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        // The wire DTO declares Route/Versions/Dependencies non-nullable with a new()/[] initializer, but
        // System.Text.Json overrides that initializer with null when the server emits an explicit JSON null
        // for the key (an omitted key keeps the initializer), so coalesce before every deref.
        var route = detail.Route ?? new();
        var activeTitle = (detail.Versions ?? [])
            .FirstOrDefault(version => string.Equals(version.VersionId, route.ActiveVersionId, StringComparison.Ordinal))?
            .Title;

        var versions = (detail.Versions ?? [])
            .Select(version => new StudioReportPublicationVersionView(
                VersionId: version.VersionId,
                Revision: version.Revision,
                Title: version.Title,
                ContentHash: version.ContentHash,
                DependencyCount: version.Dependencies?.Length ?? 0,
                IsActive: string.Equals(version.VersionId, route.ActiveVersionId, StringComparison.Ordinal),
                CreatedBy: version.CreatedBy,
                CreatedAt: version.CreatedAt))
            .OrderByDescending(version => version.Revision)
            .ToArray();

        return new StudioReportPublicationView(
            PublicationId: route.PublicationId,
            RouteSlug: route.RouteSlug,
            RoutePath: route.RoutePath,
            Kind: route.Kind,
            Lifecycle: route.Lifecycle,
            Visibility: route.Policy?.Visibility ?? HonuaContentPublicationVisibilities.Private,
            Embeddable: route.Policy?.Embed?.AllowEmbedding ?? false,
            ActiveTitle: activeTitle,
            ActiveVersionId: route.ActiveVersionId,
            ActiveRevision: route.ActiveRevision,
            PreviousVersionId: route.PreviousVersionId,
            RollbackTargetVersionId: route.RollbackTargetVersionId,
            UpdatedAt: route.UpdatedAt,
            Versions: versions);
    }
}
