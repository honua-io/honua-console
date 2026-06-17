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
//   GET  /operations/{operationId}            -> DeployOperationResponse (status)
//   POST /operations/{operationId}/submit     -> DeployOperationResponse (approve+submit)
//   POST /operations/{operationId}/rollback   -> DeployOperationResponse (rollback; 403 if gated)
//
// The server intentionally exposes NO list-all endpoint: a deploy operation is read
// by its durable id. The Console approval list therefore derives its pending
// operations from a caller-supplied id set (e.g. the release-operation lifecycle's
// deployOperationId), not from a server list, and otherwise renders an honest
// unsupported/missing state.

/// <summary>
/// Concrete v1 routes for the honua-server deploy-control admin endpoints, kept in
/// one place so the client and tests share the exact server paths.
/// </summary>
public static class DeployControlAdminRoutes
{
    public const string Prefix = "api/v1/admin/deploy";

    public static string Operation(string operationId) =>
        $"{Prefix}/operations/{Uri.EscapeDataString(operationId)}";

    public static string Submit(string operationId) =>
        $"{Prefix}/operations/{Uri.EscapeDataString(operationId)}/submit";

    public static string Rollback(string operationId) =>
        $"{Prefix}/operations/{Uri.EscapeDataString(operationId)}/rollback";
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
/// Source-generated JSON context for the deploy-control request shapes (trim/AOT
/// safe). The <c>DeployOperationResponse</c> read shape is served by
/// <see cref="MetadataReleaseJsonContext"/> and is not duplicated here.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(SubmitDeployOperationRequest))]
[JsonSerializable(typeof(RollbackDeployOperationRequest))]
public sealed partial class DeployControlJsonContext : JsonSerializerContext
{
}
