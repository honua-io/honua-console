using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

// SHIM(honua-sdk-dotnet#166): remove when the published Admin SDK exposes deploy human review.
public sealed class DeployHumanReviewResponse
{
    public required DeployOperationResponse Operation { get; init; }
    public required string Action { get; init; }
    public required OperationHumanReviewGrant Review { get; init; }
}

// Shared operation grant; capability is transient and must never be persisted or logged.
public sealed class OperationHumanReviewGrant
{
    public Guid? TenantId { get; init; }
    public required string ProposalId { get; init; }
    public long ProposalVersion { get; init; }
    public required string PlanHash { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public required string Capability { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(DeployHumanReviewResponse))]
public sealed partial class DeployHumanReviewJsonContext : JsonSerializerContext;
