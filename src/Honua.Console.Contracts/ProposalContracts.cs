using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

public sealed record ProposalSummaryWire
{
    public string? ProposalId { get; init; }
    public string? Kind { get; init; }
    public string? Status { get; init; }
    public string? RequestedBy { get; init; }
    public string? RequestedByAgent { get; init; }
    public string? Summary { get; init; }
    public string? RiskLevel { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public string? FindingId { get; init; }
    public string? AutonomyRule { get; init; }
    public string? ActionDiscriminator { get; init; }
}

public sealed record ProposalDetailWire
{
    public long Version { get; init; }
    public string? PlanHash { get; init; }
    public bool RequiresReproposal { get; init; }
    public string? ReproposalReason { get; init; }

    public string? ProposalId { get; init; }
    public string? Kind { get; init; }
    public string? Status { get; init; }
    public string? RequestedBy { get; init; }
    public string? RequestedByAgent { get; init; }
    public string? Summary { get; init; }
    public IReadOnlyList<string>? Diff { get; init; }
    public IReadOnlyList<string>? DryRun { get; init; }
    public string? RiskLevel { get; init; }
    public IReadOnlyList<string>? BlockingReasons { get; init; }
    public IReadOnlyList<string>? Warnings { get; init; }
    public string? GuardrailTier { get; init; }
    public string? ResolvedBy { get; init; }
    public string? ResolutionReason { get; init; }
    public string? ExecutionOperationId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? ResolvedAt { get; init; }
    public string? FindingId { get; init; }
    public string? AutonomyRule { get; init; }
    public string? ActionDiscriminator { get; init; }
}

public sealed record ProposalListWire
{
    public IReadOnlyList<ProposalSummaryWire>? Proposals { get; init; }
}

public sealed record RejectProposalWire
{
    public string? Reason { get; init; }
}

public sealed record ConsolePrivateProposalReviewResponse(ProposalDetailWire? Proposal, ConsoleProposalHumanReviewWire? Review);

public sealed record ConsoleProposalHumanReviewWire(
    string ProposalId, long ProposalVersion, string PlanHash, string Capability, DateTimeOffset ExpiresAt)
{
    [JsonRequired]
    public string? TenantId { get; init; }
}
