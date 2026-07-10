using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Projects the honua-server <see cref="DeployOperationResponse"/> read shape onto the
/// Console approval-surface <see cref="DeployOperationProposal"/>. Field provenance is
/// the deploy-control operation record and its embedded metadata-release context; the
/// mapper never invents data, surfacing "unknown" / null when the server omits a field.
/// </summary>
public static class DeployApprovalMapper
{
    public static DeployOperationProposal MapProposal(DeployOperationResponse operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var release = operation.MetadataRelease;
        var target = operation.Target;
        var lifecycle = DeployOperationPresentation.MapLifecycle(operation.Status);

        // Environment/revision provenance prefers the metadata-release context (a promotion
        // op) and falls back to the deploy target (a server-upgrade/rollback op); the server
        // omits whichever side does not apply to this operation's kind, so BOTH must be
        // treated as possibly absent (console#290, honua-server PR #2577 null-omission
        // contract) — never default an absent value to "" and read it as a real revision.
        var environment = release is { TargetEnvironment: { Length: > 0 } env }
            ? env
            : target is { Environment: { Length: > 0 } targetEnv } ? targetEnv : "unknown";
        var desiredRevision = release is { DesiredRevision: { Length: > 0 } rev }
            ? rev
            : target is { DesiredRevision: { Length: > 0 } targetRev } ? targetRev : "unknown";
        var currentRevision = target?.CurrentRevision;

        var evidence = release?.EvidenceRefs is { Count: > 0 } refs
            ? refs.Select(r => new DeployOperationEvidenceLink(r.Kind, r.RefId, r.Uri)).ToArray()
            : [];

        DeployOperationRollbackSummary? rollback = release?.RollbackPlan is { } plan
            ? new DeployOperationRollbackSummary(
                GitOpsReleaseMapper.MapRollbackClassification(plan.Class),
                plan.IsDataAffecting,
                // The server already folds IsDataAffecting into RequiresExplicitApproval, but
                // keep the OR explicit so the surface never under-reports the confirmation gate.
                plan.RequiresExplicitApproval || plan.IsDataAffecting,
                plan.Steps,
                plan.EvidenceRequired,
                plan.ApprovalPolicyRef)
            : null;

        return new DeployOperationProposal(
            OperationId: operation.OperationId,
            Lifecycle: lifecycle,
            RawStatus: operation.Status,
            Kind: operation.Kind,
            Priority: operation.Priority,
            // Service prefers the deploy target's name (a real server-upgrade descriptor);
            // when the operation carries no target (a metadata promotion), the operation Kind
            // is the closest durable descriptor. Surface it rather than guessing.
            Service: target is { TargetName: { Length: > 0 } targetName }
                ? targetName
                : string.IsNullOrWhiteSpace(operation.Kind) ? "deploy" : operation.Kind,
            Environment: environment,
            DesiredRevision: desiredRevision,
            CurrentRevision: currentRevision,
            Action: DeriveAction(lifecycle),
            ChangeSummary: BuildChangeSummary(operation, environment, desiredRevision),
            RequestedBy: operation.RequestedBy,
            Reason: operation.Reason,
            PrUrl: release?.PrUrl,
            CommitSha: release?.CommitSha,
            Evidence: evidence,
            RollbackPlan: rollback,
            Warnings: operation.Warnings,
            BlockingReasons: operation.BlockingReasons,
            CreatedAt: operation.CreatedAt,
            UpdatedAt: operation.UpdatedAt);
    }

    private static string DeriveAction(DeployOperationLifecycle lifecycle) => lifecycle switch
    {
        DeployOperationLifecycle.RollbackRequested or DeployOperationLifecycle.RolledBack => "rollback",
        _ => "deploy",
    };

    private static string BuildChangeSummary(DeployOperationResponse operation, string environment, string desiredRevision)
    {
        if (!string.IsNullOrWhiteSpace(operation.Reason))
        {
            return operation.Reason!;
        }

        if (operation.MetadataRelease is { PackageId: { Length: > 0 } packageId })
        {
            return $"Release package {packageId} -> {environment} @ {desiredRevision}.";
        }

        return $"Deploy operation {operation.OperationId} -> {environment} @ {desiredRevision}.";
    }
}
