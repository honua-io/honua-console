using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

// SHIM(honua-sdk-dotnet): The GitOps metadata release contracts shipped by
// honua-server#1163 (release package + GitOps manifest export) and
// honua-server#1165 (release-operation lifecycle / rollback plan) are not yet
// projected to honua-sdk-dotnet. These records mirror the server HTTP/OpenAPI
// surface (NOT the server's internal Metadata v2 domain models) and are consumed
// through the single Console shim boundary until the SDK projection lands and
// honua-console swaps them for SDK types, exactly like OperateObservabilityContracts.
//
// Route map (concrete v1), all under /api/v1/admin/metadata, admin-authorized
// (X-API-Key):
//   GET /release-packages                                   -> MetadataReleasePackageListResponse (list)
//   GET /release-packages/{packageId:guid}                  -> MetadataReleasePackageResponse   (#1163)
//   GET /release-packages/{packageId:guid}/gitops-manifest  -> GitOpsMetadataReleaseManifestResponse (#1163)
//   GET /releases/{packageId}/operation                     -> DeployOperationResponse          (#1165)
//
// JSON on the wire is camelCase; enums serialize as kebab/lower string members.
// The release-package list endpoint returns lightweight summaries (no per-package
// entry graph); the by-id detail read hydrates the full proposal/diff/matrix.

/// <summary>
/// Concrete v1 routes for the GitOps metadata release contracts, kept in one place
/// so the client and tests share the exact server paths.
/// </summary>
public static class MetadataReleaseAdminRoutes
{
    public const string Prefix = "api/v1/admin/metadata";

    public static string ReleasePackages() =>
        $"{Prefix}/release-packages";

    public static string ReleasePackage(Guid packageId) =>
        $"{Prefix}/release-packages/{packageId:D}";

    public static string GitOpsManifest(Guid packageId) =>
        $"{Prefix}/release-packages/{packageId:D}/gitops-manifest";

    public static string ReleaseOperation(string packageId) =>
        $"{Prefix}/releases/{Uri.EscapeDataString(packageId)}/operation";

    /// <summary>
    /// Coordinated platform-upgrade release operation read (Demo C, honua-server#97):
    /// the container + DB-change + metadata op for a release package id.
    /// </summary>
    public static string CoordinatedReleaseOperation(string packageId) =>
        $"{Prefix}/coordinated-releases/{Uri.EscapeDataString(packageId)}/operation";
}

/// <summary>
/// Coarse change class for a release entry (mirrors the server kebab/lower members).
/// </summary>
public enum MetadataReleaseChangeClassWire
{
    [JsonStringEnumMemberName("metadata")]
    Metadata,

    [JsonStringEnumMemberName("content")]
    Content,

    [JsonStringEnumMemberName("binding")]
    Binding,

    [JsonStringEnumMemberName("policy")]
    Policy,

    [JsonStringEnumMemberName("delete")]
    Delete,
}

/// <summary>
/// Semantic artifact kind grouping for a release entry.
/// </summary>
public enum MetadataSemanticArtifactKindWire
{
    [JsonStringEnumMemberName("resource")]
    Resource,

    [JsonStringEnumMemberName("service")]
    Service,

    [JsonStringEnumMemberName("publication")]
    Publication,

    [JsonStringEnumMemberName("field")]
    Field,

    [JsonStringEnumMemberName("catalog")]
    Catalog,

    [JsonStringEnumMemberName("policy")]
    Policy,

    [JsonStringEnumMemberName("role")]
    Role,
}

/// <summary>
/// Binding state for an artifact in a target environment.
/// </summary>
public enum MetadataEnvironmentBindingStateWire
{
    [JsonStringEnumMemberName("bound")]
    Bound,

    [JsonStringEnumMemberName("missing")]
    Missing,

    [JsonStringEnumMemberName("environment-unavailable")]
    EnvironmentUnavailable,
}

/// <summary>
/// Release package lifecycle status.
/// </summary>
public enum MetadataReleasePackageStatusWire
{
    [JsonStringEnumMemberName("draft")]
    Draft,

    [JsonStringEnumMemberName("ready")]
    Ready,

