using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

// SHIM(honua-sdk-dotnet#169): the honua-server unified GP/ETL workflow package + node registry API
// (#1185 commit 6d385f5c4, reconciled by #1228 commit 9b1ece3f7) is merged to honua-server trunk, but
// honua-sdk-dotnet does not yet project these workflow DTOs (no consumable Honua.Sdk workflow package,
// and honua-console wires no SDK NuGet feed). Per SDK_SHIM_POLICY.md the wire records and the thin HTTP
// client live behind this single Console contracts boundary until honua-sdk-dotnet#169 publishes the
// workflow projection and honua-console#7 swaps to SDK types. Do not add a sibling-repo ProjectReference;
// do not mirror these DTOs anywhere else. The wire shape mirrors
// honua-server/src/Honua.Core/Features/WorkflowPackages/Domain/{WorkflowPackageModels,WorkflowPackageEnums,
// WorkflowNodeRegistryModels,WorkflowPackageValidationModels}.cs + the console endpoint request models
// (camelCase properties, verbatim PascalCase string enums, null props omitted) and the shared
// ApiResponse<T> envelope. The endpoints are admin-authorized under /api/v1/console. See SDK_SHIM_POLICY
// "Active Shims".

#region Enums (WorkflowPackageEnums.cs)

// honua-server serializes these as verbatim PascalCase strings (UseStringEnumConverter without a naming
// policy), e.g. "Job", "Failure", "Geoprocessing" - matched by the server endpoint tests.
[JsonConverter(typeof(JsonStringEnumConverter<WorkflowNodeRuntimeKind>))]
public enum WorkflowNodeRuntimeKind
{
    Geoprocessing,
    ExtractTransformLoad,
    WorkflowUtility
}

[JsonConverter(typeof(JsonStringEnumConverter<WorkflowSchemaValueType>))]
public enum WorkflowSchemaValueType
{
    Text,
    WholeNumber,
    DecimalNumber,
    Flag,
    List,
    Structured
}

[JsonConverter(typeof(JsonStringEnumConverter<WorkflowEdgeKind>))]
public enum WorkflowEdgeKind
{
    Data,
    Control,
    Failure
}

[JsonConverter(typeof(JsonStringEnumConverter<WorkflowPublicationTarget>))]
public enum WorkflowPublicationTarget
{
    Job,
    Schedule,
    ProcessEndpoint
}

[JsonConverter(typeof(JsonStringEnumConverter<WorkflowPublicationStatus>))]
public enum WorkflowPublicationStatus
{
    Active,
    Disabled
}

#endregion

#region Node registry DTOs (WorkflowNodeRegistryModels.cs)

public sealed record WorkflowSchemaDefinition
{
    [JsonPropertyName("type")] public WorkflowSchemaValueType Type { get; init; }
    [JsonPropertyName("format")] public string? Format { get; init; }
    [JsonPropertyName("unit")] public string? Unit { get; init; }
    [JsonPropertyName("crs")] public string? Crs { get; init; }
    [JsonPropertyName("enumValues")] public IReadOnlyList<string> EnumValues { get; init; } = [];
    [JsonPropertyName("items")] public WorkflowSchemaDefinition? Items { get; init; }
    [JsonPropertyName("properties")]
    public IReadOnlyDictionary<string, WorkflowSchemaDefinition> Properties { get; init; } =
        new Dictionary<string, WorkflowSchemaDefinition>();
}

public sealed record WorkflowNodeParameterSchema
{
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("displayName")] public string DisplayName { get; init; } = string.Empty;
    [JsonPropertyName("description")] public string Description { get; init; } = string.Empty;
    [JsonPropertyName("required")] public bool Required { get; init; }
    [JsonPropertyName("defaultValue")] public string? DefaultValue { get; init; }
    [JsonPropertyName("schema")] public WorkflowSchemaDefinition Schema { get; init; } = new();
}

public sealed record WorkflowNodePortSchema
{
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("title")] public string Title { get; init; } = string.Empty;
    [JsonPropertyName("required")] public bool Required { get; init; }
    [JsonPropertyName("schema")] public WorkflowSchemaDefinition Schema { get; init; } = new();
}

