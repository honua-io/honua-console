namespace Honua.Console.Shell.Models;

/// <summary>
/// Console-owned transition view model for the native Operate publishing workspace
/// (<c>/operate/publishing</c>). This is not a server wire contract: the publishing matrix and
/// review surface bind to honua-server (service/layer publish today, the full publication registry
/// behind <c>honua-server#1183</c>) through <c>honua-sdk-dotnet</c> or the
/// <c>Honua.Console.Contracts</c> shim once those projections land. There is no standing in-memory
/// publishing data source in the merged build (Console Patterns Charter section 11); when no server
/// base URL is configured the workspace renders an explicit missing-binding state via
/// <see cref="OperateCapabilityState"/>.
/// </summary>
public sealed record PublishingWorkspace(
    IReadOnlyList<PublishingMatrixRow> Matrix,
    IReadOnlyList<PublishingReview> Reviews,
    IReadOnlyList<OperateCapabilityState> CapabilityStates);

/// <summary>
/// One cell-group of the publication matrix: a publishable resource (service, layer, catalog entry,
/// or Studio artifact) crossed with the output/catalog formats it can be published to and the
/// support state for each target. Unsupported targets carry a blocker so the UI can render the
/// blocker before any publish is attempted (acceptance criteria: "Unsupported targets render
/// blockers before execution").
/// </summary>
public sealed record PublishingMatrixRow(
    string ResourceId,
    string ResourceName,
    PublishingResourceKind ResourceKind,
    IReadOnlyList<PublishingMatrixTarget> Targets);

public sealed record PublishingMatrixTarget(
    string Format,
    PublishingTargetSupport Support,
    string? Blocker);

public enum PublishingResourceKind
{
    Service,
    Layer,
    CatalogEntry,
    StudioArtifact
}

public enum PublishingTargetSupport
{
    Supported,
    Unsupported,
    Blocked
}

/// <summary>
/// Publication review record surfaced before and after a publish. Carries the fields the acceptance
/// criteria require the review to show: resource, slot, generated endpoints, catalog registration,
/// policy, warnings, rollback class, and provenance, plus deep links into the publication job,
/// events, audit, and rollback evidence.
/// </summary>
public sealed record PublishingReview(
    string ResourceId,
    string ResourceName,
    PublishingResourceKind ResourceKind,
    string Slot,
    IReadOnlyList<PublishingEndpoint> GeneratedEndpoints,
    PublishingCatalogRegistration CatalogRegistration,
    string Policy,
    IReadOnlyList<string> Warnings,
    string RollbackClass,
    string Provenance,
    PublishingReviewLinks Links);

public sealed record PublishingEndpoint(
    string Protocol,
    string Url);

public sealed record PublishingCatalogRegistration(
    bool Registered,
    string? CatalogEntryId,
    string Visibility);

/// <summary>
/// Deep links into the publication job, events, audit, and rollback evidence for a review. A null
/// value means the backing record does not exist yet (for example before publish executes); the UI
/// renders the link only when present.
/// </summary>
public sealed record PublishingReviewLinks(
    string? JobHref,
    string? EventsHref,
    string? AuditHref,
    string? RollbackHref);

/// <summary>
/// Result of a single-publication lookup or a republish/rollback action. Carries the review and the
/// immutable version history when the server returned a publication, otherwise the capability states
/// explaining why (missing binding, missing permission, not found, conflict, unsupported, transport).
/// The data source never fabricates a review when the read fails (Console Patterns Charter section 11).
/// </summary>
public sealed record PublishingLookupResult(
    PublishingReview? Review,
    IReadOnlyList<PublishingVersion> Versions,
    IReadOnlyList<OperateCapabilityState> CapabilityStates)
{
    public bool HasReview => Review is not null;

    public static PublishingLookupResult FromCapabilityState(OperateCapabilityState state) =>
        new(null, [], [state]);
}

/// <summary>One immutable published version, projected for the version-history / rollback-target panel.</summary>
public sealed record PublishingVersion(
    string VersionId,
    long Revision,
    string? Title,
    string? ContentHash,
    bool IsActive,
    string CreatedBy,
    DateTimeOffset CreatedAt);

/// <summary>Authoring command for a republish (new immutable version, active pointer advanced).</summary>
public sealed record PublishingRepublishCommand(
    string? Title = null,
    string? ContentHash = null,
    string? ExpectedEtag = null);

/// <summary>
/// Authoring command for a rollback. Exactly one of <see cref="TargetVersionId"/> or
/// <see cref="TargetRevision"/> identifies the version the active pointer moves back to.
/// </summary>
public sealed record PublishingRollbackCommand(
    string? TargetVersionId = null,
    long? TargetRevision = null,
    string? ExpectedEtag = null);
