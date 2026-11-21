using System.Globalization;
using Honua.Console.Contracts;

namespace Honua.Console.Shell.Models;

public enum AuditChainVerificationStatus
{
    Unverified,
    Recorded,
    Verified,
}

public sealed record PolicyAuditTrailReceipt(
    string Subject,
    string Actor,
    DateTimeOffset? OccurredAt,
    string PreviousState,
    string NewState,
    string? AuditEntryId,
    string? AuditEntryHash,
    AuditChainVerificationStatus ChainStatus,
    string? Summary = null)
{
    public string TransitionDisplay => $"{PreviousState} -> {NewState}";

    public string OccurredAtDisplay => OccurredAt is { } timestamp && timestamp != default
        ? timestamp.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)
        : "Not reported";

    public string ChainStatusLabel => ChainStatus switch
    {
        AuditChainVerificationStatus.Verified => "HMAC audit chain verified",
        AuditChainVerificationStatus.Recorded => "Recorded in tamper-evident audit trail",
        _ => "Audit chain metadata not reported by server",
    };

    public static PolicyAuditTrailReceipt FromAutonomyPolicy(
        OpsAutonomyPolicyResponse policy,
        string? previousMode,
        OpsAutonomyAuditEntry? matchingAudit = null)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var actor = FirstNonBlank(policy.UpdatedBy, matchingAudit?.Actor) ?? "Server-recorded operator";
        var occurredAt = policy.UpdatedAt ?? (matchingAudit is { OccurredAt: var at } && at != default ? at : null);
        var fromMode = FirstNonBlank(policy.PreviousMode, previousMode, matchingAudit?.PreviousMode) ?? "ProposeOnly";
        var toMode = string.IsNullOrWhiteSpace(policy.Mode) ? "Unknown" : policy.Mode;
        var entryId = FirstNonBlank(policy.AuditEntryId, matchingAudit?.EventId);
        var entryHash = FirstNonBlank(policy.AuditEntryHash, matchingAudit?.AuditEntryHash);
        var verified = policy.AuditChainVerified ?? matchingAudit?.ChainVerified;
        var chainStatus = ResolveChainStatus(verified, entryId, entryHash);
        var boundsSummary = policy.MaxAutoActionsPerWindow > 0 && policy.WindowSeconds > 0
            ? $"Bounded defaults: max {policy.MaxAutoActionsPerWindow} actions / {policy.WindowSeconds}s, max blast radius {policy.MaxBlastRadius}."
            : null;

        return new PolicyAuditTrailReceipt(
            Subject: policy.Rule,
            Actor: actor,
            OccurredAt: occurredAt,
            PreviousState: fromMode,
            NewState: toMode,
            AuditEntryId: entryId,
            AuditEntryHash: entryHash,
            ChainStatus: chainStatus,
            Summary: boundsSummary);
    }

    public static PolicyAuditTrailReceipt FromRevokedSupportSession(
        SupportTicketResponse ticket,
        string previousMode)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        var session = ticket.Trust?.DelegatedSession;
        var actor = FirstNonBlank(session?.RevokedBy, ticket.AssignedOperatorId) ?? "Operator session";
        var occurredAt = session?.RevokedAt ?? (ticket.UpdatedAt != default ? ticket.UpdatedAt : null);
        var entryId = session?.AuditEntryId;
        var entryHash = session?.AuditEntryHash;
        var chainStatus = ResolveChainStatus(session?.AuditChainVerified, entryId, entryHash);
        var subject = string.IsNullOrWhiteSpace(session?.SessionId)
            ? $"ticket:{ticket.Id}"
            : $"{ticket.Id} ({session.SessionId})";

        return new PolicyAuditTrailReceipt(
            Subject: subject,
            Actor: actor,
            OccurredAt: occurredAt,
            PreviousState: string.IsNullOrWhiteSpace(previousMode) ? "active" : previousMode,
            NewState: "revoked",
            AuditEntryId: entryId,
            AuditEntryHash: entryHash,
            ChainStatus: chainStatus,
            Summary: "Break-glass support session credentials revoked.");
    }

    private static AuditChainVerificationStatus ResolveChainStatus(
        bool? verified,
        string? entryId,
        string? entryHash)
    {
        if (verified == true)
        {
            return AuditChainVerificationStatus.Verified;
        }

        if (!string.IsNullOrWhiteSpace(entryHash) || !string.IsNullOrWhiteSpace(entryId))
        {
            return AuditChainVerificationStatus.Recorded;
        }

        return AuditChainVerificationStatus.Unverified;
    }

    private static string? FirstNonBlank(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate.Trim();
            }
        }

        return null;
    }
}