public sealed record WorkflowNodeCapabilityFlags
{
    [JsonPropertyName("canValidate")] public bool CanValidate { get; init; }
    [JsonPropertyName("canDryRun")] public bool CanDryRun { get; init; }
    [JsonPropertyName("supportsJob")] public bool SupportsJob { get; init; }
    [JsonPropertyName("supportsSchedule")] public bool SupportsSchedule { get; init; }
    [JsonPropertyName("supportsProcessEndpoint")] public bool SupportsProcessEndpoint { get; init; }
    [JsonPropertyName("executable")] public bool Executable { get; init; }
}

public sealed record WorkflowNodeRuntimeHints
{
    [JsonPropertyName("workerProfile")] public string? WorkerProfile { get; init; }
    [JsonPropertyName("estimatedDurationSeconds")] public int? EstimatedDurationSeconds { get; init; }
    [JsonPropertyName("costWeight")] public double? CostWeight { get; init; }
    [JsonPropertyName("costUnit")] public string? CostUnit { get; init; }
}

public sealed record WorkflowNodeDefinition
{
    [JsonPropertyName("nodeTypeId")] public string NodeTypeId { get; init; } = string.Empty;
    [JsonPropertyName("providerId")] public string ProviderId { get; init; } = string.Empty;
    [JsonPropertyName("runtimeKind")] public WorkflowNodeRuntimeKind RuntimeKind { get; init; }
    [JsonPropertyName("title")] public string Title { get; init; } = string.Empty;
    [JsonPropertyName("description")] public string Description { get; init; } = string.Empty;
    [JsonPropertyName("category")] public string Category { get; init; } = string.Empty;
    [JsonPropertyName("parameterSchemas")] public IReadOnlyList<WorkflowNodeParameterSchema> ParameterSchemas { get; init; } = [];
    [JsonPropertyName("inputSchemas")] public IReadOnlyList<WorkflowNodePortSchema> InputSchemas { get; init; } = [];
    [JsonPropertyName("outputSchemas")] public IReadOnlyList<WorkflowNodePortSchema> OutputSchemas { get; init; } = [];
    [JsonPropertyName("capabilityFlags")] public WorkflowNodeCapabilityFlags CapabilityFlags { get; init; } = new();
    [JsonPropertyName("runtimeHints")] public WorkflowNodeRuntimeHints RuntimeHints { get; init; } = new();
    [JsonPropertyName("eligibilityWarnings")] public IReadOnlyList<string> EligibilityWarnings { get; init; } = [];
    [JsonPropertyName("processId")] public string? ProcessId { get; init; }
}

