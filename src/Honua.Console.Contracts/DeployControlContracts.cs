using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

public static class DeployControlAdminRoutes
{
    public const string Prefix = "api/v1/admin/deploy";

    [OpsParityRoute("GET")]
    public const string Preflight = Prefix + "/preflight";

    [OpsParityRoute("GET")]
    public const string Operations = Prefix + "/operations";

    [OpsParityRoute("GET")]
    public const string OperationTemplate = Operations + "/{operationId}";

    [OpsParityRoute("POST")]
    public const string SubmitTemplate = OperationTemplate + "/submit";

    [OpsParityRoute("POST")]
    public const string RollbackTemplate = OperationTemplate + "/rollback";

    // Not a pinned contract: a 404 or 501 means this server has no converge action.
    [OpsParityRoute("POST")]
    public const string PlatformReleaseConverge = "api/v1/admin/platform-release/converge";

    public static string Operation(string operationId) =>
        OperationTemplate.Replace("{operationId}", Uri.EscapeDataString(operationId), StringComparison.Ordinal);

    public static string Submit(string operationId) =>
        SubmitTemplate.Replace("{operationId}", Uri.EscapeDataString(operationId), StringComparison.Ordinal);

    public static string Rollback(string operationId) =>
        RollbackTemplate.Replace("{operationId}", Uri.EscapeDataString(operationId), StringComparison.Ordinal);

