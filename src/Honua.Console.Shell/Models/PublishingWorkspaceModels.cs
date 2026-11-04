namespace Honua.Console.Shell.Models;

public sealed record PublishingWorkspace(
    IReadOnlyList<PublishingMatrixRow> Matrix,
    IReadOnlyList<PublishingReview> Reviews,
    IReadOnlyList<OperateCapabilityState> CapabilityStates);

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

public sealed record PublishingReviewLinks(
    string? JobHref,
    string? EventsHref,
    string? AuditHref,
    string? RollbackHref);

public sealed record PublishingLookupResult(
    PublishingReview? Review,
    IReadOnlyList<PublishingVersion> Versions,
    IReadOnlyList<OperateCapabilityState> CapabilityStates)
{
    public bool HasReview => Review is not null;

    public static PublishingLookupResult FromCapabilityState(OperateCapabilityState state) =>
        new(null, [], [state]);
}

public sealed record PublishingVersion(
    string VersionId,
    long Revision,
    string? Title,
    string? ContentHash,
    bool IsActive,
    string CreatedBy,
    DateTimeOffset CreatedAt);

public sealed record PublishingRepublishCommand(
    string? Title = null,
    string? ContentHash = null,
    string? ExpectedEtag = null);

public sealed record PublishingRollbackCommand(
    string? TargetVersionId = null,
    long? TargetRevision = null,
    string? ExpectedEtag = null);