public sealed record WorkflowNodeProviderSnapshot
{
    [JsonPropertyName("providerId")] public string ProviderId { get; init; } = string.Empty;
    [JsonPropertyName("version")] public string Version { get; init; } = string.Empty;
    [JsonPropertyName("available")] public bool Available { get; init; } = true;
    [JsonPropertyName("warnings")] public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record WorkflowNodeRegistrySnapshot
{
    [JsonPropertyName("registryVersion")] public string RegistryVersion { get; init; } = string.Empty;
    [JsonPropertyName("generatedAt")] public DateTimeOffset GeneratedAt { get; init; }
    [JsonPropertyName("providers")] public IReadOnlyList<WorkflowNodeProviderSnapshot> Providers { get; init; } = [];
    [JsonPropertyName("nodes")] public IReadOnlyList<WorkflowNodeDefinition> Nodes { get; init; } = [];
    [JsonPropertyName("warnings")] public IReadOnlyList<string> Warnings { get; init; } = [];
}

#endregion

#region Package + graph DTOs (WorkflowPackageModels.cs)

public sealed record WorkflowNode
{
    [JsonPropertyName("nodeId")] public string NodeId { get; init; } = string.Empty;
    [JsonPropertyName("nodeTypeId")] public string NodeTypeId { get; init; } = string.Empty;
    [JsonPropertyName("parameters")]
    public IReadOnlyDictionary<string, string> Parameters { get; init; } =
        new Dictionary<string, string>();
    [JsonPropertyName("workerProfile")] public string? WorkerProfile { get; init; }
    [JsonPropertyName("metadata")]
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>();
}

public sealed record WorkflowEdge
{
    [JsonPropertyName("sourceNodeId")] public string SourceNodeId { get; init; } = string.Empty;
    [JsonPropertyName("targetNodeId")] public string TargetNodeId { get; init; } = string.Empty;
    [JsonPropertyName("kind")] public WorkflowEdgeKind Kind { get; init; } = WorkflowEdgeKind.Data;
    [JsonPropertyName("sourcePort")] public string? SourcePort { get; init; }
    [JsonPropertyName("targetPort")] public string? TargetPort { get; init; }
}

public sealed record WorkflowSchedule
{
    [JsonPropertyName("cronExpression")] public string CronExpression { get; init; } = string.Empty;
    [JsonPropertyName("timeZone")] public string? TimeZone { get; init; }
    [JsonPropertyName("enabled")] public bool Enabled { get; init; } = true;
}

public sealed record WorkflowGraph
{
    [JsonPropertyName("schemaVersion")] public string SchemaVersion { get; init; } = "workflow-package.v1";
    [JsonPropertyName("nodes")] public IReadOnlyList<WorkflowNode> Nodes { get; init; } = [];
    [JsonPropertyName("edges")] public IReadOnlyList<WorkflowEdge> Edges { get; init; } = [];
    [JsonPropertyName("schedule")] public WorkflowSchedule? Schedule { get; init; }
    [JsonPropertyName("workerProfile")] public string? WorkerProfile { get; init; }
    [JsonPropertyName("editorMetadata")]
    public IReadOnlyDictionary<string, string> EditorMetadata { get; init; } =
        new Dictionary<string, string>();
}

public sealed record WorkflowPackage
{
    [JsonPropertyName("packageId")] public string PackageId { get; init; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("namespace")] public string? Namespace { get; init; }
    [JsonPropertyName("graph")] public WorkflowGraph Graph { get; init; } = new();
    [JsonPropertyName("latestVersion")] public int? LatestVersion { get; init; }
    [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("updatedAt")] public DateTimeOffset UpdatedAt { get; init; }
    [JsonPropertyName("createdBy")] public string? CreatedBy { get; init; }
    [JsonPropertyName("updatedBy")] public string? UpdatedBy { get; init; }
    [JsonPropertyName("metadata")]
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>();
}

#endregion

#region Validation + dry-run + publication DTOs (WorkflowPackageValidationModels.cs)

public sealed record WorkflowPackageValidationFailure
{
    [JsonPropertyName("code")] public string Code { get; init; } = string.Empty;
    [JsonPropertyName("message")] public string Message { get; init; } = string.Empty;
    [JsonPropertyName("fieldPath")] public string? FieldPath { get; init; }
}

public sealed record WorkflowPackageValidationResult
{
    [JsonPropertyName("isValid")] public bool IsValid { get; init; }
    [JsonPropertyName("failures")] public IReadOnlyList<WorkflowPackageValidationFailure> Failures { get; init; } = [];
    [JsonPropertyName("warnings")] public IReadOnlyList<string> Warnings { get; init; } = [];
    [JsonPropertyName("packageHash")] public string? PackageHash { get; init; }
}

public sealed record WorkflowPackageVersion
{
    [JsonPropertyName("packageId")] public string PackageId { get; init; } = string.Empty;
    [JsonPropertyName("version")] public int Version { get; init; }
    [JsonPropertyName("schemaVersion")] public string SchemaVersion { get; init; } = string.Empty;
    [JsonPropertyName("packageHash")] public string PackageHash { get; init; } = string.Empty;
    [JsonPropertyName("graph")] public WorkflowGraph Graph { get; init; } = new();
    [JsonPropertyName("validation")] public WorkflowPackageValidationResult Validation { get; init; } = new();
    [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("createdBy")] public string? CreatedBy { get; init; }
}

public sealed record WorkflowDryRunLogEntry
{
    [JsonPropertyName("timestamp")] public DateTimeOffset Timestamp { get; init; }
    [JsonPropertyName("level")] public string Level { get; init; } = string.Empty;
    [JsonPropertyName("message")] public string Message { get; init; } = string.Empty;
    [JsonPropertyName("nodeId")] public string? NodeId { get; init; }
}

public sealed record WorkflowDryRunArtifact
{
    [JsonPropertyName("artifactId")] public string ArtifactId { get; init; } = string.Empty;
    [JsonPropertyName("kind")] public string Kind { get; init; } = string.Empty;
    [JsonPropertyName("label")] public string Label { get; init; } = string.Empty;
    [JsonPropertyName("schema")] public WorkflowSchemaDefinition? Schema { get; init; }
}

public sealed record WorkflowDryRunResult
{
    [JsonPropertyName("validation")] public WorkflowPackageValidationResult Validation { get; init; } = new();
    [JsonPropertyName("estimatedDurationSeconds")] public int EstimatedDurationSeconds { get; init; }
    [JsonPropertyName("estimatedCostWeight")] public double EstimatedCostWeight { get; init; }
    [JsonPropertyName("outputSchemas")] public IReadOnlyList<WorkflowNodePortSchema> OutputSchemas { get; init; } = [];
    [JsonPropertyName("logs")] public IReadOnlyList<WorkflowDryRunLogEntry> Logs { get; init; } = [];
    [JsonPropertyName("artifacts")] public IReadOnlyList<WorkflowDryRunArtifact> Artifacts { get; init; } = [];
    [JsonPropertyName("truncated")] public bool Truncated { get; init; }
    [JsonPropertyName("packageHash")] public string PackageHash { get; init; } = string.Empty;
}

public sealed record WorkflowPublication
{
    [JsonPropertyName("publicationId")] public string PublicationId { get; init; } = string.Empty;
    [JsonPropertyName("packageId")] public string PackageId { get; init; } = string.Empty;
    [JsonPropertyName("packageVersion")] public int PackageVersion { get; init; }
    [JsonPropertyName("packageHash")] public string PackageHash { get; init; } = string.Empty;
    [JsonPropertyName("target")] public WorkflowPublicationTarget Target { get; init; }
    [JsonPropertyName("status")] public WorkflowPublicationStatus Status { get; init; } = WorkflowPublicationStatus.Active;
    [JsonPropertyName("processId")] public string? ProcessId { get; init; }
    [JsonPropertyName("schedule")] public WorkflowSchedule? Schedule { get; init; }
    [JsonPropertyName("workflowDefinitionId")] public string? WorkflowDefinitionId { get; init; }
    [JsonPropertyName("endpointPath")] public string? EndpointPath { get; init; }
    [JsonPropertyName("eligibility")] public WorkflowPackageValidationResult Eligibility { get; init; } = new();
    [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("createdBy")] public string? CreatedBy { get; init; }
    [JsonPropertyName("provenance")]
    public IReadOnlyDictionary<string, string> Provenance { get; init; } =
        new Dictionary<string, string>();
}

public sealed record WorkflowPublicationRunResult
{
    [JsonPropertyName("publicationId")] public string PublicationId { get; init; } = string.Empty;
    [JsonPropertyName("packageId")] public string PackageId { get; init; } = string.Empty;
    [JsonPropertyName("packageVersion")] public int PackageVersion { get; init; }
    [JsonPropertyName("target")] public WorkflowPublicationTarget Target { get; init; }
    [JsonPropertyName("jobId")] public string? JobId { get; init; }
    [JsonPropertyName("workflowRunId")] public string? WorkflowRunId { get; init; }
    [JsonPropertyName("provenance")]
    public IReadOnlyDictionary<string, string> Provenance { get; init; } =
        new Dictionary<string, string>();
}

#endregion

#region List responses + request bodies (WorkflowPackageEndpointModels.cs)

public sealed record WorkflowPackageListResponse
{
    [JsonPropertyName("items")] public IReadOnlyList<WorkflowPackage> Items { get; init; } = [];
}

public sealed record WorkflowPackageVersionListResponse
{
    [JsonPropertyName("items")] public IReadOnlyList<WorkflowPackageVersion> Items { get; init; } = [];
}

public sealed record WorkflowPublicationListResponse
{
    [JsonPropertyName("items")] public IReadOnlyList<WorkflowPublication> Items { get; init; } = [];
}

public sealed record SaveWorkflowPackageRequest
{
    [JsonPropertyName("packageId")] public string? PackageId { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("namespace")] public string? Namespace { get; init; }
    [JsonPropertyName("graph")] public WorkflowGraph Graph { get; init; } = new();
    [JsonPropertyName("metadata")]
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>();
}

public sealed record PublishWorkflowPackageRequest
{
    [JsonPropertyName("publicationId")] public string? PublicationId { get; init; }
    [JsonPropertyName("target")] public WorkflowPublicationTarget Target { get; init; }
    [JsonPropertyName("processId")] public string? ProcessId { get; init; }
    [JsonPropertyName("schedule")] public WorkflowSchedule? Schedule { get; init; }
    [JsonPropertyName("enabled")] public bool Enabled { get; init; } = true;
}

public sealed record RunWorkflowPublicationRequest
{
    [JsonPropertyName("idempotencyKey")] public string? IdempotencyKey { get; init; }
    [JsonPropertyName("parameters")]
    public IReadOnlyDictionary<string, string> Parameters { get; init; } =
        new Dictionary<string, string>();
}

#endregion

#region Envelope + result types

public sealed record WorkflowApiResponse<T>(
    bool Success,
    T? Data,
    string? Message,
    DateTimeOffset? Timestamp);

public sealed record WorkflowEndpointResult<T>(T? Data, WorkflowEndpointIssue? Issue)
{
    public bool IsSuccess => Issue is null;