    [JsonStringEnumMemberName("staged")]
    Staged,

    [JsonStringEnumMemberName("superseded")]
    Superseded,

    [JsonStringEnumMemberName("cancelled")]
    Cancelled,
}

/// <summary>Object metadata header reused across Metadata v2 responses.</summary>
public sealed record MetadataObjectMetadataResponse
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("namespace")]
    public string? Namespace { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

/// <summary>
/// Lightweight release-package summary returned by the list endpoint. Secret-safe:
/// carries only package identity, lifecycle, source/target environments, and coarse
/// counts (no per-package entry graph). The full proposal/diff/matrix is hydrated by
/// the by-id detail read.
/// </summary>
public sealed record MetadataReleasePackageSummaryResponse
{
    [JsonPropertyName("packageId")]
    public Guid PackageId { get; init; }

    [JsonPropertyName("packageKey")]
    public string PackageKey { get; init; } = string.Empty;

    [JsonPropertyName("namespace")]
    public string? Namespace { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("summary")]
    public string? Summary { get; init; }

    [JsonPropertyName("sourceEnvironment")]
    public string SourceEnvironment { get; init; } = string.Empty;

    [JsonPropertyName("sourceRevision")]
    public long SourceRevision { get; init; }

    [JsonPropertyName("targetEnvironments")]
    public IReadOnlyList<string> TargetEnvironments { get; init; } = [];

    [JsonPropertyName("entryCount")]
    public int EntryCount { get; init; }

    [JsonPropertyName("status")]
    public MetadataReleasePackageStatusWire Status { get; init; } = MetadataReleasePackageStatusWire.Draft;

    [JsonPropertyName("createdBy")]
    public string CreatedBy { get; init; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>Paged list of metadata release-package summaries.</summary>
public sealed record MetadataReleasePackageListResponse
{
    [JsonPropertyName("generatedAt")]
    public DateTimeOffset GeneratedAt { get; init; }

    [JsonPropertyName("items")]
    public IReadOnlyList<MetadataReleasePackageSummaryResponse> Items { get; init; } = [];

    [JsonPropertyName("count")]
    public int Count { get; init; }

    [JsonPropertyName("limit")]
    public int Limit { get; init; }

    [JsonPropertyName("offset")]
    public int Offset { get; init; }
}

/// <summary>Persisted metadata release package (#1163).</summary>
public sealed record MetadataReleasePackageResponse
{
    [JsonPropertyName("packageId")]
    public Guid PackageId { get; init; }

    [JsonPropertyName("metadata")]
    public MetadataObjectMetadataResponse? Metadata { get; init; }

    [JsonPropertyName("sourceEnvironment")]
    public string SourceEnvironment { get; init; } = string.Empty;

    [JsonPropertyName("sourceRevision")]
    public long SourceRevision { get; init; }

    [JsonPropertyName("sourceEtag")]
    public string SourceEtag { get; init; } = string.Empty;

    [JsonPropertyName("targetEnvironments")]
    public IReadOnlyList<string> TargetEnvironments { get; init; } = [];

    [JsonPropertyName("entries")]
    public IReadOnlyList<MetadataReleaseEntryResponse> Entries { get; init; } = [];

    [JsonPropertyName("dataScripts")]
    public IReadOnlyList<MetadataReleaseDataScriptResponse> DataScripts { get; init; } = [];

    [JsonPropertyName("status")]
    public MetadataReleasePackageStatusWire Status { get; init; } = MetadataReleasePackageStatusWire.Draft;

    [JsonPropertyName("createdBy")]
    public string CreatedBy { get; init; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>One semantic artifact entry in a release package.</summary>
public sealed record MetadataReleaseEntryResponse
{
    [JsonPropertyName("semanticId")]
    public string SemanticId { get; init; } = string.Empty;

    [JsonPropertyName("artifactKind")]
    public MetadataSemanticArtifactKindWire ArtifactKind { get; init; }

    [JsonPropertyName("resourceType")]
    public string? ResourceType { get; init; }

    [JsonPropertyName("desiredMetadataRevision")]
    public long DesiredMetadataRevision { get; init; }

    [JsonPropertyName("desiredContentVersionId")]
    public string? DesiredContentVersionId { get; init; }

    [JsonPropertyName("changeClass")]
    public MetadataReleaseChangeClassWire ChangeClass { get; init; } = MetadataReleaseChangeClassWire.Metadata;

    [JsonPropertyName("targetStates")]
    public IReadOnlyList<MetadataReleaseTargetStateResponse> TargetStates { get; init; } = [];

    [JsonPropertyName("dependentSemanticIds")]
    public IReadOnlyList<string> DependentSemanticIds { get; init; } = [];
}

/// <summary>
/// One data script carried by a release bundle and its rollback coverage (#1163
/// data-script coverage). <c>coverage</c> serializes as kebab/lower string members
/// (<c>covered</c> / <c>no-rollback</c>); absent/unknown maps to unknown.
/// </summary>
public sealed record MetadataReleaseDataScriptResponse
{
    [JsonPropertyName("scriptId")]
    public string ScriptId { get; init; } = string.Empty;

    [JsonPropertyName("fileName")]
    public string FileName { get; init; } = string.Empty;

    [JsonPropertyName("coverage")]
    public string? Coverage { get; init; }
}

/// <summary>Last-observed target environment state for a release entry.</summary>
public sealed record MetadataReleaseTargetStateResponse
{
    [JsonPropertyName("environment")]
    public string Environment { get; init; } = string.Empty;

    [JsonPropertyName("currentMetadataRevision")]
    public long? CurrentMetadataRevision { get; init; }

    [JsonPropertyName("currentContentVersionId")]
    public string? CurrentContentVersionId { get; init; }

    [JsonPropertyName("bindingState")]
    public MetadataEnvironmentBindingStateWire BindingState { get; init; }
}

/// <summary>GitOps-safe metadata release manifest (#1163).</summary>
public sealed record GitOpsMetadataReleaseManifestResponse
{
    [JsonPropertyName("apiVersion")]
    public string ApiVersion { get; init; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("metadata")]
    public MetadataObjectMetadataResponse? Metadata { get; init; }

    [JsonPropertyName("spec")]
    public GitOpsMetadataReleaseSpecResponse? Spec { get; init; }
}

/// <summary>GitOps manifest spec.</summary>
public sealed record GitOpsMetadataReleaseSpecResponse
{
    [JsonPropertyName("packageId")]
    public Guid PackageId { get; init; }

    [JsonPropertyName("source")]
    public GitOpsMetadataReleaseSourceResponse? Source { get; init; }

    [JsonPropertyName("targets")]
    public IReadOnlyList<GitOpsMetadataReleaseTargetResponse> Targets { get; init; } = [];

    [JsonPropertyName("entries")]
    public IReadOnlyList<MetadataReleaseEntryResponse> Entries { get; init; } = [];
}

/// <summary>GitOps manifest source environment state.</summary>
public sealed record GitOpsMetadataReleaseSourceResponse
{
    [JsonPropertyName("environment")]
    public string Environment { get; init; } = string.Empty;

    [JsonPropertyName("revision")]
    public long Revision { get; init; }

    [JsonPropertyName("etag")]
    public string ETag { get; init; } = string.Empty;
}

/// <summary>GitOps manifest target environment.</summary>
public sealed record GitOpsMetadataReleaseTargetResponse
{
    [JsonPropertyName("environment")]
    public string Environment { get; init; } = string.Empty;
}

/// <summary>Durable deploy workflow operation projecting the metadata release lifecycle (#1165).</summary>
public sealed record DeployOperationResponse
{
    [JsonPropertyName("operationId")]
    public string OperationId { get; init; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("priority")]
    public string Priority { get; init; } = string.Empty;

    /// <summary>
    /// The deploy target this operation actuates against (server-upgrade / rollback kind
    /// operations). <see langword="null"/> when the server omits it — a metadata-promotion
    /// kind operation carries its context in <see cref="MetadataRelease"/> instead and has no
    /// <c>target</c> object; absence here must be treated as "no target data", never as a
    /// zero-value target (console#290, honua-server PR #2577 null-omission contract).
    /// </summary>
    [JsonPropertyName("target")]
    public DeployPlanTargetResponse? Target { get; init; }

    [JsonPropertyName("metadataRelease")]
    public MetadataReleaseContextResponse? MetadataRelease { get; init; }

    [JsonPropertyName("providerOperationId")]
    public string? ProviderOperationId { get; init; }

    [JsonPropertyName("currentPhase")]
    public string? CurrentPhase { get; init; }

    [JsonPropertyName("observedState")]
    public string? ObservedState { get; init; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; init; }

    [JsonPropertyName("warnings")]
    public IReadOnlyList<string> Warnings { get; init; } = [];

    [JsonPropertyName("blockingReasons")]
    public IReadOnlyList<string> BlockingReasons { get; init; } = [];

    [JsonPropertyName("requestedBy")]
    public string? RequestedBy { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; init; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; init; }

    [JsonPropertyName("completedAt")]
    public DateTimeOffset? CompletedAt { get; init; }
}

/// <summary>
/// Deploy target metadata embedded in a workflow operation response (server-upgrade /
/// rollback kind operations; mirrors the server <c>DeployPlanTargetResponse</c>). Every
/// field but <c>targetId</c>/<c>targetKind</c>/<c>backend</c>/<c>environment</c>/<c>targetName</c>/
/// <c>desiredRevision</c>/<c>parameters</c> is nullable and omitted from the wire when null.
/// </summary>
public sealed record DeployPlanTargetResponse
{
    [JsonPropertyName("targetId")]
    public string TargetId { get; init; } = string.Empty;

    [JsonPropertyName("targetKind")]
    public string TargetKind { get; init; } = string.Empty;

    [JsonPropertyName("backend")]
    public string Backend { get; init; } = string.Empty;

    [JsonPropertyName("environment")]
    public string Environment { get; init; } = string.Empty;

    [JsonPropertyName("targetName")]
    public string TargetName { get; init; } = string.Empty;

    [JsonPropertyName("artifactReference")]
    public string? ArtifactReference { get; init; }

    [JsonPropertyName("runtimeProfile")]
    public string? RuntimeProfile { get; init; }

    [JsonPropertyName("currentRevision")]
    public string? CurrentRevision { get; init; }

    [JsonPropertyName("desiredRevision")]
    public string DesiredRevision { get; init; } = string.Empty;

    [JsonPropertyName("parameters")]
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();
}

/// <summary>Metadata release lifecycle context embedded in a workflow operation.</summary>
public sealed record MetadataReleaseContextResponse
{
    [JsonPropertyName("packageId")]
    public string PackageId { get; init; } = string.Empty;

    [JsonPropertyName("gitOperationId")]
    public string? GitOperationId { get; init; }

    [JsonPropertyName("prUrl")]
    public string? PrUrl { get; init; }

    [JsonPropertyName("commitSha")]
    public string? CommitSha { get; init; }

    [JsonPropertyName("desiredRevision")]
    public string DesiredRevision { get; init; } = string.Empty;

    [JsonPropertyName("targetEnvironment")]
    public string TargetEnvironment { get; init; } = string.Empty;

    [JsonPropertyName("deployOperationId")]
    public string? DeployOperationId { get; init; }

    [JsonPropertyName("jobIds")]
    public IReadOnlyList<string> JobIds { get; init; } = [];

    [JsonPropertyName("evidenceRefs")]
    public IReadOnlyList<MetadataEvidenceRefResponse> EvidenceRefs { get; init; } = [];

    [JsonPropertyName("currentStage")]
    public string CurrentStage { get; init; } = string.Empty;

    [JsonPropertyName("rollbackPlan")]
    public MetadataRollbackPlanResponse? RollbackPlan { get; init; }

    [JsonPropertyName("blockers")]
    public IReadOnlyList<string> Blockers { get; init; } = [];

    [JsonPropertyName("warnings")]
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>Rollback plan embedded in a metadata release operation.</summary>
public sealed record MetadataRollbackPlanResponse
{
    [JsonPropertyName("class")]
    public string Class { get; init; } = string.Empty;

    [JsonPropertyName("isDataAffecting")]
    public bool IsDataAffecting { get; init; }

    [JsonPropertyName("requiresExplicitApproval")]
    public bool RequiresExplicitApproval { get; init; }

    [JsonPropertyName("steps")]
    public IReadOnlyList<string> Steps { get; init; } = [];

    [JsonPropertyName("evidenceRequired")]
    public IReadOnlyList<string> EvidenceRequired { get; init; } = [];

    [JsonPropertyName("approvalPolicyRef")]
    public string? ApprovalPolicyRef { get; init; }
}

/// <summary>Evidence reference embedded in a release operation.</summary>
public sealed record MetadataEvidenceRefResponse
{
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("refId")]
    public string RefId { get; init; } = string.Empty;

    [JsonPropertyName("uri")]
    public string? Uri { get; init; }

    [JsonPropertyName("at")]
    public DateTimeOffset At { get; init; }
}

/// <summary>
/// One artifact carried by a coordinated platform-upgrade release (Demo C): the container image, the
/// DB/schema change, or the metadata semantic diff, shown side by side in the console.
/// </summary>
public sealed record CoordinatedReleaseArtifactResponse
{
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("desired")]
    public string Desired { get; init; } = string.Empty;

    [JsonPropertyName("current")]
    public string? Current { get; init; }
}

/// <summary>One step in the coordinated ordered step timeline (with gate/rollback status).</summary>
public sealed record CoordinatedReleaseStepResponse
{
    [JsonPropertyName("step")]
    public string Step { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("requiresApproval")]
    public bool RequiresApproval { get; init; }

    [JsonPropertyName("isReversible")]
    public bool IsReversible { get; init; }

    [JsonPropertyName("detail")]
    public string? Detail { get; init; }
}

/// <summary>
/// Coordinated platform-upgrade release operation (Demo C, honua-server#97): the three artifact kinds,
/// the ordered step timeline with per-step gate/approve status, and the rollback readiness for the op.
/// </summary>
public sealed record CoordinatedReleaseOperationResponse
{
    [JsonPropertyName("operationId")]
    public string OperationId { get; init; } = string.Empty;

    [JsonPropertyName("packageId")]
    public string PackageId { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("currentStep")]
    public string CurrentStep { get; init; } = string.Empty;

    [JsonPropertyName("targetEnvironment")]
    public string TargetEnvironment { get; init; } = string.Empty;

    [JsonPropertyName("currentPhase")]
    public string? CurrentPhase { get; init; }

    [JsonPropertyName("artifacts")]
    public IReadOnlyList<CoordinatedReleaseArtifactResponse> Artifacts { get; init; } = [];

    [JsonPropertyName("steps")]
    public IReadOnlyList<CoordinatedReleaseStepResponse> Steps { get; init; } = [];

    [JsonPropertyName("containerGateApproved")]
    public bool ContainerGateApproved { get; init; }

    [JsonPropertyName("dataGateApproved")]
    public bool DataGateApproved { get; init; }

    [JsonPropertyName("containerOperationId")]
    public string? ContainerOperationId { get; init; }

    [JsonPropertyName("metadataOperationId")]
    public string? MetadataOperationId { get; init; }

    [JsonPropertyName("rollbackReady")]
    public bool RollbackReady { get; init; }

    [JsonPropertyName("blockers")]
    public IReadOnlyList<string> Blockers { get; init; } = [];

    [JsonPropertyName("warnings")]
    public IReadOnlyList<string> Warnings { get; init; } = [];

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; init; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; init; }

    [JsonPropertyName("completedAt")]
    public DateTimeOffset? CompletedAt { get; init; }
}

/// <summary>
/// Source-generated JSON context for the GitOps metadata release wire contracts
/// (trim/AOT safe), mirroring OperateObservabilityJsonContext.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(MetadataReleasePackageListResponse))]
[JsonSerializable(typeof(MetadataReleasePackageResponse))]
[JsonSerializable(typeof(GitOpsMetadataReleaseManifestResponse))]
[JsonSerializable(typeof(DeployOperationResponse))]
[JsonSerializable(typeof(DeployPlanTargetResponse))]
[JsonSerializable(typeof(CoordinatedReleaseOperationResponse))]
public sealed partial class MetadataReleaseJsonContext : JsonSerializerContext
{
}
