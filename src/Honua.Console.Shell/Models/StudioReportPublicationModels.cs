using Honua.Console.Contracts;

namespace Honua.Console.Shell.Models;

// Console-side projections of the honua-server content publication registry (honua-server#1183),
// scoped to the report builder read path. These are immutable view records the report builder
// renders; they carry no local authoring/mutation state and never fabricate publication data —
// when the server is unbound or the publication is missing, the surface renders a capability state
// instead (Console Patterns Charter section 11).

/// <summary>
/// A binding/permission/missing surface for the report builder, mirroring the form-builder and
/// Operate capability-state pattern so missing bindings, missing permissions, and unsupported
/// contracts render consistently.
/// </summary>
public sealed record StudioReportCapabilityState(
    string Surface,
    string State,
    string Contract,
    string Detail);

/// <summary>
/// The server-owned publication state of a report: its active route plus the immutable version
/// history. Reading a publication is the first server-bound slice of the report builder; authoring
/// (data bindings, Vega-Lite charts, layout, narrative) and publish/share/embed mutation land in
/// follow-up slices once the editor surface is built on top of this read binding.
/// </summary>
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

/// <summary>One immutable published version of a report, projected for the version-history panel.</summary>
public sealed record StudioReportPublicationVersionView(
    string VersionId,
    long Revision,
    string? Title,
    string? ContentHash,
    int DependencyCount,
    bool IsActive,
    string CreatedBy,
    DateTimeOffset CreatedAt);

/// <summary>
/// Result of loading a report publication: the view when the server returned data, otherwise the
/// capability states explaining why (missing binding, missing permission, not found, unsupported).
/// </summary>
public sealed record StudioReportPublicationLoad(
    StudioReportPublicationView? Publication,
    IReadOnlyList<StudioReportCapabilityState> CapabilityStates)
{
    public bool HasPublication => Publication is not null;
}

/// <summary>Maps the honua-server content publication wire records to the report builder view records.</summary>
public static class StudioReportPublicationMapper
{
    public static StudioReportPublicationView ToView(HonuaContentPublicationDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        var route = detail.Route;
        // The wire DTO declares Versions/Dependencies non-nullable with a [] initializer, but
        // System.Text.Json overrides that initializer with null when the server emits an explicit JSON null
        // for the key (an omitted key keeps the initializer), so coalesce before every deref.
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