    public static WorkflowEndpointResult<T> FromData(T data) => new(data, null);

    public static WorkflowEndpointResult<T> FromIssue(WorkflowEndpointIssue issue) => new(default, issue);
}

public sealed record WorkflowEndpointIssue(
    string State,
    string Contract,
    string Detail,
    int? StatusCode = null)
{
    /// <summary>True when the server reported a conflict (e.g. duplicate publication id) and the caller must reload.</summary>
    public bool IsConflict => StatusCode == (int)HttpStatusCode.Conflict;
}

#endregion

#region Typed client

public sealed record WorkflowPackageClientOptions(Uri BaseUri, string? ApiKey = null);

/// <summary>
/// Thin typed client over the admin-authorized honua-server workflow package API (#1185, /api/v1/console).
/// Mirrors <see cref="IStudioPackageLifecycleClient"/>: every call returns a
/// <see cref="WorkflowEndpointResult{T}"/> so callers render shared blocked/missing-binding/forbidden
/// surfaces instead of throwing or fabricating data.
/// </summary>
public interface IWorkflowPackageApiClient
{
    Uri BaseUri { get; }

    Task<WorkflowEndpointResult<WorkflowNodeRegistrySnapshot>> GetNodeRegistryAsync(
        CancellationToken cancellationToken = default);

