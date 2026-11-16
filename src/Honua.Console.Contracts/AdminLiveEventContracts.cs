namespace Honua.Console.Contracts;

// SHIM(honua-sdk-dotnet#231): Admin event payloads have no consumed SDK projection.
public sealed record ProposalRealtimeWire
{
    public string? ProposalId { get; init; }
    public string? Kind { get; init; }
    public string? Status { get; init; }
    public string? RequestedBy { get; init; }
    public string? RiskLevel { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
}

// SHIM(honua-sdk-dotnet#231): Admin event payloads have no consumed SDK projection.
public sealed record DeployOperationTransitionWire
{
    public string? OperationId { get; init; }
    public string? TransitionKind { get; init; }
    public string? Status { get; init; }
    public string? CorrelationId { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
}
