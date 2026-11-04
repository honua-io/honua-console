using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

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

    public static string CoordinatedReleaseOperation(string packageId) =>
        $"{Prefix}/coordinated-releases/{Uri.EscapeDataString(packageId)}/operation";
}

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

public enum MetadataEnvironmentBindingStateWire
{
    [JsonStringEnumMemberName("bound")]
    Bound,

    [JsonStringEnumMemberName("missing")]
    Missing,

    [JsonStringEnumMemberName("environment-unavailable")]
    EnvironmentUnavailable,
}

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

// The list row carries identity and counts only, with no per-package entry graph.
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

// Coverage is covered or no-rollback; any other value, including absence, means unknown.
public sealed record MetadataReleaseDataScriptResponse
{
    [JsonPropertyName("scriptId")]
    public string ScriptId { get; init; } = string.Empty;

    [JsonPropertyName("fileName")]
    public string FileName { get; init; } = string.Empty;

    [JsonPropertyName("coverage")]
    public string? Coverage { get; init; }
}

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

public sealed record GitOpsMetadataReleaseSourceResponse
{
    [JsonPropertyName("environment")]
    public string Environment { get; init; } = string.Empty;

    [JsonPropertyName("revision")]
    public long Revision { get; init; }

    [JsonPropertyName("etag")]
    public string ETag { get; init; } = string.Empty;
}

public sealed record GitOpsMetadataReleaseTargetResponse
{
    [JsonPropertyName("environment")]
    public string Environment { get; init; } = string.Empty;
}

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

    // Omitted for a metadata promotion; absence means there is no target, not a zero-value one.
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

// Nullable members are left off the payload instead of written as null.
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