    Task<WorkflowEndpointResult<WorkflowPackageListResponse>> ListPackagesAsync(
        CancellationToken cancellationToken = default);

    Task<WorkflowEndpointResult<WorkflowPackage>> CreatePackageAsync(
        SaveWorkflowPackageRequest request,
        CancellationToken cancellationToken = default);

    Task<WorkflowEndpointResult<WorkflowPackage>> GetPackageAsync(
        string packageId,
        CancellationToken cancellationToken = default);

    Task<WorkflowEndpointResult<WorkflowPackage>> UpdatePackageAsync(
        string packageId,
        SaveWorkflowPackageRequest request,
        CancellationToken cancellationToken = default);

    Task<WorkflowEndpointResult<WorkflowPackageVersionListResponse>> ListVersionsAsync(
        string packageId,
        CancellationToken cancellationToken = default);

    Task<WorkflowEndpointResult<WorkflowPackageVersion>> CreateVersionAsync(
        string packageId,
        CancellationToken cancellationToken = default);

    Task<WorkflowEndpointResult<WorkflowPackageVersion>> GetVersionAsync(
        string packageId,
        int version,
        CancellationToken cancellationToken = default);

    Task<WorkflowEndpointResult<WorkflowPackageValidationResult>> ValidateVersionAsync(
        string packageId,
        int version,
        CancellationToken cancellationToken = default);

