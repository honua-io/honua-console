using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

// SHIM(honua-sdk-dotnet): The honua-server deploy-control admin endpoints
// (DeployControlEndpoints) are not yet projected to honua-sdk-dotnet. These
// records/routes mirror the server HTTP surface so the Console human-in-the-loop
// approval surface can read an operation and POST the governed submit/rollback
// actions through the single Console shim boundary until the SDK projection lands.
//
// Route map (concrete v1), all under /api/v1/admin/deploy, admin-authorized
// (X-API-Key); the operation read/response shape (DeployOperationResponse) is shared
// with the metadata release contracts and lives in MetadataReleaseContracts.cs:
//   GET  /operations                          -> DeployOperationListResponse (paged list, honua-server#2577)
//   GET  /operations/{operationId}            -> DeployOperationResponse (status)
//   POST /operations/{operationId}/submit     -> DeployOperationResponse (approve+submit)
//   POST /operations/{operationId}/rollback   -> DeployOperationResponse (rollback; 403 if gated)
//   GET  /preflight                           -> DeployPreflightResponse (console#290)
//
// console#290 (deploy cockpit completion): the list endpoint above landed in
// honua-server PR #2577 (merged) — the release-scrape workaround this file used to
// document is retired; the Console approval surface now reads the durable list
// directly with the tracked-id fallback kept only for callers holding an id the
// list's bounded materialization window has aged out.
//
// PlatformReleaseConverge is speculative: honua-server#2564 (platform-release
// converge API) is still open at the time this route/DTO pair was authored. The
// route constant and response shape below are a best-effort placeholder so the
// Console UI can wire the capability-detected "converge" action end to end (a 404
// from an older/pre-#2564 server maps to Unsupported today, which is what every real
// server currently returns). Reconcile the response DTO's field names against the
// server's actual OpenAPI contract in the SAME PR that consumes #2564 once merged;
// do not treat the shape below as a pinned contract.

/// <summary>
/// Concrete v1 routes for the honua-server deploy-control admin endpoints, kept in
/// one place so the client and tests share the exact server paths.
/// </summary>
public static class DeployControlAdminRoutes
{
    public const string Prefix = "api/v1/admin/deploy";

    /// <summary>The deploy preflight route (honua-server, pre-existing; console#290 first consumer).</summary>
    [OpsParityRoute("GET")]
    public const string Preflight = Prefix + "/preflight";

    /// <summary>The route for listing deploy operations.</summary>
    [OpsParityRoute("GET")]
    public const string Operations = Prefix + "/operations";

    /// <summary>The route template for reading one deploy operation.</summary>
    [OpsParityRoute("GET")]
    public const string OperationTemplate = Operations + "/{operationId}";

    /// <summary>The route template for human submission of one deploy operation.</summary>
    [OpsParityRoute("POST")]
    public const string SubmitTemplate = OperationTemplate + "/submit";

    /// <summary>The route template for requesting rollback of one deploy operation.</summary>
    [OpsParityRoute("POST")]
    public const string RollbackTemplate = OperationTemplate + "/rollback";

    /// <summary>
    /// Speculative platform-release converge route (honua-server#2564, not yet merged at the time
    /// this constant was authored). Every server today returns 404/501 for this path, which the
    /// client maps to <c>Unsupported</c> — the capability-detected "unavailable" state.
    /// </summary>
    [OpsParityRoute("POST")]
    public const string PlatformReleaseConverge = "api/v1/admin/platform-release/converge";

    public static string Operation(string operationId) =>
        OperationTemplate.Replace("{operationId}", Uri.EscapeDataString(operationId), StringComparison.Ordinal);

    public static string Submit(string operationId) =>
        SubmitTemplate.Replace("{operationId}", Uri.EscapeDataString(operationId), StringComparison.Ordinal);

    public static string Rollback(string operationId) =>
        RollbackTemplate.Replace("{operationId}", Uri.EscapeDataString(operationId), StringComparison.Ordinal);

    /// <summary>
    /// Builds the paged deploy-operations list route (honua-server PR #2577's pinned contract):
    /// <c>GET /operations?status=&amp;kind=&amp;page=&amp;pageSize=</c>. All parameters are optional;
    /// omitted filters are left off the query string rather than sent as empty values.
    /// </summary>
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

    /// <summary>Builds the preflight route, optionally requesting the operator-diagnostics fields.</summary>
    public static string PreflightWithDiagnostics(bool includeDiagnostics = true) =>
        includeDiagnostics ? $"{Preflight}?includeDiagnostics=true" : Preflight;
}

/// <summary>
/// Request payload for approving (submitting) a paused deploy-control operation.
/// Mirrors the server <c>SubmitDeployOperationRequest</c> (an optional free-form
/// reason captured in the decision audit).
/// </summary>
public sealed record SubmitDeployOperationRequest
{
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

/// <summary>
/// Request payload for rolling back a deploy-control operation. Mirrors the server
/// <c>RollbackDeployOperationRequest</c>. A data-affecting rollback is gated by the
/// server's OperatorApprovalGate; the reason is captured in the decision audit.
/// </summary>
public sealed record RollbackDeployOperationRequest
{
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

/// <summary>
/// Paged, newest-first collection of durable deploy workflow operations. Pinned to
/// honua-server PR #2577's wire contract: <c>null</c>-valued properties on each
/// <see cref="DeployOperationResponse"/> item are OMITTED from the payload
/// (<c>DefaultIgnoreCondition = WhenWritingNull</c>) — treat an absent key as
/// <c>null</c>, never as a zero/empty default. <c>totalCount</c>/<c>hasMore</c> are
/// computed over a bounded materialization window (all active operations + the most
/// recent terminal operations); unbounded historical paging past the window is not
/// guaranteed.
/// </summary>
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

/// <summary>Instance-scoped deploy preflight response for coordinated Honua rollouts (honua-server DeployPreflightResponse).</summary>
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

/// <summary>Readiness summary embedded in a deploy preflight response.</summary>
public sealed record DeployPreflightReadinessResponse
{
    [JsonPropertyName("isReady")]
    public bool IsReady { get; init; }

    [JsonPropertyName("statusCode")]
    public int StatusCode { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}

/// <summary>Migration and schema alignment summary embedded in a deploy preflight response.</summary>
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

/// <summary>Database compatibility summary embedded in a deploy preflight response.</summary>
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

/// <summary>
/// Cross-plane platform-release consistency summary embedded in a deploy preflight response
/// (ADR-0060 WS2).
/// </summary>
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

/// <summary>One plane element's platform-release projection embedded in a deploy preflight response.</summary>
public sealed record DeployPreflightPlaneProjectionResponse
{
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

/// <summary>
/// Speculative per-target outcome of a platform-release converge action
/// (honua-server#2564, not yet merged — see the file-level remarks above). Reconcile
/// against the real contract once #2564 lands.
/// </summary>
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

/// <summary>
/// Speculative platform-release converge response (honua-server#2564, not yet merged — see the
/// file-level remarks above).
/// </summary>
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

/// <summary>
/// Source-generated JSON context for the deploy-control request/response shapes (trim/AOT
/// safe). The <c>DeployOperationResponse</c> read shape is served by
/// <see cref="MetadataReleaseJsonContext"/> and is not duplicated here.
/// </summary>
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