    public static string OperationsList(string? status = null, string? kind = null, int? page = null, int? pageSize = null)
    {
        var query = new List<string>(4);
        if (!string.IsNullOrWhiteSpace(status))
        {
            query.Add($"status={Uri.EscapeDataString(status.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(kind))
        {
            query.Add($"kind={Uri.EscapeDataString(kind.Trim())}");
        }

        if (page is { } p)
        {
            query.Add($"page={p}");
        }

        if (pageSize is { } ps)
        {
            query.Add($"pageSize={ps}");
        }

        return query.Count == 0 ? Operations : $"{Operations}?{string.Join('&', query)}";
    }

    public static string PreflightWithDiagnostics(bool includeDiagnostics = true) =>
        includeDiagnostics ? $"{Preflight}?includeDiagnostics=true" : Preflight;
}

public sealed record SubmitDeployOperationRequest
{
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

// A data-affecting rollback can be refused by the server approval gate.
public sealed record RollbackDeployOperationRequest
{
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

// TotalCount and HasMore cover active operations plus a bounded set of recent terminal ones.
public sealed record DeployOperationListResponse
{
    [JsonPropertyName("items")]
    public IReadOnlyList<DeployOperationResponse> Items { get; init; } = [];

    [JsonPropertyName("page")]
    public int Page { get; init; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; init; }

    [JsonPropertyName("totalCount")]
    public int TotalCount { get; init; }

    [JsonPropertyName("hasMore")]
    public bool HasMore { get; init; }
}

public sealed record DeployPreflightResponse
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("readyForCoordinatedDeploy")]
    public bool ReadyForCoordinatedDeploy { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("serverVersion")]
    public string? ServerVersion { get; init; }

    [JsonPropertyName("environment")]
    public string? Environment { get; init; }

    [JsonPropertyName("deploymentMode")]
    public string? DeploymentMode { get; init; }

    [JsonPropertyName("instanceName")]
    public string? InstanceName { get; init; }

    [JsonPropertyName("generatedAt")]
    public DateTimeOffset GeneratedAt { get; init; }

    [JsonPropertyName("readiness")]
    public DeployPreflightReadinessResponse? Readiness { get; init; }

    [JsonPropertyName("migration")]
    public DeployPreflightMigrationResponse? Migration { get; init; }

    [JsonPropertyName("databaseCompatibility")]
    public DeployPreflightDatabaseCompatibilityResponse? DatabaseCompatibility { get; init; }

    [JsonPropertyName("platformRelease")]
    public DeployPreflightPlatformReleaseResponse? PlatformRelease { get; init; }
}

public sealed record DeployPreflightReadinessResponse
{
    [JsonPropertyName("isReady")]
    public bool IsReady { get; init; }

    [JsonPropertyName("statusCode")]
    public int StatusCode { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}

public sealed record DeployPreflightMigrationResponse
{
    [JsonPropertyName("lifecycleStatus")]
    public string LifecycleStatus { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("planAvailable")]
    public bool PlanAvailable { get; init; }

    [JsonPropertyName("upgradeRequired")]
    public bool UpgradeRequired { get; init; }

    [JsonPropertyName("pendingScripts")]
    public IReadOnlyList<string> PendingScripts { get; init; } = [];

    [JsonPropertyName("executedButNotDiscoveredScripts")]
    public IReadOnlyList<string> ExecutedButNotDiscoveredScripts { get; init; } = [];

    [JsonPropertyName("planError")]
    public string? PlanError { get; init; }
}

public sealed record DeployPreflightDatabaseCompatibilityResponse
{
    [JsonPropertyName("isCompatible")]
    public bool IsCompatible { get; init; }

    [JsonPropertyName("engineVersion")]
    public string EngineVersion { get; init; } = string.Empty;

    [JsonPropertyName("postGisVersion")]
    public string? PostGisVersion { get; init; }

    [JsonPropertyName("postGisRasterVersion")]
    public string? PostGisRasterVersion { get; init; }

    [JsonPropertyName("installedExtensions")]
    public IReadOnlyList<string> InstalledExtensions { get; init; } = [];

    [JsonPropertyName("warnings")]
    public IReadOnlyList<string> Warnings { get; init; } = [];

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; init; }
}

public sealed record DeployPreflightPlatformReleaseResponse
{
    [JsonPropertyName("releaseVersion")]
    public string? ReleaseVersion { get; init; }

    [JsonPropertyName("releaseDeclared")]
    public bool ReleaseDeclared { get; init; }

    [JsonPropertyName("isCoVersioned")]
    public bool IsCoVersioned { get; init; }

    [JsonPropertyName("serving")]
    public IReadOnlyList<DeployPreflightPlaneProjectionResponse> Serving { get; init; } = [];

    [JsonPropertyName("execution")]
    public IReadOnlyList<DeployPreflightPlaneProjectionResponse> Execution { get; init; } = [];

    [JsonPropertyName("skewedIds")]
    public IReadOnlyList<string> SkewedIds { get; init; } = [];
}

public sealed record DeployPreflightPlaneProjectionResponse
{
    [JsonPropertyName("observationStatus")]
    public string? ObservationStatus { get; init; }

    [JsonPropertyName("observedArtifactReference")]
    public string? ObservedArtifactReference { get; init; }

    [JsonPropertyName("observedImageId")]
    public string? ObservedImageId { get; init; }

    [JsonPropertyName("serverVersion")]
    public string? ServerVersion { get; init; }

    [JsonPropertyName("observedAt")]
    public DateTimeOffset? ObservedAt { get; init; }

    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("runtimeProfile")]
    public string? RuntimeProfile { get; init; }

    [JsonPropertyName("effectiveArtifactReference")]
    public string? EffectiveArtifactReference { get; init; }

    [JsonPropertyName("projectedFromRelease")]
    public bool ProjectedFromRelease { get; init; }

    [JsonPropertyName("skewed")]
    public bool Skewed { get; init; }
}

public sealed record PlatformReleaseConvergeTargetOutcome
{
    [JsonPropertyName("targetId")]
    public string TargetId { get; init; } = string.Empty;

    [JsonPropertyName("outcome")]
    public string Outcome { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("operationId")]
    public string? OperationId { get; init; }
}

// These field names are not a pinned server contract.
public sealed record PlatformReleaseConvergeResponse
{
    [JsonPropertyName("generatedAt")]
    public DateTimeOffset GeneratedAt { get; init; }

    [JsonPropertyName("targets")]
    public IReadOnlyList<PlatformReleaseConvergeTargetOutcome> Targets { get; init; } = [];

    [JsonPropertyName("proposalId")]
    public string? ProposalId { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

// DeployOperationResponse is serialized with MetadataReleaseJsonContext, so it is not registered again here.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(SubmitDeployOperationRequest))]
[JsonSerializable(typeof(RollbackDeployOperationRequest))]
[JsonSerializable(typeof(DeployOperationListResponse))]
[JsonSerializable(typeof(DeployPreflightResponse))]
[JsonSerializable(typeof(PlatformReleaseConvergeResponse))]
public sealed partial class DeployControlJsonContext : JsonSerializerContext
{
}