    Task<WorkflowEndpointResult<WorkflowDryRunResult>> DryRunVersionAsync(
        string packageId,
        int version,
        CancellationToken cancellationToken = default);

    Task<WorkflowEndpointResult<WorkflowPublication>> PublishVersionAsync(
        string packageId,
        int version,
        PublishWorkflowPackageRequest request,
        CancellationToken cancellationToken = default);

    Task<WorkflowEndpointResult<WorkflowPublicationListResponse>> ListPublicationsAsync(
        CancellationToken cancellationToken = default);

    Task<WorkflowEndpointResult<WorkflowPublicationRunResult>> RunPublicationAsync(
        string publicationId,
        RunWorkflowPublicationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class HttpWorkflowPackageApiClient : IWorkflowPackageApiClient, IDisposable
{
    private const string ConsoleRoot = "/api/v1/console";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;

    public HttpWorkflowPackageApiClient(HttpClient httpClient, WorkflowPackageClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        BaseUri = options.BaseUri;
        _apiKey = options.ApiKey;
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= BaseUri;
    }

    public Uri BaseUri { get; }

    public Task<WorkflowEndpointResult<WorkflowNodeRegistrySnapshot>> GetNodeRegistryAsync(
        CancellationToken cancellationToken = default) =>
        SendAsync<object, WorkflowNodeRegistrySnapshot>(
            HttpMethod.Get,
            $"{ConsoleRoot}/workflow-node-registry",
            null,
            "GET /api/v1/console/workflow-node-registry",
            cancellationToken);

    public Task<WorkflowEndpointResult<WorkflowPackageListResponse>> ListPackagesAsync(
        CancellationToken cancellationToken = default) =>
        SendAsync<object, WorkflowPackageListResponse>(
            HttpMethod.Get,
            $"{ConsoleRoot}/workflow-packages",
            null,
            "GET /api/v1/console/workflow-packages",
            cancellationToken);

    public Task<WorkflowEndpointResult<WorkflowPackage>> CreatePackageAsync(
        SaveWorkflowPackageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync<SaveWorkflowPackageRequest, WorkflowPackage>(
            HttpMethod.Post,
            $"{ConsoleRoot}/workflow-packages",
            request,
            "POST /api/v1/console/workflow-packages",
            cancellationToken);
    }

    public Task<WorkflowEndpointResult<WorkflowPackage>> GetPackageAsync(
        string packageId,
        CancellationToken cancellationToken = default) =>
        SendAsync<object, WorkflowPackage>(
            HttpMethod.Get,
            $"{ConsoleRoot}/workflow-packages/{Uri.EscapeDataString(packageId)}",
            null,
            "GET /api/v1/console/workflow-packages/{packageId}",
            cancellationToken);

    public Task<WorkflowEndpointResult<WorkflowPackage>> UpdatePackageAsync(
        string packageId,
        SaveWorkflowPackageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync<SaveWorkflowPackageRequest, WorkflowPackage>(
            HttpMethod.Put,
            $"{ConsoleRoot}/workflow-packages/{Uri.EscapeDataString(packageId)}",
            request,
            "PUT /api/v1/console/workflow-packages/{packageId}",
            cancellationToken);
    }

    public Task<WorkflowEndpointResult<WorkflowPackageVersionListResponse>> ListVersionsAsync(
        string packageId,
        CancellationToken cancellationToken = default) =>
        SendAsync<object, WorkflowPackageVersionListResponse>(
            HttpMethod.Get,
            $"{ConsoleRoot}/workflow-packages/{Uri.EscapeDataString(packageId)}/versions",
            null,
            "GET /api/v1/console/workflow-packages/{packageId}/versions",
            cancellationToken);

    public Task<WorkflowEndpointResult<WorkflowPackageVersion>> CreateVersionAsync(
        string packageId,
        CancellationToken cancellationToken = default) =>
        SendAsync<object, WorkflowPackageVersion>(
            HttpMethod.Post,
            $"{ConsoleRoot}/workflow-packages/{Uri.EscapeDataString(packageId)}/versions",
            null,
            "POST /api/v1/console/workflow-packages/{packageId}/versions",
            cancellationToken);

    public Task<WorkflowEndpointResult<WorkflowPackageVersion>> GetVersionAsync(
        string packageId,
        int version,
        CancellationToken cancellationToken = default) =>
        SendAsync<object, WorkflowPackageVersion>(
            HttpMethod.Get,
            $"{ConsoleRoot}/workflow-packages/{Uri.EscapeDataString(packageId)}/versions/{version}",
            null,
            "GET /api/v1/console/workflow-packages/{packageId}/versions/{version}",
            cancellationToken);

    public Task<WorkflowEndpointResult<WorkflowPackageValidationResult>> ValidateVersionAsync(
        string packageId,
        int version,
        CancellationToken cancellationToken = default) =>
        SendAsync<object, WorkflowPackageValidationResult>(
            HttpMethod.Post,
            $"{ConsoleRoot}/workflow-packages/{Uri.EscapeDataString(packageId)}/versions/{version}/validate",
            null,
            "POST /api/v1/console/workflow-packages/{packageId}/versions/{version}/validate",
            cancellationToken);

    public Task<WorkflowEndpointResult<WorkflowDryRunResult>> DryRunVersionAsync(
        string packageId,
        int version,
        CancellationToken cancellationToken = default) =>
        SendAsync<object, WorkflowDryRunResult>(
            HttpMethod.Post,
            $"{ConsoleRoot}/workflow-packages/{Uri.EscapeDataString(packageId)}/versions/{version}/dry-run",
            null,
            "POST /api/v1/console/workflow-packages/{packageId}/versions/{version}/dry-run",
            cancellationToken);

    public Task<WorkflowEndpointResult<WorkflowPublication>> PublishVersionAsync(
        string packageId,
        int version,
        PublishWorkflowPackageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync<PublishWorkflowPackageRequest, WorkflowPublication>(
            HttpMethod.Post,
            $"{ConsoleRoot}/workflow-packages/{Uri.EscapeDataString(packageId)}/versions/{version}/publish",
            request,
            "POST /api/v1/console/workflow-packages/{packageId}/versions/{version}/publish",
            cancellationToken);
    }

    public Task<WorkflowEndpointResult<WorkflowPublicationListResponse>> ListPublicationsAsync(
        CancellationToken cancellationToken = default) =>
        SendAsync<object, WorkflowPublicationListResponse>(
            HttpMethod.Get,
            $"{ConsoleRoot}/workflow-publications",
            null,
            "GET /api/v1/console/workflow-publications",
            cancellationToken);

    public Task<WorkflowEndpointResult<WorkflowPublicationRunResult>> RunPublicationAsync(
        string publicationId,
        RunWorkflowPublicationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync<RunWorkflowPublicationRequest, WorkflowPublicationRunResult>(
            HttpMethod.Post,
            $"{ConsoleRoot}/workflow-publications/{Uri.EscapeDataString(publicationId)}/runs",
            request,
            "POST /api/v1/console/workflow-publications/{publicationId}/runs",
            cancellationToken);
    }

    public void Dispose() => _httpClient.Dispose();

    private async Task<WorkflowEndpointResult<TResponse>> SendAsync<TBody, TResponse>(
        HttpMethod method,
        string path,
        TBody? body,
        string contract,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", _apiKey);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: JsonOptions);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return WorkflowEndpointResult<TResponse>.FromIssue(new WorkflowEndpointIssue(
                "Unavailable",
                contract,
                $"The Honua server workflow endpoint could not be reached: {ex.Message}"));
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return WorkflowEndpointResult<TResponse>.FromIssue(new WorkflowEndpointIssue(
                "Unavailable",
                contract,
                $"The Honua server workflow endpoint could not be reached: {ex.Message}"));
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var serverDetail = await TryReadErrorDetailAsync(response, cancellationToken).ConfigureAwait(false);
                return WorkflowEndpointResult<TResponse>.FromIssue(CreateIssue(contract, response.StatusCode, serverDetail));
            }

            WorkflowApiResponse<TResponse>? envelope;
            try
            {
                envelope = await response.Content
                    .ReadFromJsonAsync<WorkflowApiResponse<TResponse>>(JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                return WorkflowEndpointResult<TResponse>.FromIssue(new WorkflowEndpointIssue(
                    "Unsupported",
                    contract,
                    $"The Honua server workflow response did not match the expected API shape: {ex.Message}",
                    (int)response.StatusCode));
            }

            if (envelope?.Success == true && envelope.Data is not null)
            {
                return WorkflowEndpointResult<TResponse>.FromData(envelope.Data);
            }

            return WorkflowEndpointResult<TResponse>.FromIssue(new WorkflowEndpointIssue(
                "Unavailable",
                contract,
                envelope?.Message ?? "The Honua server workflow response did not include data.",
                (int)response.StatusCode));
        }
    }

    // Surfaces the server's own validation/problem message on a non-success status so the editor's blocked
    // surface explains *why* (e.g. "Workflow package validation failed" + the failing graph rules) rather
    // than only an HTTP code. Best-effort: a missing or unparseable body simply yields the generic detail.
    private static async Task<string?> TryReadErrorDetailAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var message = root.TryGetProperty("message", out var messageElement) &&
                messageElement.ValueKind == JsonValueKind.String
                ? messageElement.GetString()
                : root.TryGetProperty("detail", out var detailElement) && detailElement.ValueKind == JsonValueKind.String
                    ? detailElement.GetString()
                    : null;

            // ApiResponse<WorkflowPackageValidationResult>.Failure carries the failing rules under data.failures.
            if (root.TryGetProperty("data", out var data) &&
                data.ValueKind == JsonValueKind.Object &&
                data.TryGetProperty("failures", out var failures) &&
                failures.ValueKind == JsonValueKind.Array &&
                failures.GetArrayLength() > 0)
            {
                var rules = failures.EnumerateArray()
                    .Select(failure => failure.TryGetProperty("message", out var ruleMessage) &&
                        ruleMessage.ValueKind == JsonValueKind.String
                        ? ruleMessage.GetString()
                        : null)
                    .Where(rule => !string.IsNullOrWhiteSpace(rule))
                    .ToArray();

                if (rules.Length > 0)
                {
                    var joined = string.Join("; ", rules);
                    return string.IsNullOrWhiteSpace(message) ? joined : $"{message} {joined}";
                }
            }

            return string.IsNullOrWhiteSpace(message) ? null : message;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    private static WorkflowEndpointIssue CreateIssue(string contract, HttpStatusCode statusCode, string? serverDetail)
    {
        var state = statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Missing permission",
            HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented => "Unsupported",
            HttpStatusCode.BadRequest => "Validation failed",
            HttpStatusCode.Conflict => "Conflict",
            _ => "Unavailable"
        };

        var detail = serverDetail ?? statusCode switch
        {
            HttpStatusCode.Unauthorized => "The Honua server rejected the workflow request because admin authentication is missing.",
            HttpStatusCode.Forbidden => "The Honua server rejected the workflow request because the current principal lacks admin permission.",
            HttpStatusCode.NotFound => "The Honua server does not expose this workflow contract, or the package/version was not found.",
            HttpStatusCode.MethodNotAllowed => "The Honua server exposes the route but not the required workflow API verb.",
            HttpStatusCode.NotImplemented => "The Honua server reports this workflow capability is not implemented.",
            HttpStatusCode.BadRequest => "The Honua server rejected the workflow package as invalid.",
            HttpStatusCode.Conflict => "The workflow publication already exists or conflicts with server state; reload before retrying.",
            _ => string.Format(
                CultureInfo.InvariantCulture,
                "The Honua server returned HTTP {0} ({1}) for the workflow request.",
                (int)statusCode,
                statusCode)
        };

        return new WorkflowEndpointIssue(state, contract, detail, (int)statusCode);
    }
}

#endregion
